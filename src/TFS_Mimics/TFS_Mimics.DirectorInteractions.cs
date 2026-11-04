using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Player talks to an enemy ─────────────────────────────────────────────
        // A player speaking while looking at an enemy close by gets an answer once they stop:
        // a teammate's voice, or an echo of their own. Tracking runs on every client so the
        // gizmos show it; only the host queues the answer.
        private sealed class TalkTrack
        {
            public int Actor;
            public string Name;
            public PlayerAvatar Avatar;
            public GameObject Enemy;      // enemy being talked to in the current utterance
            public int EnemyViewId = -1;
            public string EnemyName;
            public float TalkTime;        // seconds spoken at that enemy
            public float SilentFor;
            public readonly Dictionary<GameObject, float> LookTime = new Dictionary<GameObject, float>(); // per utterance
            public Vector3 RayOrigin;     // last look ray while talking, for the gizmo
            public Vector3 RayEnd;
            public float RayUntil;
            public float UtteranceStart = -1f; // when the current utterance began (spotting answers to an enemy's line)
            public GameObject ShownEnemy; // kept for the gizmo after the utterance ends
            public string Status;
            public Color StatusColor;
            public float StatusUntil;
        }

        // A hit between an enemy and another enemy / a valuable, kept briefly for the gizmos.
        private sealed class HitLink
        {
            public Vector3 From;
            public Vector3 To;
            public string Label;
            public float Until;
            public Color Color;
        }

        private const float TalkRange = 10f;
        private const float TalkMaxAngle = 25f;
        private const float TalkAimTolerance = 0.9f; // metres an enemy's centre may be off the look ray at close range
        private const float TalkMinSpeech = 0.6f;
        private const float TalkSilenceToAnswer = 0.7f;
        private const float TalkPollInterval = 0.1f;

        private readonly Dictionary<int, TalkTrack> _talkTracks = new Dictionary<int, TalkTrack>();
        private readonly Dictionary<int, float> _directorTalkCooldown = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _directorFightCooldown = new Dictionary<int, float>();
        private readonly List<HitLink> _hitLinks = new List<HitLink>();
        private float _nextTalkPollAt;
        private float _directorHitItemCooldownUntil;

        private void DirectorPollTalk()
        {
            if (!SemiFunc.RunIsLevel())
            {
                _talkTracks.Clear();
                _hitLinks.Clear();
                return;
            }
            if (Time.time < _nextTalkPollAt) return;
            _nextTalkPollAt = Time.time + TalkPollInterval;

            // Cheap check first: nothing to do while nobody talks and no utterance is waiting for its end.
            var anyTalking = false;
            foreach (var avatar in SemiFunc.PlayerGetList())
            {
                if (avatar != null && avatar.voiceChat != null && avatar.voiceChat.isTalking)
                {
                    anyTalking = true;
                    break;
                }
            }
            if (!anyTalking && !_talkTracks.Values.Any(t => t.Enemy != null)) return;

            var enemies = new List<(GameObject go, Vector3 center)>();
            foreach (var go in GetEnemiesList())
                if (go != null) enemies.Add((go, GetEnemyCenter(go)));

            foreach (var p in DirectorSnapshotPlayers())
            {
                if (!_talkTracks.TryGetValue(p.Actor, out var track))
                {
                    track = new TalkTrack { Actor = p.Actor };
                    _talkTracks[p.Actor] = track;
                }
                track.Name = p.Name;
                track.Avatar = p.Avatar;

                var speaking = p.Alive && p.Avatar.voiceChat != null && p.Avatar.voiceChat.isTalking;
                if (speaking)
                {
                    track.SilentFor = 0f;
                    if (track.UtteranceStart < 0f) track.UtteranceStart = Time.time;
                    var target = FindTalkTarget(p, enemies);
                    track.RayOrigin = p.Eye;
                    track.RayEnd = target.go != null ? target.center : p.Eye + p.Forward * TalkRange;
                    track.RayUntil = Time.time + 0.3f;
                    if (target.go == null) continue;

                    // The utterance goes to whoever the player looked at the longest, not the first enemy they glanced at.
                    track.LookTime.TryGetValue(target.go, out var looked);
                    track.LookTime[target.go] = looked + TalkPollInterval;
                    var leader = track.LookTime.OrderByDescending(kv => kv.Value).First();
                    if (track.Enemy != leader.Key)
                    {
                        track.Enemy = leader.Key;
                        track.EnemyViewId = GetEnemyNetViewId(leader.Key);
                        track.EnemyName = NormalizeEnemyName(leader.Key.name);
                    }
                    track.TalkTime = leader.Value;
                    track.ShownEnemy = leader.Key;
                    SetTalkStatus(track, $"talking {track.TalkTime:F1}s", CYellow, 1f);
                    continue;
                }

                if (track.Enemy == null)
                {
                    track.LookTime.Clear();
                    track.UtteranceStart = -1f;
                    continue;
                }
                track.SilentFor += TalkPollInterval;
                if (track.SilentFor < TalkSilenceToAnswer) continue;

                if (track.TalkTime < TalkMinSpeech)
                    SetTalkStatus(track, $"too short ({track.TalkTime:F1}s)", CTextDim, 2f);
                else if (IsDirectorHost)
                    DirectorTryQueueTalkAnswer(track);
                else
                    SetTalkStatus(track, "said something (host decides)", CTextDim, 3f);

                track.Enemy = null;
                track.TalkTime = 0f;
                track.LookTime.Clear();
                track.UtteranceStart = -1f;
            }
        }

        // A player answering an enemy's line keeps the conversation going: the enemy always answers back,
        // up to a few exchanges, then falls silent.
        private readonly Dictionary<int, float> _enemyLineEndsAt = new Dictionary<int, float>();
        private readonly Dictionary<int, (int count, float lastAt)> _talkExchanges = new Dictionary<int, (int, float)>();
        private const int MaxTalkExchanges = 3;

        private void NoteEnemyLine(int viewId, float duration) => _enemyLineEndsAt[viewId] = Time.time + duration;

        // The enemy whose centre sits closest to the player's look ray: within range, a cone, a tolerance that
        // widens a little with distance, and not behind a wall. Nearer enemies win ties.
        private static (GameObject go, Vector3 center) FindTalkTarget(DirectorPlayer p, List<(GameObject go, Vector3 center)> enemies)
        {
            (GameObject go, Vector3 center) best = (null, Vector3.zero);
            var bestScore = float.MaxValue;
            foreach (var e in enemies)
            {
                var to = e.center - p.Eye;
                var dist = to.magnitude;
                if (dist > TalkRange || dist < 0.01f) continue;
                if (Vector3.Angle(p.Forward, to) > TalkMaxAngle) continue;

                var along = Vector3.Dot(to, p.Forward);
                var offRay = (to - p.Forward * along).magnitude;
                var tolerance = TalkAimTolerance + dist * 0.08f;
                if (along <= 0f || offRay > tolerance) continue;
                if (!HasLineOfSight(p.Eye, e.center, e.go)) continue;

                var score = offRay / tolerance + dist / TalkRange * 0.3f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        // The game's vision mask includes physics-object layers, which the enemy's own body can be on —
        // so hits on the target enemy itself don't count as an obstruction.
        private static readonly RaycastHit[] SightHits = new RaycastHit[16];

        private static bool HasLineOfSight(Vector3 eye, Vector3 target, GameObject enemyGo)
        {
            var to = target - eye;
            var dist = to.magnitude;
            if (dist < 0.01f) return true;
            var count = Physics.RaycastNonAlloc(eye, to / dist, SightHits, dist, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var hit = SightHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(enemyGo.transform)) continue;
                var parent = hit.GetComponentInParent<EnemyParent>();
                if (parent != null && parent.gameObject == enemyGo) continue;
                if (hit.GetComponentInParent<PlayerAvatar>() != null) continue; // own body / teammates don't block talking
                return false;
            }
            return true;
        }

        private static Vector3 GetEnemyCenter(GameObject enemyGo)
        {
            var parent = enemyGo.GetComponent<EnemyParent>();
            var enemy = parent != null ? parent.Enemy : enemyGo.GetComponentInChildren<Enemy>();
            if (enemy != null && enemy.CenterTransform != null) return enemy.CenterTransform.position;
            return enemyGo.transform.position + Vector3.up;
        }

        private static void SetTalkStatus(TalkTrack track, string status, Color color, float seconds)
        {
            track.Status = status;
            track.StatusColor = color;
            track.StatusUntil = Time.time + seconds;
        }

        private void DirectorTryQueueTalkAnswer(TalkTrack track)
        {
            if (!DirectorEnabled || (Plugin.configEventTalkAnswer != null && !Plugin.configEventTalkAnswer.Value) || track.EnemyViewId < 0) return;

            var what = $"{track.Name} talked to {track.EnemyName} ({track.TalkTime:F1}s)";

            var exchange = 0;
            if (_enemyLineEndsAt.TryGetValue(track.EnemyViewId, out var lineEnd)
                && track.UtteranceStart >= lineEnd - 1f && track.UtteranceStart - lineEnd <= 6f)
            {
                if (!_talkExchanges.TryGetValue(track.EnemyViewId, out var ex) || Time.time - ex.lastAt > 30f) ex = (0, 0f);
                if (ex.count >= MaxTalkExchanges)
                {
                    _talkExchanges.Remove(track.EnemyViewId);
                    _directorTalkCooldown[track.EnemyViewId] = Time.time + 30f;
                    DirectorFeed("talk", $"{track.EnemyName} falls silent after {MaxTalkExchanges} exchanges with {track.Name}", CTextDim);
                    SetTalkStatus(track, "it went quiet", CTextDim, 3f);
                    return;
                }
                exchange = ex.count + 1;
                _talkExchanges[track.EnemyViewId] = (exchange, Time.time);
            }

            if (exchange == 0 && _directorTalkCooldown.TryGetValue(track.EnemyViewId, out var until) && until > Time.time)
            {
                DirectorNote("talk", $"talk:{track.EnemyViewId}", $"skipped (cooldown {until - Time.time:F0}s): {what}");
                SetTalkStatus(track, "no answer (cooldown)", CTextDim, 3f);
                return;
            }
            if (exchange == 0 && UnityEngine.Random.value > 0.6f)
            {
                DirectorNote("talk", $"talk:{track.EnemyViewId}", $"skipped (60% chance): {what}");
                SetTalkStatus(track, "no answer (chance)", CTextDim, 3f);
                return;
            }

            var sounds = DirectorEligibleSounds();
            var hasOwn = sounds.Any(s => s.SpeakerActor == track.Actor);
            var hasOther = sounds.Any(s => s.SpeakerActor >= 0 && s.SpeakerActor != track.Actor);
            if (!hasOwn && !hasOther)
            {
                DirectorNote("talk", $"talk:{track.EnemyViewId}", $"skipped (no voices yet): {what}");
                SetTalkStatus(track, "no answer (no voices)", CTextDim, 3f);
                return;
            }

            // In a back-and-forth an echo would break the illusion, so it's rare there.
            var echo = hasOwn && (!hasOther || UnityEngine.Random.value < (exchange > 0 ? 0.2f : 0.5f));
            _directorTalkCooldown[track.EnemyViewId] = Time.time + 20f;
            var label = exchange > 0
                ? $"{track.EnemyName} answers {track.Name} back (exchange {exchange}/{MaxTalkExchanges})"
                : $"{track.EnemyName} {(echo ? "echoes" : "answers")} {track.Name}";
            DirectorQueue(DirectorEventKind.TalkAnswer, track.Actor, track.Enemy.transform.position, exchange > 0 ? 0.6f : 1f, exchange > 0 ? 1.2f : 2f, label, track.EnemyViewId);
            _directorPending[_directorPending.Count - 1].VictimVoice = echo;
            SetTalkStatus(track, exchange > 0 ? $"conversation {exchange}/{MaxTalkExchanges}" : echo ? "echo queued" : "answer queued", CGreen, 4f);
        }

        // ─── Enemy hits enemy / valuable (host: HurtCollider runs enemy logic on the master) ───
        internal static void DirectorOnEnemyHitEnemy(Enemy attacker, Enemy victim)
        {
            var self = DirectorOwner;
            if (self == null || attacker == null || victim == null || !IsDirectorHost || !DirectorEnabled || !SemiFunc.RunIsLevel()) return;
            if (Plugin.configEventEnemyFight != null && !Plugin.configEventEnemyFight.Value) return;

            var victimGo = victim.EnemyParent != null ? victim.EnemyParent.gameObject : victim.gameObject;
            var attackerGo = attacker.EnemyParent != null ? attacker.EnemyParent.gameObject : attacker.gameObject;
            var viewId = GetEnemyNetViewId(victimGo);
            if (viewId < 0) return;

            var what = $"{NormalizeEnemyName(attackerGo.name)} hits {NormalizeEnemyName(victimGo.name)}";
            self.AddHitLink(attacker.transform.position, victim.transform.position, what);
            if (self._directorFightCooldown.TryGetValue(viewId, out var until) && until > Time.time)
            {
                self.DirectorNote("fight", $"fight:{viewId}", $"skipped (cooldown {until - Time.time:F0}s): {what}");
                return;
            }
            self._directorFightCooldown[viewId] = Time.time + 15f;
            if (UnityEngine.Random.value > 0.6f)
            {
                self.DirectorNote("fight", $"fight:{viewId}", $"skipped (60% chance): {what}");
                return;
            }
            self.DirectorQueue(DirectorEventKind.EnemyFight, -1, victim.transform.position, 0.2f, 0.6f, what, viewId);
        }

        internal static void DirectorOnEnemyHitObject(Enemy attacker, PhysGrabObject obj)
        {
            var self = DirectorOwner;
            if (self == null || attacker == null || obj == null || !obj.isValuable || !IsDirectorHost || !DirectorEnabled || !SemiFunc.RunIsLevel()) return;
            if (Plugin.configEventEnemyHitItem != null && !Plugin.configEventEnemyHitItem.Value) return;

            var attackerGo = attacker.EnemyParent != null ? attacker.EnemyParent.gameObject : attacker.gameObject;
            var viewId = GetEnemyNetViewId(attackerGo);
            if (viewId < 0) return;

            var owner = obj.lastPlayerGrabbing;
            var ownerActor = owner != null ? owner.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1 : -1;
            var what = $"{NormalizeEnemyName(attackerGo.name)} hits {obj.name.Replace("(Clone)", "").Trim()}" +
                       (owner != null ? $" ({GetPlayerDisplayName(owner.photonView?.Owner)}'s)" : "");
            self.AddHitLink(attacker.transform.position, obj.transform.position, what);
            if (Time.time < self._directorHitItemCooldownUntil)
            {
                self.DirectorNote("hititem", $"hititem:{viewId}", $"skipped (cooldown {self._directorHitItemCooldownUntil - Time.time:F0}s): {what}");
                return;
            }
            self._directorHitItemCooldownUntil = Time.time + 20f;
            if (UnityEngine.Random.value > 0.5f)
            {
                self.DirectorNote("hititem", $"hititem:{viewId}", $"skipped (50% chance): {what}");
                return;
            }
            self.DirectorQueue(DirectorEventKind.EnemyHitItem, ownerActor, obj.transform.position, 0.3f, 0.8f, what, viewId);
        }

        private void AddHitLink(Vector3 from, Vector3 to, string label) => AddHitLink(from, to, label, GizmoHitColor);

        private void AddHitLink(Vector3 from, Vector3 to, string label, Color color)
        {
            // HurtColliders tick every 0.05s while overlapping — refresh the same link instead of stacking.
            var existing = _hitLinks.FirstOrDefault(l => l.Label == label);
            if (existing == null)
            {
                existing = new HitLink { Label = label };
                _hitLinks.Add(existing);
                if (_hitLinks.Count > 12) _hitLinks.RemoveAt(0);
            }
            existing.From = from;
            existing.To = to;
            existing.Until = Time.time + 3f;
            existing.Color = color;
        }

        private DirectorRequest BuildInteractionRequest(DirectorPendingEvent ev, string reason)
        {
            switch (ev.Kind)
            {
                case DirectorEventKind.TalkAnswer:
                    // Echo: the speaker's own voice comes back. Answer: someone else's voice replies.
                    return new DirectorRequest
                    {
                        Reason = reason,
                        SoundFilter = ev.VictimVoice
                            ? (System.Func<DirectorSound, bool>)(s => s.SpeakerActor == ev.Actor)
                            : s => s.SpeakerActor >= 0 && s.SpeakerActor != ev.Actor,
                        ForcedEnemyViewId = ev.EnemyViewId,
                        IgnoreEnemyVisibility = true
                    };
                case DirectorEventKind.EnemyHitItem:
                {
                    // The loot's last carrier "protests" from the enemy that smashed it; anyone's voice otherwise.
                    var ownerHasVoice = ev.Actor >= 0 && DirectorEligibleSounds().Any(s => s.SpeakerActor == ev.Actor);
                    return new DirectorRequest
                    {
                        Reason = reason,
                        SoundFilter = ownerHasVoice ? (System.Func<DirectorSound, bool>)(s => s.SpeakerActor == ev.Actor) : s => s.SpeakerActor >= 0,
                        ForcedEnemyViewId = ev.EnemyViewId,
                        IgnoreEnemyVisibility = true
                    };
                }
                default:
                    // Enemy fight: the one that got hit cries out in a player's voice.
                    return BuildLureRequest(ev, reason, lookUnder: false);
            }
        }

        private void DirectorSimulateInteraction(DirectorEventKind kind, PlayerAvatar local, int actor, string label)
        {
            var nearest = DirectorSnapshotEnemies().OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault();
            if (nearest == null) { DirectorFeed("sim", "no free enemy nearby", CTextDim); return; }

            switch (kind)
            {
                case DirectorEventKind.TalkAnswer:
                {
                    var echo = UnityEngine.Random.value < 0.5f;
                    DirectorQueue(kind, actor, nearest.Pos, 0.5f, 1f, label + $" {(echo ? "echo" : "answer")} from {nearest.Name}", nearest.ViewId);
                    _directorPending[_directorPending.Count - 1].VictimVoice = echo;
                    break;
                }
                case DirectorEventKind.EnemyFight:
                    AddHitLink(local.transform.position, nearest.Pos, label + $" → {nearest.Name}");
                    DirectorQueue(kind, -1, nearest.Pos, 0.5f, 1f, label + $" → {nearest.Name}", nearest.ViewId);
                    break;
                default:
                    AddHitLink(nearest.Pos, local.transform.position, label + $" by {nearest.Name}");
                    DirectorQueue(kind, actor, nearest.Pos, 0.5f, 1f, label + $" by {nearest.Name}", nearest.ViewId);
                    break;
            }
        }
    }
}
