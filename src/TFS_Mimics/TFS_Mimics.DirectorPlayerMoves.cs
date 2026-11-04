using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── What a player does about an enemy: flee, approach, hide (host, 4 Hz) ──
        private sealed class PlayerEnemyTrack
        {
            public readonly List<(float time, float dist)> Distances = new List<(float, float)>();
            public float HiddenSince = -1f;
            public bool HideReported;
        }

        private readonly Dictionary<(int actor, int viewId), PlayerEnemyTrack> _playerEnemyTracks = new Dictionary<(int, int), PlayerEnemyTrack>();
        private readonly Dictionary<(int actor, DirectorEventKind kind), float> _playerMoveCooldown = new Dictionary<(int, DirectorEventKind), float>();
        private float _nextPlayerMovePollAt;

        private static readonly Color MoveFleeColor = new Color(1f, 0.6f, 0.2f, 1f);
        private static readonly Color MoveApproachColor = new Color(0.4f, 1f, 0.5f, 1f);
        private static readonly Color MoveHideColor = new Color(0.4f, 0.6f, 1f, 1f);

        private void DirectorPollPlayerMoves()
        {
            if (Time.time < _nextPlayerMovePollAt) return;
            _nextPlayerMovePollAt = Time.time + 0.25f;
            if (Plugin.configEventPlayerMoves != null && !Plugin.configEventPlayerMoves.Value) return;

            var enemies = new List<(GameObject go, int viewId, Vector3 center, bool hunting, bool searching)>();
            foreach (var go in GetEnemiesList())
            {
                if (go == null) continue;
                var viewId = GetEnemyNetViewId(go);
                if (viewId < 0) continue;
                var probe = GetStateProbe(go);
                var state = probe != null ? ReadState(probe) : null;
                var hunting = state != null && IsDangerState(state);
                var searching = state != null && (state.Contains("Search") || state.Contains("LookUnder") || state.Contains("Investigate"));
                enemies.Add((go, viewId, GetEnemyCenter(go), hunting, searching));
            }
            if (enemies.Count == 0) return;

            var seen = new HashSet<(int, int)>();
            foreach (var p in DirectorSnapshotPlayers())
            {
                if (!p.Alive || p.Avatar == null) continue;
                foreach (var e in enemies)
                {
                    var dist = Vector3.Distance(p.Pos, e.center);
                    if (dist > 15f) continue;
                    var key = (p.Actor, e.viewId);
                    seen.Add(key);
                    if (!_playerEnemyTracks.TryGetValue(key, out var track))
                    {
                        track = new PlayerEnemyTrack();
                        _playerEnemyTracks[key] = track;
                    }
                    track.Distances.Add((Time.time, dist));
                    while (track.Distances.Count > 0 && Time.time - track.Distances[0].time > 2f) track.Distances.RemoveAt(0);

                    var name = NormalizeEnemyName(e.go.name);
                    var then = track.Distances[0];
                    var span = Time.time - then.time;
                    var delta = dist - then.dist; // > 0: getting away

                    // Flee: sprinting away from an enemy that is after someone.
                    if (e.hunting && p.Avatar.isSprinting && span >= 1.2f && delta >= 2f && dist <= 14f)
                        TryQueuePlayerMove(DirectorEventKind.PlayerFlee, p, e.viewId, e.center, $"{p.Name} runs from {name}", MoveFleeColor);

                    // Approach: walking up to a calm enemy while looking at it.
                    else if (!e.hunting && span >= 1.2f && delta <= -2f && dist >= 3f && dist <= 12f
                             && Vector3.Angle(p.Forward, e.center - p.Eye) <= 30f)
                        TryQueuePlayerMove(DirectorEventKind.PlayerApproach, p, e.viewId, e.center, $"{p.Name} walks up to {name}", MoveApproachColor);

                    // Hide: crouched or crawling out of sight of an enemy that's hunting or searching nearby.
                    var hidden = (e.hunting || e.searching) && dist <= 10f && (p.Avatar.isCrouching || p.Avatar.isCrawling)
                                 && !HasLineOfSight(p.Eye, e.center, e.go);
                    if (!hidden)
                    {
                        track.HiddenSince = -1f;
                        track.HideReported = false;
                    }
                    else
                    {
                        if (track.HiddenSince < 0f) track.HiddenSince = Time.time;
                        if (!track.HideReported && Time.time - track.HiddenSince >= 1.5f)
                        {
                            track.HideReported = true;
                            TryQueuePlayerMove(DirectorEventKind.PlayerHide, p, e.viewId, e.center, $"{p.Name} hides from {name}", MoveHideColor);
                        }
                    }
                }
            }

            foreach (var k in _playerEnemyTracks.Keys.Where(k => !seen.Contains(k)).ToList()) _playerEnemyTracks.Remove(k);
        }

        private void TryQueuePlayerMove(DirectorEventKind kind, DirectorPlayer p, int viewId, Vector3 enemyPos, string label, Color color)
        {
            var tag = kind.ToString().ToLowerInvariant();
            if (_playerMoveCooldown.TryGetValue((p.Actor, kind), out var until) && until > Time.time) return; // per-tick spam, no note
            _playerMoveCooldown[(p.Actor, kind)] = Time.time + 25f;
            AddHitLink(p.Pos, enemyPos, label, color);
            if (UnityEngine.Random.value > 0.6f)
            {
                DirectorNote(tag, $"{tag}:{p.Actor}", $"skipped (60% chance): {label}");
                return;
            }
            var delay = kind == DirectorEventKind.PlayerFlee ? 0.3f : kind == DirectorEventKind.PlayerHide ? 1.5f : 0.8f;
            DirectorQueue(kind, p.Actor, p.Pos, delay, delay + 0.7f, label, viewId);
        }

        // ─── An enemy got hurt: by itself / the level, or by a player ─────────────
        private readonly Dictionary<int, float> _enemyHurtCooldown = new Dictionary<int, float>();

        internal static void DirectorOnEnemyHurt(Enemy enemy, int damage, HurtCollider collider, PhysGrabObjectImpactDetector impact)
        {
            var self = DirectorOwner;
            if (self == null || enemy == null || !IsDirectorHost || !DirectorEnabled || !SemiFunc.RunIsLevel()) return;

            PlayerAvatar player = null;
            string cause;
            if (collider != null)
            {
                if (collider.enemyHost != null) return; // enemy vs enemy is the "fight" event
                player = collider.playerCausingHurtOverride != null ? collider.playerCausingHurtOverride : collider.playerCausingHurt;
                cause = player != null ? "hit" : collider.deathPit ? "fell into a pit" : "trap";
            }
            else if (impact != null)
            {
                var obj = impact.physGrabObject;
                var ownBody = obj != null && obj.GetComponent<EnemyRigidbody>() is EnemyRigidbody body && body.enemy == enemy;
                if (ownBody) cause = "crashed into something";
                else if (impact.isEnemy) cause = "bumped into another enemy";
                else
                {
                    player = obj != null ? obj.lastPlayerGrabbing : null;
                    var item = obj != null ? obj.name.Replace("(Clone)", "").Trim() : "something";
                    cause = player != null ? $"hit with {item}" : $"hit by {item}";
                }
            }
            else cause = "hurt itself";

            var enemyGo = enemy.EnemyParent != null ? enemy.EnemyParent.gameObject : enemy.gameObject;
            var viewId = GetEnemyNetViewId(enemyGo);
            if (viewId < 0) return;
            var name = NormalizeEnemyName(enemyGo.name);
            var byPlayer = player != null;
            var label = byPlayer ? $"{GetPlayerDisplayName(player.photonView?.Owner)} {cause} {name} (-{damage})" : $"{name} {cause} (-{damage})";
            if (!byPlayer && cause == "hurt itself") label = $"{name} got hurt (-{damage})";

            var enabled = byPlayer
                ? Plugin.configEventEnemyHurtByPlayer == null || Plugin.configEventEnemyHurtByPlayer.Value
                : Plugin.configEventEnemySelfHurt == null || Plugin.configEventEnemySelfHurt.Value;
            if (!enabled) return;

            var tag = byPlayer ? "enemyhit" : "selfhurt";
            if (self._enemyHurtCooldown.TryGetValue(viewId, out var until) && until > Time.time)
            {
                self.DirectorNote(tag, $"{tag}:{viewId}", $"skipped (cooldown {until - Time.time:F0}s): {label}");
                return;
            }
            self._enemyHurtCooldown[viewId] = Time.time + (byPlayer ? 15f : 20f);
            if (UnityEngine.Random.value > (byPlayer ? 0.6f : 0.7f))
            {
                self.DirectorNote(tag, $"{tag}:{viewId}", $"skipped (chance): {label}");
                return;
            }

            var attacker = byPlayer ? player.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1 : -1;
            if (byPlayer) self.AddHitLink(player.transform.position, enemy.transform.position, label, GizmoHitColor);
            self.DirectorQueue(byPlayer ? DirectorEventKind.EnemyHurtByPlayer : DirectorEventKind.EnemySelfHurt,
                attacker, enemy.transform.position, 0.2f, 0.5f, label, viewId);
            // By a player: half the time a teammate pleads "stop, it's me!", half the time the attacker's own scream.
            if (byPlayer) self._directorPending[self._directorPending.Count - 1].VictimVoice = UnityEngine.Random.value < 0.5f;
        }

        private DirectorRequest BuildMoveRequest(DirectorPendingEvent ev, string reason)
        {
            switch (ev.Kind)
            {
                case DirectorEventKind.EnemySelfHurt:
                    // The enemy complains in anyone's voice.
                    return new DirectorRequest { Reason = reason, SoundFilter = s => s.SpeakerActor >= 0, ForcedEnemyViewId = ev.EnemyViewId, IgnoreEnemyVisibility = true };
                case DirectorEventKind.EnemyHurtByPlayer:
                {
                    var hasOther = DirectorEligibleSounds().Any(s => s.SpeakerActor >= 0 && s.SpeakerActor != ev.Actor);
                    var attackerScream = ev.VictimVoice || !hasOther;
                    return new DirectorRequest
                    {
                        Reason = reason,
                        SoundFilter = attackerScream
                            ? (System.Func<DirectorSound, bool>)(s => s.SpeakerActor == ev.Actor)
                            : s => s.SpeakerActor >= 0 && s.SpeakerActor != ev.Actor,
                        ForcedEnemyViewId = ev.EnemyViewId,
                        IgnoreEnemyVisibility = true
                    };
                }
                default:
                {
                    // Flee / approach / hide: a teammate's voice, aimed at that player.
                    var request = BuildLureRequest(ev, reason, lookUnder: false);
                    request.IgnoreEnemyVisibility = true;
                    return request;
                }
            }
        }

        private void DirectorSimulateMove(DirectorEventKind kind, PlayerAvatar local, int actor, string label)
        {
            var nearest = DirectorSnapshotEnemies().OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault();
            if (nearest == null) { DirectorFeed("sim", "no free enemy nearby", CTextDim); return; }
            var color = kind == DirectorEventKind.PlayerFlee ? MoveFleeColor : kind == DirectorEventKind.PlayerHide ? MoveHideColor : MoveApproachColor;
            AddHitLink(local.transform.position, nearest.Pos, label + $" ({nearest.Name})", color);
            DirectorQueue(kind, actor, local.transform.position, 0.5f, 1f, label + $" ({nearest.Name})", nearest.ViewId);
        }
    }
}
