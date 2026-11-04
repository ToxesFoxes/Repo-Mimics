using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Mob interactions: enemies talk to each other ─────────────────────────
        // Two calm enemies near each other replay a real exchange between players, line by line —
        // only when the recording contexts link the clips (B answered A). Players overhear it from a distance.
        private sealed class MobDialogLine
        {
            public float FireAt;
            public string Guid;
            public string Speaker;
            public int ViewId;
            public float Duration;
            public bool Played;
        }

        private sealed class MobDialog
        {
            public GameObject GoA;
            public GameObject GoB;
            public string NameA;
            public string NameB;
            public string Kind; // "real exchange" or "context match"
            public readonly List<MobDialogLine> Lines = new List<MobDialogLine>();
            public float EndsAt;
        }

        private MobDialog _mobDialog;
        private float _mobDialogNextCheckAt;
        private float _mobDialogCooldownUntil;

        private const float MobDialogMinGap = 3f;
        private const float MobDialogMaxGap = 14f;
        private const float MobDialogHearRadius = 20f;

        private void DirectorPollMobDialog()
        {
            if (_mobDialog != null)
            {
                AdvanceMobDialog();
                return;
            }

            if (Plugin.configEventMobDialog != null && !Plugin.configEventMobDialog.Value) return;
            if (Time.time < _mobDialogNextCheckAt || Time.time < _mobDialogCooldownUntil) return;
            _mobDialogNextCheckAt = Time.time + 6f;
            if (UnityEngine.Random.value > 0.35f) return;

            TryStartMobDialog(force: false);
        }

        private void AdvanceMobDialog()
        {
            var d = _mobDialog;
            if (d.GoA == null || d.GoB == null)
            {
                DirectorFeed("dialog", $"{d.NameA} ↔ {d.NameB}: cut short (an enemy is gone)", CTextDim);
                EndMobDialog();
                return;
            }

            foreach (var line in d.Lines)
            {
                if (line.Played || Time.time < line.FireAt) continue;
                line.Played = true;
                _directorPlaybackDepth = -1; // a dialog line never spawns persona follow-ups
                SendDirectorPlayback(new DirectorPickResult { SoundGuid = line.Guid, EnemyViewIds = new[] { line.ViewId }, ListenerCount = 0 });
                _directorPlaybackDepth = 0;
                _directorLastEventPlaybackAt = Time.time;
            }

            if (Time.time >= d.EndsAt) EndMobDialog();
        }

        private void EndMobDialog()
        {
            _mobDialog = null;
            _mobDialogCooldownUntil = Time.time + UnityEngine.Random.Range(90f, 150f);
        }

        // force: debug sim — no chance/cooldown, and any two free enemies near the local player will do.
        private void TryStartMobDialog(bool force)
        {
            var sounds = DirectorEligibleSounds().Where(s => s.SpeakerActor >= 0 && ClipFactor(s, null) > 0f).ToList();
            var chains = FindRecordedExchanges(sounds, out var kind);
            if (chains.Count == 0)
            {
                DirectorNote("dialog", "dialog:none", "no dialog: no recorded exchange between players yet (needs replies in recording context)");
                return;
            }

            var players = DirectorSnapshotPlayers().Where(p => p.Alive).ToList();
            var enemies = DirectorSnapshotEnemies();
            var local = players.FirstOrDefault(p => p.Avatar != null && p.Avatar.isLocal);

            (DirectorEnemy a, DirectorEnemy b, List<DirectorPlayer> listeners) bestPair = (null, null, null);
            foreach (var a in enemies)
            foreach (var b in enemies)
            {
                if (a.ViewId >= b.ViewId) continue;
                var gap = Vector3.Distance(a.Pos, b.Pos);
                if (gap < (force ? 0.5f : MobDialogMinGap) || gap > (force ? 30f : MobDialogMaxGap)) continue;
                if (!force && (IsEnemyBusyWithPlayers(a.Go) || IsEnemyBusyWithPlayers(b.Go))) continue;

                var mid = (a.Pos + b.Pos) * 0.5f;
                var listeners = players.Where(p => Vector3.Distance(p.Pos, mid) <= MobDialogHearRadius).ToList();
                if (listeners.Count == 0) continue;
                if (force && local != null && Vector3.Distance(local.Pos, mid) > 30f) continue;
                if (bestPair.a == null || listeners.Count > bestPair.listeners.Count
                    || (listeners.Count == bestPair.listeners.Count && UnityEngine.Random.value < 0.5f))
                    bestPair = (a, b, listeners);
            }
            if (bestPair.a == null)
            {
                DirectorNote("dialog", "dialog:pair", "no dialog: no two calm enemies near each other with a player in earshot");
                return;
            }

            // A chain at least one listener can hear entirely (has every clip, and isn't muted to their own voice).
            var listenersOk = bestPair.listeners;
            var usable = chains
                .Where(c => listenersOk.Any(l => c.All(s => s.HaveActors.Contains(l.Actor) && (l.HearsSelf || s.SpeakerActor != l.Actor))))
                .OrderByDescending(c => c.Count + UnityEngine.Random.value)
                .ToList();
            if (usable.Count == 0)
            {
                DirectorNote("dialog", "dialog:clips", "no dialog: listeners nearby don't have every clip of an exchange");
                return;
            }

            var chain = usable[0];
            var dialog = new MobDialog
            {
                GoA = bestPair.a.Go,
                GoB = bestPair.b.Go,
                NameA = bestPair.a.Name,
                NameB = bestPair.b.Name,
                Kind = kind
            };

            // Each distinct speaker keeps "their" enemy; speakers alternate A, B, A...
            var speakerSide = new Dictionary<int, int>();
            var t = Time.time + 0.2f;
            foreach (var s in chain)
            {
                if (!speakerSide.ContainsKey(s.SpeakerActor)) speakerSide[s.SpeakerActor] = speakerSide.Count % 2;
                var onA = speakerSide[s.SpeakerActor] == 0;
                dialog.Lines.Add(new MobDialogLine
                {
                    FireAt = t,
                    Guid = s.Guid,
                    Speaker = s.Label,
                    ViewId = onA ? bestPair.a.ViewId : bestPair.b.ViewId,
                    Duration = s.Duration
                });
                var gap = s.Context != null ? Mathf.Clamp(s.Context.GetFloat("replyGap", 0.6f), 0.3f, 1.2f) : 0.6f;
                t += s.Duration + gap;
            }
            dialog.EndsAt = t + 0.5f;
            _mobDialog = dialog;
            _directorTimerSuppressedUntil = Mathf.Max(_directorTimerSuppressedUntil, dialog.EndsAt + 5f);

            var lines = string.Join(" → ", chain.Select(s => s.Label));
            DirectorFeed("dialog", $"{dialog.NameA} ↔ {dialog.NameB}: {lines} ({kind}, {listenersOk.Count} in earshot)", CGreen);
            var decision = new DirectorDecision
            {
                Time = Time.time,
                Reason = "mob dialog",
                Sound = lines,
                Enemies = $"{dialog.NameA}, {dialog.NameB}",
                Listeners = listenersOk.Count,
                Score = chain.Count
            };
            foreach (var line in dialog.Lines)
                decision.Details.Add($"+{line.FireAt - Time.time:F1}s {line.Speaker} on {(line.ViewId == bestPair.a.ViewId ? dialog.NameA : dialog.NameB)} ({line.Duration:F1}s)");
            _directorDecisions.Add(decision);
            if (_directorDecisions.Count > DirectorDecisionsMax) _directorDecisions.RemoveAt(0);
        }

        // Chasing, attacking or noticing someone: not the moment for small talk.
        private bool IsEnemyBusyWithPlayers(GameObject enemyGo)
        {
            var probe = GetStateProbe(enemyGo);
            var state = probe != null ? ReadState(probe) : null;
            return state != null && IsDangerState(state);
        }

        // Real exchanges: clips linked by "replyTo" (B answered A, C answered B...), 2–4 lines, different speakers in a row.
        // Fallback "context match": a phrase said to a teammate followed by someone else's reply.
        private static List<List<DirectorSound>> FindRecordedExchanges(List<DirectorSound> sounds, out string kind)
        {
            var byGuid = sounds.Where(s => s.Context != null).ToDictionary(s => s.Guid);
            var chains = new List<List<DirectorSound>>();
            foreach (var last in byGuid.Values)
            {
                var chain = new List<DirectorSound> { last };
                var current = last;
                while (chain.Count < 4)
                {
                    var prevGuid = current.Context.Get("replyTo");
                    if (prevGuid == null || !byGuid.TryGetValue(prevGuid, out var prev) || prev.SpeakerActor == current.SpeakerActor || chain.Contains(prev)) break;
                    chain.Insert(0, prev);
                    current = prev;
                }
                if (chain.Count >= 2) chains.Add(chain);
            }

            // Drop chains that are just the beginning of a longer one.
            chains = chains.Where(c => !chains.Any(o => o != c && o.Count > c.Count && o.Take(c.Count).SequenceEqual(c))).ToList();
            if (chains.Count > 0)
            {
                kind = "real exchange";
                return chains;
            }

            kind = "context match";
            var openers = byGuid.Values.Where(s => s.Context.Has("toPlayer") && !s.Context.Has("scream")).ToList();
            var replies = byGuid.Values.Where(s => s.Context.Has("reply") && !s.Context.Has("scream")).ToList();
            foreach (var opener in openers.OrderBy(_ => UnityEngine.Random.value).Take(6))
            {
                var reply = replies.FirstOrDefault(r => r.SpeakerActor != opener.SpeakerActor);
                if (reply != null) chains.Add(new List<DirectorSound> { opener, reply });
            }
            return chains;
        }

        private void DirectorSimulateMobDialog()
        {
            if (_mobDialog != null)
            {
                DirectorFeed("sim", "a mob dialog is already running", CTextDim);
                return;
            }
            TryStartMobDialog(force: true);
        }
    }
}
