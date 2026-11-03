using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace TFS_Mimics
{
    // Host-side "director": decides which voice, from which enemy, for which listeners —
    // both on the regular timer and in reaction to game events (death, hurt, spotted, valuable broken).
    public partial class TFS_Mimics
    {
        // ─── Types ────────────────────────────────────────────────────────────────
        private sealed class DirectorPlayer
        {
            public Player Player;
            public PlayerAvatar Avatar;
            public int Actor;
            public string Name;
            public Vector3 Pos;
            public Vector3 Eye;
            public Vector3 Forward;
            public bool Alive;
            public bool HearsSelf;
        }

        private sealed class DirectorEnemy
        {
            public GameObject Go;
            public int ViewId;
            public string Name;
            public Vector3 Pos;
        }

        private sealed class DirectorSound
        {
            public string Guid;
            public int SpeakerActor;   // -1 = custom audio (no speaker)
            public string Label;
            public float Duration;
            public HashSet<int> HaveActors;  // who has this clip (late joiners only get new ones)
        }

        private sealed class DirectorPickResult
        {
            public string SoundGuid;
            public int[] EnemyViewIds;
            public int ListenerCount;
        }

        private sealed class DirectorRequest
        {
            public string Reason;
            public Func<DirectorSound, bool> SoundFilter;
            public Func<DirectorPlayer, bool> ListenerFilter;
            public int ForcedEnemyViewId = -1;
            public Vector3? Anchor;          // enemies near this point get a bonus
            public float AnchorRadius = 20f;
            public bool IgnoreEnemyVisibility; // talking head: being seen is the point
            public int SpectatorActor = -1;    // dead player who still counts as a listener (spectating the head)
            public float Radius = -1f;         // listener radius override (events reach a bit further than the timer)

            public static DirectorRequest Timer() => new DirectorRequest { Reason = "timer" };
        }

        private sealed class DirectorDecision
        {
            public float Time;
            public string Reason;
            public string Sound;
            public string Enemies;
            public int Listeners;
            public float Score;
            public readonly List<string> Details = new List<string>();
        }

        private sealed class DirectorFeedEntry
        {
            public float Time;
            public string Kind;
            public string Text;
            public Color Color;
        }

        private enum DirectorEventKind { Death, Hurt, Spotted, Break, HeadTalk, Pickup }

        // Head Grabber carrying a dead player's head (tracked by grabber instance id)
        private sealed class DirectorCarriedHead
        {
            public int OwnerActor;
            public string OwnerName;
            public float NextTalkAt;
            public float NextShoutAt;
        }

        private sealed class DirectorPendingEvent
        {
            public DirectorEventKind Kind;
            public float FireAt;
            public int Actor;            // dead / hurt / spotted player, or valuable holder
            public Vector3 Position;
            public int EnemyViewId = -1; // spotted: the enemy that saw the player
            public string Label;
            public int Retries;
            public int EnemyRetries;
        }

        // ─── State (host) ─────────────────────────────────────────────────────────
        private const int DirectorFeedMax = 40;
        private const int DirectorDecisionsMax = 12;
        private const float DirectorEventGap = 10f;        // min seconds between event playbacks
        private const float DirectorRecentWindow = 90f;    // a sound played within this window is penalized
        private static readonly Dictionary<string, float> _directorLastPlayed = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> _directorPlayCount = new Dictionary<string, int>();
        private readonly List<DirectorFeedEntry> _directorFeed = new List<DirectorFeedEntry>();
        private readonly List<DirectorDecision> _directorDecisions = new List<DirectorDecision>();
        private readonly List<DirectorPendingEvent> _directorPending = new List<DirectorPendingEvent>();
        private readonly Dictionary<int, int> _directorLastHealth = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _directorHurtCooldown = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _directorSpotCooldown = new Dictionary<int, float>();
        private float _directorBreakCooldownUntil;
        private float _directorPickupCooldownUntil;
        private readonly Dictionary<string, float> _directorNoteThrottle = new Dictionary<string, float>();
        private float _directorLastEventPlaybackAt = -999f;
        private float _directorTimerSuppressedUntil;
        private float _directorNextHealthPollAt;
        private float _directorNextHeadPollAt;
        private readonly Dictionary<int, DirectorCarriedHead> _directorCarriedHeads = new Dictionary<int, DirectorCarriedHead>();

        private static bool IsDirectorHost => SemiFunc.IsMasterClientOrSingleplayer();
        private static bool DirectorEnabled => Plugin.configSmartDirector == null || Plugin.configSmartDirector.Value;

        // The local (IsMine) component owns the director; event patches route through it.
        private static TFS_Mimics DirectorOwner => patches.VoiceDataBus.LocalMimics;

        // ─── Feed / bookkeeping ───────────────────────────────────────────────────
        private void DirectorFeed(string kind, string text, Color color)
        {
            _directorFeed.Add(new DirectorFeedEntry { Time = Time.time, Kind = kind, Text = text, Color = color });
            if (_directorFeed.Count > DirectorFeedMax) _directorFeed.RemoveAt(0);
            DLog($"Director[{kind}]: {text}");
        }

        // A detection that was intentionally not queued (cooldown, threshold, chance) — shown dimmed so the
        // Director tab proves the hook fired. Throttled per key so per-frame hooks (vision) don't flood it.
        private void DirectorNote(string kind, string key, string text)
        {
            if (_directorNoteThrottle.TryGetValue(key, out var until) && until > Time.time) return;
            _directorNoteThrottle[key] = Time.time + 5f;
            DLog($"Director[{kind}]: {text}");
            _directorFeed.Add(new DirectorFeedEntry { Time = Time.time, Kind = kind, Text = text, Color = CTextDim });
            if (_directorFeed.Count > DirectorFeedMax) _directorFeed.RemoveAt(0);
        }

        private static void DirectorNotePlayed(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            _directorLastPlayed[guid] = Time.time;
            _directorPlayCount[guid] = _directorPlayCount.TryGetValue(guid, out var c) ? c + 1 : 1;
        }

        // ─── Snapshot ─────────────────────────────────────────────────────────────
        private List<DirectorPlayer> DirectorSnapshotPlayers()
        {
            var result = new List<DirectorPlayer>();
            foreach (var avatar in FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None))
            {
                if (avatar == null) continue;
                var owner = avatar.photonView != null ? avatar.photonView.Owner : null;
                if (owner == null && avatar.isLocal) owner = PhotonNetwork.LocalPlayer;
                if (owner == null) continue;

                var vision = avatar.PlayerVisionTarget != null ? avatar.PlayerVisionTarget.VisionTransform : null;
                result.Add(new DirectorPlayer
                {
                    Player = owner,
                    Avatar = avatar,
                    Actor = owner.ActorNumber,
                    Name = GetPlayerDisplayName(owner),
                    Pos = avatar.transform.position,
                    Eye = vision != null ? vision.position : avatar.transform.position + Vector3.up * 1.5f,
                    Forward = vision != null ? vision.forward : avatar.transform.forward,
                    Alive = !avatar.isDisabled && !avatar.deadSet,
                    HearsSelf = PlayerHearsSelf(owner)
                });
            }
            return result;
        }

        private List<DirectorEnemy> DirectorSnapshotEnemies()
        {
            var result = new List<DirectorEnemy>();
            foreach (var enemy in GetEnemiesList())
            {
                if (enemy == null) continue;
                var viewId = GetEnemyNetViewId(enemy);
                if (viewId < 0) continue;
                var key = GetPlaybackTargetKey(enemy, null);
                if (key != 0 && playbackBusyUntilByTargetKey.TryGetValue(key, out var busy) && busy > Time.time) continue;
                result.Add(new DirectorEnemy
                {
                    Go = enemy,
                    ViewId = viewId,
                    Name = NormalizeEnemyName(enemy.name),
                    Pos = GetEnemyDistancePosition(enemy, null)
                });
            }
            return result;
        }

        // Unlike the legacy "everyone must have it" rule, a sound is usable if anyone has it: listeners
        // without the clip just don't count (ListenerFactor). So a late joiner doesn't freeze the old pool.
        private List<DirectorSound> DirectorEligibleSounds()
        {
            var result = new List<DirectorSound>();
            List<string> stale = null;
            foreach (var kv in soundReadinessMap)
            {
                if (kv.Value.Count == 0) continue;

                var voice = FindCachedAudio(kv.Key);
                if (voice != null)
                {
                    result.Add(new DirectorSound
                    {
                        Guid = kv.Key,
                        SpeakerActor = voice.SourceActor,
                        Label = !string.IsNullOrWhiteSpace(voice.SourceName) ? voice.SourceName : voice.SourcePlayerId,
                        Duration = voice.SampleRate > 0 ? voice.AudioData.Length / (float)(voice.SampleRate * 2) : 0f,
                        HaveActors = kv.Value
                    });
                    continue;
                }

                var custom = _customAudioClips.FirstOrDefault(e => e?.SoundGuid == kv.Key);
                if (custom?.Clip != null)
                {
                    result.Add(new DirectorSound { Guid = kv.Key, SpeakerActor = -1, Label = custom.FileName, Duration = custom.Clip.length, HaveActors = kv.Value });
                    continue;
                }

                // A voice the host itself no longer holds can't be described or scored — drop it from the map.
                // (Custom sounds use "fs:"/"api:" ids and may legitimately be missing on the host.)
                if (!kv.Key.StartsWith("fs:") && !kv.Key.StartsWith("api:"))
                    (stale ??= new List<string>()).Add(kv.Key);
            }

            if (stale != null)
            {
                foreach (var guid in stale) soundReadinessMap.Remove(guid);
                DLog($"Director: pruned {stale.Count} readiness entries the host no longer holds");
            }
            return result;
        }

        // ─── Perception ───────────────────────────────────────────────────────────
        // 0 = occluded, 1 = clear line of sight but outside the view cone, 2 = in view
        private static int Visibility(DirectorPlayer viewer, Vector3 target)
        {
            var to = target - viewer.Eye;
            var dist = to.magnitude;
            if (dist < 0.01f) return 2;
            if (Physics.Raycast(viewer.Eye, to / dist, dist - 0.3f, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore))
                return 0;
            return Vector3.Dot(viewer.Forward, to / dist) > 0.5f ? 2 : 1; // ~60° half-angle
        }

        // Per-decision caches: the same listener/speaker pairs come up for every sound × enemy combination.
        private static readonly Dictionary<(int, int), bool> _speakerVisCache = new Dictionary<(int, int), bool>();
        private static readonly Dictionary<int, bool> _aloneCache = new Dictionary<int, bool>();

        private static bool SpeakerVisible(DirectorPlayer listener, DirectorPlayer speaker)
        {
            var key = (listener.Actor, speaker.Actor);
            if (!_speakerVisCache.TryGetValue(key, out var visible))
            {
                visible = Visibility(listener, speaker.Eye) == 2;
                _speakerVisCache[key] = visible;
            }
            return visible;
        }

        private static bool IsAlone(DirectorPlayer l, List<DirectorPlayer> players)
        {
            if (!_aloneCache.TryGetValue(l.Actor, out var alone))
            {
                alone = !players.Any(o => o != l && o.Alive && Vector3.Distance(o.Pos, l.Pos) < 10f);
                _aloneCache[l.Actor] = alone;
            }
            return alone;
        }

        // ─── Scoring ──────────────────────────────────────────────────────────────
        private static float ClipFactor(DirectorSound s, List<string> why)
        {
            var f = 1f;
            var count = _directorPlayCount.TryGetValue(s.Guid, out var c) ? c : 0;
            if (count > 0) { f *= 1f / (1f + 0.5f * count); why?.Add($"played×{count}"); }
            if (_directorLastPlayed.TryGetValue(s.Guid, out var at) && Time.time - at < DirectorRecentWindow) { f *= 0.25f; why?.Add("recent"); }
            if (s.Duration > 6f) { f *= 0.6f; why?.Add("long"); }
            return f;
        }

        private static float ListenerFactor(DirectorSound s, DirectorEnemy e, DirectorPlayer l,
            Dictionary<int, DirectorPlayer> byActor, List<DirectorPlayer> players,
            Dictionary<(int, int), int> enemyVis, List<string> why)
        {
            // Listener doesn't have this clip (joined later / dropped it): nothing would play for them.
            if (s.HaveActors != null && !s.HaveActors.Contains(l.Actor)) { why?.Add("doesn't have clip"); return 0f; }

            // Own voice to someone who opted out: they won't hear anything.
            if (s.SpeakerActor == l.Actor && !l.HearsSelf) return 0f;

            var f = 1f;
            var d = Vector3.Distance(l.Pos, e.Pos);
            if (d < 4f) { f *= 0.25f; why?.Add("too close"); }
            else if (d < 6f) f *= 0.7f;

            switch (enemyVis.TryGetValue((l.Actor, e.ViewId), out var vis) ? vis : 0)
            {
                case 2: f *= 0.35f; why?.Add("enemy in view"); break;
                case 1: f *= 1.1f; break;
                default: f *= 1.25f; why?.Add("hidden"); break;
            }

            if (s.SpeakerActor < 0)
            {
                f *= 0.9f;
            }
            else if (s.SpeakerActor == l.Actor)
            {
                f *= 0.6f; why?.Add("own voice");
            }
            else if (byActor.TryGetValue(s.SpeakerActor, out var speaker))
            {
                if (!speaker.Alive) { f *= 1.6f; why?.Add("dead speaker"); }
                else
                {
                    var sd = Vector3.Distance(l.Pos, speaker.Pos);
                    if (sd < 25f && SpeakerVisible(l, speaker)) { f *= 0.15f; why?.Add("speaker visible"); }
                    else if (sd < 8f) { f *= 0.5f; why?.Add("speaker near"); }
                    else if (sd > 20f) { f *= 1.3f; why?.Add("speaker far"); }
                }
            }
            else
            {
                f *= 1.2f; why?.Add("speaker left");
            }

            // Alone is scarier.
            if (IsAlone(l, players)) { f *= 1.25f; why?.Add("alone"); }
            return f;
        }

        // Set by DirectorPick when it failed only because no (free) enemy was close enough — worth retrying.
        private bool _directorLastPickWantsEnemy;
        private const int DirectorEventEnemyRetries = 4;
        private const float DirectorEventRadius = 20f; // enemy AudioSource maxDistance

        private DirectorPickResult DirectorPick(DirectorRequest req)
        {
            _directorLastPickWantsEnemy = false;
            _speakerVisCache.Clear();
            _aloneCache.Clear();
            var sounds = DirectorEligibleSounds();
            if (req.SoundFilter != null) sounds = sounds.Where(req.SoundFilter).ToList();
            if (sounds.Count == 0)
            {
                DirectorFeed(req.Reason, req.SoundFilter != null
                    ? "no matching voice (e.g. this event needs another player's voice, or theirs isn't recorded yet)"
                    : "no eligible sound (nothing recorded and shared with everyone yet)", CTextDim);
                return null;
            }

            var players = DirectorSnapshotPlayers();
            var byActor = players.ToDictionary(p => p.Actor);
            var listeners = players.Where(p => (p.Alive || p.Actor == req.SpectatorActor) && (req.ListenerFilter == null || req.ListenerFilter(p))).ToList();
            var enemies = DirectorSnapshotEnemies();
            if (req.ForcedEnemyViewId >= 0) enemies = enemies.Where(e => e.ViewId == req.ForcedEnemyViewId).ToList();
            if (listeners.Count == 0 || enemies.Count == 0)
            {
                _directorLastPickWantsEnemy = enemies.Count == 0;
                DirectorFeed(req.Reason, listeners.Count == 0 ? "no listeners" : "no free enemy", CTextDim);
                return null;
            }

            var radius = req.Radius > 0f ? req.Radius : (float)(Plugin.configPlaybackNearRadius?.Value ?? 15);
            var inRange = new Dictionary<int, List<DirectorPlayer>>();
            var enemyVis = new Dictionary<(int, int), int>();
            foreach (var e in enemies)
            {
                var list = listeners.Where(l => Vector3.Distance(l.Pos, e.Pos) <= radius).ToList();
                if (list.Count == 0) continue;
                inRange[e.ViewId] = list;
                foreach (var l in list) enemyVis[(l.Actor, e.ViewId)] = req.IgnoreEnemyVisibility ? 1 : Visibility(l, e.Pos + Vector3.up);
            }

            if (inRange.Count == 0)
            {
                _directorLastPickWantsEnemy = true;
                DirectorFeed(req.Reason, $"no enemy within {radius:F0}m of a listener", CTextDim);
                return null;
            }

            // Bound the search: preferred sounds all, otherwise a random sample.
            var sample = sounds.Count > 40 ? sounds.OrderBy(_ => UnityEngine.Random.value).Take(40).ToList() : sounds;

            var scored = new List<(DirectorSound s, DirectorEnemy e, float score)>();
            foreach (var s in sample)
            {
                var clip = ClipFactor(s, null);
                foreach (var e in enemies)
                {
                    if (!inRange.TryGetValue(e.ViewId, out var ls)) continue;
                    var sum = 0f;
                    foreach (var l in ls) sum += ListenerFactor(s, e, l, byActor, players, enemyVis, null);
                    if (req.Anchor.HasValue && Vector3.Distance(e.Pos, req.Anchor.Value) <= req.AnchorRadius) sum *= 1.5f;
                    if (sum > 0f) scored.Add((s, e, sum * clip));
                }
            }

            if (scored.Count == 0)
            {
                var muted = listeners.Where(l => !l.HearsSelf).Select(l => l.Actor).ToList();
                var onlyOwnMuted = muted.Count > 0 && sample.All(s => muted.Contains(s.SpeakerActor));
                DirectorFeed(req.Reason, onlyOwnMuted
                    ? "only voices of listeners who turned off Hear Yourself — nobody would hear them"
                    : "every option scored zero", CTextDim);
                return null;
            }

            // Weighted pick among the best few keeps it smart but not predictable.
            var top = scored.OrderByDescending(x => x.score).Take(5).ToList();
            var total = top.Sum(x => x.score * x.score);
            var roll = UnityEngine.Random.value * total;
            var chosen = top[0];
            foreach (var t in top)
            {
                roll -= t.score * t.score;
                if (roll <= 0f) { chosen = t; break; }
            }

            // Cover listeners the chosen enemy can't reach with extra enemies playing the same sound.
            var viewIds = new List<int> { chosen.e.ViewId };
            var covered = new HashSet<int>(inRange[chosen.e.ViewId].Select(l => l.Actor));
            foreach (var e in enemies.OrderBy(_ => UnityEngine.Random.value))
            {
                if (e.ViewId == chosen.e.ViewId || !inRange.TryGetValue(e.ViewId, out var ls)) continue;
                var gain = ls.Where(l => !covered.Contains(l.Actor))
                    .Sum(l => ListenerFactor(chosen.s, e, l, byActor, players, enemyVis, null));
                if (gain < 0.8f) continue;
                viewIds.Add(e.ViewId);
                foreach (var l in ls) covered.Add(l.Actor);
                if (viewIds.Count >= 3) break;
            }

            var decision = new DirectorDecision
            {
                Time = Time.time,
                Reason = req.Reason,
                Sound = chosen.s.SpeakerActor < 0 ? $"♫ {chosen.s.Label}" : chosen.s.Label,
                Enemies = string.Join(", ", viewIds.Select(id => enemies.First(x => x.ViewId == id).Name)),
                Listeners = covered.Count,
                Score = chosen.score
            };
            var clipWhy = new List<string>();
            ClipFactor(chosen.s, clipWhy);
            if (clipWhy.Count > 0) decision.Details.Add("clip: " + string.Join(", ", clipWhy));
            foreach (var l in inRange[chosen.e.ViewId])
            {
                var why = new List<string>();
                var f = ListenerFactor(chosen.s, chosen.e, l, byActor, players, enemyVis, why);
                decision.Details.Add($"{l.Name} ({Vector3.Distance(l.Pos, chosen.e.Pos):F0}m): ×{f:F2} {string.Join(", ", why)}");
            }
            foreach (var t in top.Where(t => t.s != chosen.s || t.e != chosen.e).Take(2))
                decision.Details.Add($"alt: {t.s.Label} @ {t.e.Name} = {t.score:F2}");

            _directorDecisions.Add(decision);
            if (_directorDecisions.Count > DirectorDecisionsMax) _directorDecisions.RemoveAt(0);
            DirectorFeed(req.Reason, $"{decision.Sound} → {decision.Enemies} ({decision.Listeners} listener{(decision.Listeners != 1 ? "s" : "")}, {decision.Score:F2})", CGreen);

            return new DirectorPickResult { SoundGuid = chosen.s.Guid, EnemyViewIds = viewIds.ToArray(), ListenerCount = covered.Count };
        }

        // ─── Events: intake ───────────────────────────────────────────────────────
        private void DirectorQueue(DirectorEventKind kind, int actor, Vector3 pos, float minDelay, float maxDelay, string label, int enemyViewId = -1)
        {
            _directorPending.Add(new DirectorPendingEvent
            {
                Kind = kind,
                Actor = actor,
                Position = pos,
                EnemyViewId = enemyViewId,
                Label = label,
                FireAt = Time.time + UnityEngine.Random.Range(minDelay, maxDelay)
            });
            DirectorFeed(kind.ToString().ToLowerInvariant(), label, CYellow);
        }

        internal static void DirectorOnPlayerDeath(PlayerAvatar avatar)
        {
            var self = DirectorOwner;
            if (self == null || !IsDirectorHost || !DirectorEnabled || avatar == null) return;
            if (Plugin.configEventDeath != null && !Plugin.configEventDeath.Value) return;
            var actor = avatar.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            self.DirectorQueue(DirectorEventKind.Death, actor, avatar.transform.position, 8f, 20f,
                $"{GetPlayerDisplayName(avatar.photonView?.Owner)} died");
        }

        internal static void DirectorOnEnemySpotted(Enemy enemy, PlayerAvatar player, bool culled)
        {
            var self = DirectorOwner;
            if (self == null || !IsDirectorHost || !DirectorEnabled || culled || enemy == null || player == null) return;
            if (Plugin.configEventSpotted != null && !Plugin.configEventSpotted.Value) return;
            if (!SemiFunc.RunIsLevel()) return;

            var enemyGo = enemy.EnemyParent != null ? enemy.EnemyParent.gameObject : enemy.gameObject;
            var viewId = GetEnemyNetViewId(enemyGo);
            if (viewId < 0) return;
            if (self._directorSpotCooldown.TryGetValue(viewId, out var until) && until > Time.time)
            {
                self.DirectorNote("spotted", $"spot:{viewId}", $"skipped (cooldown {until - Time.time:F0}s): {NormalizeEnemyName(enemyGo.name)} sees {GetPlayerDisplayName(player.photonView?.Owner)}");
                return;
            }
            self._directorSpotCooldown[viewId] = Time.time + 40f;

            var actor = player.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            self.DirectorQueue(DirectorEventKind.Spotted, actor, player.transform.position, 3f, 7f,
                $"{NormalizeEnemyName(enemyGo.name)} spotted {GetPlayerDisplayName(player.photonView?.Owner)}", viewId);
        }

        internal static void DirectorOnValuableBroken(PhysGrabObject obj, float valueLost, Vector3 point)
        {
            var self = DirectorOwner;
            if (self == null || !IsDirectorHost || !DirectorEnabled || obj == null) return;
            if (Plugin.configEventBreak != null && !Plugin.configEventBreak.Value) return;
            if (!SemiFunc.RunIsLevel()) return;
            if (valueLost < 25f)
            {
                self.DirectorNote("break", "break:small", $"skipped (under $25): lost ${valueLost:F0}");
                return;
            }
            if (Time.time < self._directorBreakCooldownUntil)
            {
                self.DirectorNote("break", "break:cd", $"skipped (cooldown {self._directorBreakCooldownUntil - Time.time:F0}s): lost ${valueLost:F0}");
                return;
            }

            var holder = obj.lastPlayerGrabbing;
            if (holder == null)
            {
                self.DirectorNote("break", "break:noholder", $"skipped (nobody held it): lost ${valueLost:F0}");
                return;
            }
            self._directorBreakCooldownUntil = Time.time + 15f;
            var actor = holder.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            self.DirectorQueue(DirectorEventKind.Break, actor, point, 2f, 5f,
                $"{GetPlayerDisplayName(holder.photonView?.Owner)} broke ${valueLost:F0} worth");
        }

        // Picking up a valuable is frequent, so it's gated by value, chance and a cooldown.
        internal static void DirectorOnValuableGrabbed(PhysGrabObject obj, PlayerAvatar player)
        {
            var self = DirectorOwner;
            if (self == null || !IsDirectorHost || !DirectorEnabled || obj == null || player == null) return;
            if (Plugin.configEventPickup != null && !Plugin.configEventPickup.Value) return;
            if (!SemiFunc.RunIsLevel() || !obj.isValuable) return;

            var valuable = obj.GetComponent<ValuableObject>();
            var value = valuable != null ? valuable.dollarValueCurrent : 0f;
            var who = GetPlayerDisplayName(player.photonView?.Owner);
            var key = $"pickup:{obj.GetInstanceID()}";

            if (value < 200f)
            {
                self.DirectorNote("pickup", key, $"skipped (under $200): {who} grabbed ${value:F0}");
                return;
            }
            if (Time.time < self._directorPickupCooldownUntil)
            {
                self.DirectorNote("pickup", key, $"skipped (cooldown {self._directorPickupCooldownUntil - Time.time:F0}s): {who} grabbed ${value:F0}");
                return;
            }
            if (UnityEngine.Random.value > 0.35f)
            {
                self.DirectorNote("pickup", key, $"skipped (35% chance): {who} grabbed ${value:F0}");
                return;
            }

            self._directorPickupCooldownUntil = Time.time + 40f;
            var actor = player.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            self.DirectorQueue(DirectorEventKind.Pickup, actor, obj.transform.position, 3f, 8f, $"{who} grabbed ${value:F0} worth");
        }

        // Hurt has no host-side hook (it runs on the victim's client), so poll the synced health instead.
        private void DirectorPollHealth()
        {
            if (Time.time < _directorNextHealthPollAt) return;
            _directorNextHealthPollAt = Time.time + 0.5f;

            var enabled = Plugin.configEventHurt == null || Plugin.configEventHurt.Value;
            foreach (var avatar in FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None))
            {
                if (avatar == null || avatar.playerHealth == null) continue;
                var actor = avatar.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
                var hp = avatar.playerHealth.health;
                var had = _directorLastHealth.TryGetValue(actor, out var prev);
                _directorLastHealth[actor] = hp;

                if (!enabled || !had || avatar.deadSet || hp <= 0 || prev - hp <= 0) continue;
                var who = GetPlayerDisplayName(avatar.photonView?.Owner);
                if (prev - hp < 5)
                {
                    DirectorNote("hurt", $"hurt:small:{actor}", $"skipped (under 5 HP): {who} -{prev - hp}");
                    continue;
                }
                if (_directorHurtCooldown.TryGetValue(actor, out var until) && until > Time.time)
                {
                    DirectorNote("hurt", $"hurt:cd:{actor}", $"skipped (cooldown {until - Time.time:F0}s): {who} -{prev - hp}");
                    continue;
                }
                _directorHurtCooldown[actor] = Time.time + 12f;
                DirectorQueue(DirectorEventKind.Hurt, actor, avatar.transform.position, 1.5f, 4f,
                    $"{GetPlayerDisplayName(avatar.photonView?.Owner)} took {prev - hp} damage");
            }
        }

        // Head Grabber: while it carries a dead player's head, the head "talks" with its owner's voice
        // (the owner spectates through that head and watches it lure their friends).
        private void DirectorPollHeadGrabbers()
        {
            if (Time.time < _directorNextHeadPollAt) return;
            _directorNextHeadPollAt = Time.time + 0.5f;

            var enabled = Plugin.configEventHeadTalk == null || Plugin.configEventHeadTalk.Value;
            var seen = new HashSet<int>();
            foreach (var grabber in FindObjectsByType<EnemyHeadGrabber>(FindObjectsSortMode.None))
            {
                if (grabber == null || !grabber.headTargetActive || grabber.headTarget == null || grabber.headTarget.playerAvatar == null) continue;

                var key = grabber.GetInstanceID();
                seen.Add(key);
                var owner = grabber.headTarget.playerAvatar;
                var ownerActor = owner.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

                if (!_directorCarriedHeads.TryGetValue(key, out var carried) || carried.OwnerActor != ownerActor)
                {
                    carried = new DirectorCarriedHead
                    {
                        OwnerActor = ownerActor,
                        OwnerName = GetPlayerDisplayName(owner.photonView?.Owner),
                        NextTalkAt = Time.time + UnityEngine.Random.Range(4f, 8f),
                        NextShoutAt = Time.time + 3f
                    };
                    _directorCarriedHeads[key] = carried;
                    DirectorFeed("head", $"Head Grabber picked up {carried.OwnerName}'s head", CYellow);
                }

                if (!enabled || _directorPending.Any(p => p.Kind == DirectorEventKind.HeadTalk && p.Actor == ownerActor)) continue;

                var enemyGo = grabber.enemy != null && grabber.enemy.EnemyParent != null ? grabber.enemy.EnemyParent.gameObject : grabber.gameObject;
                var viewId = GetEnemyNetViewId(enemyGo);
                if (viewId < 0) continue;

                // Attacking while carrying the head: the head screams right away.
                if (grabber.currentState == EnemyHeadGrabber.State.Attack && Time.time >= carried.NextShoutAt)
                {
                    carried.NextShoutAt = Time.time + 10f;
                    DirectorQueue(DirectorEventKind.HeadTalk, ownerActor, grabber.transform.position, 0f, 0.3f,
                        $"{carried.OwnerName}'s head screams during an attack", viewId);
                }
                else if (Time.time >= carried.NextTalkAt)
                {
                    carried.NextTalkAt = Time.time + UnityEngine.Random.Range(18f, 30f);
                    DirectorQueue(DirectorEventKind.HeadTalk, ownerActor, grabber.transform.position, 0f, 0.5f,
                        $"{carried.OwnerName}'s head calls out", viewId);
                }
            }

            foreach (var key in _directorCarriedHeads.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                DirectorFeed("head", $"Head Grabber dropped {_directorCarriedHeads[key].OwnerName}'s head", CTextDim);
                _directorCarriedHeads.Remove(key);
            }
        }

        // ─── Events: resolution ───────────────────────────────────────────────────
        private void DirectorUpdate()
        {
            if (!IsDirectorHost || !SemiFunc.RunIsLevel())
            {
                _directorPending.Clear();
                _directorLastHealth.Clear();
                return;
            }

            if (!DirectorEnabled) return;

            DirectorPollHealth();
            DirectorPollHeadGrabbers();

            for (var i = _directorPending.Count - 1; i >= 0; i--)
            {
                var ev = _directorPending[i];
                if (Time.time < ev.FireAt) continue;

                // The talking head has its own pacing; other events keep a global gap.
                if (ev.Kind != DirectorEventKind.HeadTalk && Time.time - _directorLastEventPlaybackAt < DirectorEventGap)
                {
                    if (ev.Retries++ < 2) { ev.FireAt = Time.time + 5f; continue; }
                    _directorPending.RemoveAt(i);
                    DirectorFeed(ev.Kind.ToString().ToLowerInvariant(), $"dropped (too soon after another event): {ev.Label}", CTextDim);
                    continue;
                }

                _directorPending.RemoveAt(i);
                var request = BuildEventRequest(ev);
                if (request.Radius <= 0f)
                    request.Radius = Mathf.Max(DirectorEventRadius, (float)(Plugin.configPlaybackNearRadius?.Value ?? 15));
                var pick = DirectorPick(request);
                if (pick == null)
                {
                    // Nobody close enough right now — give enemies/players a few seconds to come together.
                    if (_directorLastPickWantsEnemy && ev.Kind != DirectorEventKind.HeadTalk && ev.EnemyRetries < DirectorEventEnemyRetries)
                    {
                        ev.EnemyRetries++;
                        ev.FireAt = Time.time + 4f;
                        _directorPending.Add(ev);
                        DirectorNote(ev.Kind.ToString().ToLowerInvariant(), $"wait:{ev.GetHashCode()}",
                            $"waiting for an enemy nearby ({ev.EnemyRetries}/{DirectorEventEnemyRetries}): {ev.Label}");
                    }
                    continue;
                }

                SendDirectorPlayback(pick);
                _directorLastEventPlaybackAt = Time.time;
                var minDelay = (float)(Plugin.configMinDelay?.Value ?? 10);
                _directorTimerSuppressedUntil = Time.time + Mathf.Max(5f, minDelay * 0.5f);
            }
        }

        private DirectorRequest BuildEventRequest(DirectorPendingEvent ev)
        {
            var reason = $"{ev.Kind.ToString().ToLowerInvariant()}: {ev.Label}";
            switch (ev.Kind)
            {
                case DirectorEventKind.Death:
                    // The dead player "calls for help" from near the body.
                    return new DirectorRequest { Reason = reason, SoundFilter = s => s.SpeakerActor == ev.Actor, Anchor = ev.Position };
                case DirectorEventKind.Hurt:
                    // Teammates around hear the injured one; the victim already knows they got hit.
                    // With nobody else alive (solo, or last one standing) the victim is the audience.
                    var othersAlive = DirectorSnapshotPlayers().Any(p => p.Alive && p.Actor != ev.Actor);
                    return new DirectorRequest { Reason = reason, SoundFilter = s => s.SpeakerActor == ev.Actor, ListenerFilter = l => !othersAlive || l.Actor != ev.Actor, Anchor = ev.Position };
                case DirectorEventKind.Pickup:
                {
                    // A hidden enemy nearby "talks" to whoever grabbed the loot — another teammate's voice if there is one.
                    var otherVoices = DirectorEligibleSounds().Any(s => s.SpeakerActor >= 0 && s.SpeakerActor != ev.Actor);
                    return new DirectorRequest
                    {
                        Reason = reason,
                        SoundFilter = s => s.SpeakerActor >= 0 && (!otherVoices || s.SpeakerActor != ev.Actor),
                        Anchor = ev.Position,
                        AnchorRadius = 15f
                    };
                }
                case DirectorEventKind.HeadTalk:
                    // The carried head speaks with its owner's voice, from the grabber itself.
                    return new DirectorRequest { Reason = reason, SoundFilter = s => s.SpeakerActor == ev.Actor, ForcedEnemyViewId = ev.EnemyViewId, IgnoreEnemyVisibility = true, SpectatorActor = ev.Actor };
                case DirectorEventKind.Spotted:
                    // The enemy that saw the player lures them with someone else's voice.
                    return new DirectorRequest { Reason = reason, SoundFilter = s => s.SpeakerActor >= 0 && s.SpeakerActor != ev.Actor, ForcedEnemyViewId = ev.EnemyViewId };
                default:
                    return new DirectorRequest { Reason = reason, SoundFilter = s => s.SpeakerActor == ev.Actor, Anchor = ev.Position, AnchorRadius = 15f };
            }
        }

        // ─── Debug: simulations (Director tab) ────────────────────────────────────
        private void DirectorSimulate(DirectorEventKind kind)
        {
            var local = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).FirstOrDefault(a => a != null && a.isLocal);
            if (local == null) return;
            var actor = PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            var label = $"[sim] {kind.ToString().ToLowerInvariant()} ({GetPlayerDisplayName(PhotonNetwork.LocalPlayer)})";

            if (kind == DirectorEventKind.HeadTalk)
            {
                // Prefer a real Head Grabber; any free enemy stands in when none is around.
                var free = DirectorSnapshotEnemies();
                var grabberIds = new HashSet<int>(FindObjectsByType<EnemyHeadGrabber>(FindObjectsSortMode.None)
                    .Where(g => g != null && g.enemy != null && g.enemy.EnemyParent != null)
                    .Select(g => GetEnemyNetViewId(g.enemy.EnemyParent.gameObject)));
                var target = free.Where(e => grabberIds.Contains(e.ViewId)).OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault()
                    ?? free.OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault();
                if (target == null) { DirectorFeed("sim", "no free enemy to carry your head", CTextDim); return; }
                DirectorQueue(kind, actor, local.transform.position, 0.5f, 1f, label + $" from {target.Name}", target.ViewId);
                return;
            }

            if (kind == DirectorEventKind.Spotted)
            {
                var nearest = DirectorSnapshotEnemies().OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault();
                if (nearest == null) { DirectorFeed("sim", "no free enemy to spot you", CTextDim); return; }
                DirectorQueue(kind, actor, local.transform.position, 0.5f, 1f, label + $" by {nearest.Name}", nearest.ViewId);
                return;
            }

            DirectorQueue(kind, actor, local.transform.position, 0.5f, 1f, label);
        }
    }
}
