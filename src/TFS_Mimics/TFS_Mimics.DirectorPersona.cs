using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Whose voice: unwitnessed deaths and sticky identities (host) ─────────
        // A dead player's voice is most unsettling for those who didn't see them die ("wait, they're alive?"),
        // and pointless for those who did. An enemy also tends to keep the voice it used last.
        private sealed class DeathRecord
        {
            public float Time;
            public readonly HashSet<int> Witnesses = new HashSet<int>();
        }

        private static readonly Dictionary<int, DeathRecord> _deathRecords = new Dictionary<int, DeathRecord>();
        private static readonly Dictionary<int, (int actor, float until)> _enemyVoiceIdentity = new Dictionary<int, (int, float)>();
        private const float IdentityStickSeconds = 180f;

        // Host, at the moment of death: who among the living could see it happen.
        private void RecordDeathWitnesses(PlayerAvatar victim, int victimActor)
        {
            var record = new DeathRecord { Time = Time.time };
            var chest = victim.transform.position + Vector3.up;
            foreach (var p in DirectorSnapshotPlayers())
            {
                if (!p.Alive || p.Actor == victimActor) continue;
                var to = chest - p.Eye;
                if (to.magnitude > 15f || Vector3.Angle(p.Forward, to) > 60f) continue;
                if (!HasLineOfSight(p.Eye, chest, victim.gameObject)) continue;
                record.Witnesses.Add(p.Actor);
            }
            _deathRecords[victimActor] = record;
            DirectorNote("death", $"witness:{victimActor}", record.Witnesses.Count == 0
                ? $"{GetPlayerDisplayName(victim.photonView?.Owner)} died unseen"
                : $"{GetPlayerDisplayName(victim.photonView?.Owner)} died in front of {record.Witnesses.Count} player(s)");
        }

        // ListenerFactor for a dead speaker's voice.
        private static float DeadSpeakerFactor(int speakerActor, int listenerActor, List<string> why)
        {
            if (_deathRecords.TryGetValue(speakerActor, out var record) && record.Witnesses.Contains(listenerActor))
            {
                why?.Add("saw them die");
                return 0.5f;
            }
            why?.Add("unwitnessed death");
            return 2f;
        }

        private static float IdentityFactor(int enemyViewId, int speakerActor)
        {
            if (speakerActor < 0 || !_enemyVoiceIdentity.TryGetValue(enemyViewId, out var id) || id.until < Time.time) return 1f;
            return id.actor == speakerActor ? 1.4f : 0.85f;
        }

        // ─── The enemy's own situation vs the clip's context ──────────────────────
        // The phrase should fit the scene it's played in: said to someone → played where a player faces the enemy,
        // said in a group → played to a group, said in danger → played by a hunting enemy.
        private sealed class EnemySituation
        {
            public int Hearers;
            public bool Faced;
            public bool Hunting;
        }

        private static float SituationFactor(RecordingContext c, EnemySituation e, List<string> why)
        {
            if (c == null || e == null) return 1f;
            var f = 1f;

            var mates = c.GetInt("matesCount", -1);
            if (mates >= 0)
            {
                var clipGroup = mates >= 2;
                var sceneGroup = e.Hearers >= 2;
                if (clipGroup == sceneGroup) { f *= 1.15f; why?.Add(sceneGroup ? "group fits" : "one-on-one fits"); }
                else f *= 0.9f;
            }

            var addressed = c.Has("toPlayer") || c.Has("toEnemy");
            if (addressed && e.Faced) { f *= 1.25f; why?.Add("said to someone, enemy is watched"); }
            else if (!addressed && !e.Faced) f *= 1.1f;

            var tense = c.Has("danger") || c.Has("hurt") || c.Has("spotted");
            if (tense && e.Hunting) { f *= 1.3f; why?.Add("tense phrase, hunting enemy"); }
            else if (!tense && !e.Hunting) f *= 1.1f;
            else if (!tense && e.Hunting) f *= 0.9f;

            return Mathf.Clamp(f, 0.6f, 1.8f);
        }

        // ─── Persona rhythm: an enemy that speaks as someone keeps that player's chat rhythm ───
        private static readonly Dictionary<int, List<float>> _speechTimes = new Dictionary<int, List<float>>();
        private int _directorPlaybackDepth; // 0 = normal playback, n = n-th follow-up, -1 = no follow-ups (mob dialog)

        // Host: every new (not loaded-from-disk) clip marks when that player spoke.
        private static void NoteSpeechTiming(CachedAudioEntry entry)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer() || entry == null || entry.SourceActor < 0 || entry.FilePath != null || string.IsNullOrEmpty(entry.SoundGuid)) return;
            if (!_speechTimes.TryGetValue(entry.SourceActor, out var times))
            {
                times = new List<float>();
                _speechTimes[entry.SourceActor] = times;
            }
            times.Add(Time.time);
            if (times.Count > 16) times.RemoveAt(0);
        }

        // Median gap between a player's phrases (only short gaps — a long silence is not "rhythm").
        private static float MedianSpeechGap(int actor)
        {
            if (!_speechTimes.TryGetValue(actor, out var times) || times.Count < 4) return -1f;
            var gaps = new List<float>();
            for (var i = 1; i < times.Count; i++)
            {
                var gap = times[i] - times[i - 1];
                if (gap >= 2f && gap <= 60f) gaps.Add(gap);
            }
            if (gaps.Count < 3) return -1f;
            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        // After any director playback: remember the identity, maybe schedule the next line in that player's rhythm.
        private void DirectorAfterPlayback(string soundGuid, int[] enemyViewIds)
        {
            var voice = FindCachedAudio(soundGuid);
            if (voice == null || voice.SourceActor < 0 || enemyViewIds == null || enemyViewIds.Length == 0) return;

            var lineDuration = voice.SampleRate > 0 ? voice.AudioData.Length / (float)(voice.SampleRate * 2) : 0f;
            foreach (var id in enemyViewIds)
            {
                _enemyVoiceIdentity[id] = (voice.SourceActor, Time.time + IdentityStickSeconds);
                NoteEnemyLine(id, lineDuration);
            }

            if (_directorPlaybackDepth < 0 || _directorPlaybackDepth >= 2) return;
            if (Plugin.configPersonaRhythm != null && !Plugin.configPersonaRhythm.Value) return;
            var gap = MedianSpeechGap(voice.SourceActor);
            if (gap < 0f || UnityEngine.Random.value > 0.4f) return;

            var duration = voice.SampleRate > 0 ? voice.AudioData.Length / (float)(voice.SampleRate * 2) : 0f;
            var delay = Mathf.Clamp(gap, 3f, 30f);
            var enemyGo = GetEnemyGameObjectByViewId(enemyViewIds[0]);
            var name = enemyGo != null ? NormalizeEnemyName(enemyGo.name) : "enemy";
            var speaker = !string.IsNullOrWhiteSpace(voice.SourceName) ? voice.SourceName : voice.SourcePlayerId;
            DirectorQueue(DirectorEventKind.PersonaFollowUp, voice.SourceActor, enemyGo != null ? enemyGo.transform.position : Vector3.zero,
                duration + delay, duration + delay + 0.5f, $"{name} keeps talking as {speaker} (their rhythm ~{gap:F0}s)", enemyViewIds[0]);
            var ev = _directorPending[_directorPending.Count - 1];
            ev.Depth = _directorPlaybackDepth + 1;
            ev.ExcludeGuid = soundGuid;
        }

        private static void DirectorResetPersona()
        {
            _deathRecords.Clear();
            _enemyVoiceIdentity.Clear();
        }
    }
}
