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
            public RecordingContext Context; // null for custom audio and clips recorded before contexts existed
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
            public Func<DirectorSound, float> ContextWeight; // prefers clips whose recording context fits the event

            public static DirectorRequest Timer() => new DirectorRequest { Reason = "timer", ContextWeight = s => ContextWeightFor(DirectorEventKind.Timer, false, s) };
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

        private enum DirectorEventKind { Death, Hurt, Spotted, Break, HeadTalk, Pickup, SneakLure, LookUnderLure, VoiceLine, Special, TalkAnswer, EnemyFight, EnemyHitItem, Timer, PersonaFollowUp, PlayerFlee, PlayerApproach, PlayerHide, EnemySelfHurt, EnemyHurtByPlayer }

        // Events tied to a specific moment of an enemy's behaviour: no waiting for a better time.
        private static bool IsMomentEvent(DirectorEventKind kind) =>
            kind == DirectorEventKind.HeadTalk || kind == DirectorEventKind.SneakLure || kind == DirectorEventKind.LookUnderLure ||
            kind == DirectorEventKind.VoiceLine || kind == DirectorEventKind.Special || kind == DirectorEventKind.TalkAnswer ||
            kind == DirectorEventKind.EnemyFight || kind == DirectorEventKind.EnemyHitItem || kind == DirectorEventKind.PersonaFollowUp ||
            kind >= DirectorEventKind.PlayerFlee;

        // Generic view of any enemy's state machine: its own `currentState` enum, or Enemy.CurrentState (Headman).
        private sealed class DirectorStateProbe
        {
            public Component Owner;
            public System.Reflection.FieldInfo Field;
            public Enemy Enemy;
            public string LastState;
        }

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
            public bool VictimVoice;     // special: the nearest player's own voice instead of a teammate's; talk answer: echo
            public int PlayOnViewId = -1; // special: play from this object instead of the enemy (Bomb Thrower's thrown head)
            public int Depth;             // persona follow-up: how many lines this enemy already said in a row
            public string ExcludeGuid;    // persona follow-up: don't repeat the previous line
        }

        // Stage 4: one enemy's signature moment, recognised by its state machine (component type + state name).
        private sealed class DirectorSpecial
        {
            public string Label;      // {enemy} / {victim} are filled in
            public bool VictimVoice;  // true: the victim's own voice ("help!"); false: a teammate's voice luring them
            public float Chance;
            public float Cooldown;
        }

        private static readonly Dictionary<(string, string), DirectorSpecial> DirectorSpecials = new Dictionary<(string, string), DirectorSpecial>
        {
            [("EnemySlowMouth", "Attached")] = new DirectorSpecial { Label = "{enemy} latched onto {victim}'s face - they mumble", VictimVoice = true, Chance = 0.8f, Cooldown = 20f },
            [("EnemyHidden", "PlayerPickup")] = new DirectorSpecial { Label = "{enemy} carries {victim} away - they call for help", VictimVoice = true, Chance = 0.8f, Cooldown = 20f },
            [("EnemyHidden", "PlayerNotice")] = new DirectorSpecial { Label = "{enemy} whispers next to {victim}", Chance = 0.5f, Cooldown = 30f },
            [("EnemySnatcher", "SnatchPlayerToPoint")] = new DirectorSpecial { Label = "{enemy} drags {victim} off - they call for help", VictimVoice = true, Chance = 0.8f, Cooldown = 20f },
            [("EnemyHeartHugger", "LureStart")] = new DirectorSpecial { Label = "{enemy} lures {victim} in", Chance = 0.8f, Cooldown = 30f },
            [("EnemyBirthdayBoy", "CreepyStare")] = new DirectorSpecial { Label = "{enemy} stares at {victim} and calls them over", Chance = 0.6f, Cooldown = 30f },
            [("EnemyThinMan", "Notice")] = new DirectorSpecial { Label = "{enemy} appears behind {victim}", Chance = 0.5f, Cooldown = 30f },
            [("EnemyValuableThrower", "Throw")] = new DirectorSpecial { Label = "{enemy} throws loot at {victim} - \"catch!\"", Chance = 0.6f, Cooldown = 25f },
            [("EnemyTick", "Bite")] = new DirectorSpecial { Label = "{enemy} bites {victim}", VictimVoice = true, Chance = 0.5f, Cooldown = 30f },
            [("EnemyCeilingEye", "HasTarget")] = new DirectorSpecial { Label = "{enemy} locks onto {victim} from the ceiling", Chance = 0.5f, Cooldown = 30f },
        };

        // ─── State (host) ─────────────────────────────────────────────────────────
        private const int DirectorFeedMax = 40;
        private const int DirectorDecisionsMax = 12;
        private const float DirectorEventGap = 10f;        // min seconds between event playbacks
        private const float DirectorRecentWindow = 90f;    // a sound played within this window is penalized
        private const float DirectorWarmUpSeconds = 10f;   // a fresh clip isn't used before this
        private const float DirectorClipCooldown = 30f;    // the same clip never plays twice within this
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
        private float _directorNextStatePollAt;
        private readonly Dictionary<int, DirectorStateProbe> _directorStateProbes = new Dictionary<int, DirectorStateProbe>();
        private readonly Dictionary<int, float> _directorLureCooldown = new Dictionary<int, float>();
        private readonly Dictionary<int, DirectorCarriedHead> _directorCarriedHeads = new Dictionary<int, DirectorCarriedHead>();
        private readonly Dictionary<int, float> _directorVoiceLineCooldown = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _directorSpecialCooldown = new Dictionary<int, float>();
        private readonly Dictionary<int, string> _directorHeadStates = new Dictionary<int, string>();

        private static readonly DirectorSpecial BombHeadSpecial = new DirectorSpecial
        {
            Label = "{enemy}'s thrown head lands near {victim} and starts talking", Chance = 0.8f, Cooldown = 20f
        };

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
                var eye = vision != null ? vision.position : avatar.transform.position + Vector3.up * 1.5f;
                result.Add(new DirectorPlayer
                {
                    Player = owner,
                    Avatar = avatar,
                    Actor = owner.ActorNumber,
                    Name = GetPlayerDisplayName(owner),
                    Pos = avatar.transform.position,
                    Eye = eye,
                    Forward = GetLookDirection(avatar, eye),
                    Alive = !avatar.isDisabled && !avatar.deadSet,
                    HearsSelf = PlayerHearsSelf(owner)
                });
            }
            return result;
        }

        // The game only rotates VisionTransform for the local player; remote players' look comes from the
        // synced flashlight aim point (camera ray hit), with the synced body rotation as a fallback.
        private static Vector3 GetLookDirection(PlayerAvatar avatar, Vector3 eye)
        {
            if (avatar.isLocal)
            {
                var cam = Camera.main;
                if (cam != null) return cam.transform.forward;
            }
            else if (avatar.flashlightLightAim != null)
            {
                var toAim = avatar.flashlightLightAim.clientAimPoint - eye;
                if (avatar.flashlightLightAim.clientAimPoint != Vector3.zero && toAim.sqrMagnitude > 0.25f) return toAim.normalized;
            }
            if (!avatar.isLocal) return avatar.clientRotation * Vector3.forward;
            var vision = avatar.PlayerVisionTarget != null ? avatar.PlayerVisionTarget.VisionTransform : null;
            return vision != null ? vision.forward : avatar.transform.forward;
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
                    // Warm-up: a phrase never comes back right after it was said.
                    if (Time.time - voice.ReceivedAt < DirectorWarmUpSeconds) continue;
                    result.Add(new DirectorSound
                    {
                        Guid = kv.Key,
                        SpeakerActor = voice.SourceActor,
                        Label = !string.IsNullOrWhiteSpace(voice.SourceName) ? voice.SourceName : voice.SourcePlayerId,
                        Duration = voice.SampleRate > 0 ? voice.AudioData.Length / (float)(voice.SampleRate * 2) : 0f,
                        HaveActors = kv.Value,
                        Context = voice.Context
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
            if (_directorLastPlayed.TryGetValue(s.Guid, out var at) && Time.time - at < DirectorClipCooldown) { why?.Add("cooldown"); return 0f; }
            if (_directorLastPlayed.TryGetValue(s.Guid, out at) && Time.time - at < DirectorRecentWindow) { f *= 0.25f; why?.Add("recent"); }
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
                if (!speaker.Alive) f *= DeadSpeakerFactor(s.SpeakerActor, l.Actor, why);
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

            var situations = new Dictionary<int, EnemySituation>();
            foreach (var e in enemies)
            {
                if (!inRange.TryGetValue(e.ViewId, out var hearers)) continue;
                situations[e.ViewId] = new EnemySituation
                {
                    Hearers = hearers.Count,
                    Faced = hearers.Any(l => enemyVis.TryGetValue((l.Actor, e.ViewId), out var v) && v == 2),
                    Hunting = IsEnemyBusyWithPlayers(e.Go)
                };
            }

            var scored = new List<(DirectorSound s, DirectorEnemy e, float score)>();
            foreach (var s in sample)
            {
                var clip = ClipFactor(s, null) * (req.ContextWeight?.Invoke(s) ?? 1f);
                foreach (var e in enemies)
                {
                    if (!inRange.TryGetValue(e.ViewId, out var ls)) continue;
                    var sum = 0f;
                    foreach (var l in ls) sum += ListenerFactor(s, e, l, byActor, players, enemyVis, null);
                    if (req.Anchor.HasValue && Vector3.Distance(e.Pos, req.Anchor.Value) <= req.AnchorRadius) sum *= 1.5f;
                    sum *= SituationFactor(s.Context, situations[e.ViewId], null) * IdentityFactor(e.ViewId, s.SpeakerActor);
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
            if (chosen.s.Context != null)
            {
                decision.Details.Add($"context ×{req.ContextWeight?.Invoke(chosen.s) ?? 1f:F2}: {chosen.s.Context.Summary()}");
                var sitWhy = new List<string>();
                var sit = SituationFactor(chosen.s.Context, situations[chosen.e.ViewId], sitWhy);
                decision.Details.Add($"situation ×{sit:F2}" + (sitWhy.Count > 0 ? ": " + string.Join(", ", sitWhy) : ""));
            }
            var identity = IdentityFactor(chosen.e.ViewId, chosen.s.SpeakerActor);
            if (identity != 1f) decision.Details.Add(identity > 1f ? "keeps its voice ×1.4" : "different voice than before ×0.85");
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
            NoteDeathEvent(avatar);
            var self = DirectorOwner;
            if (self == null || !IsDirectorHost || !DirectorEnabled || avatar == null) return;
            if (Plugin.configEventDeath != null && !Plugin.configEventDeath.Value) return;
            var actor = avatar.photonView?.Owner?.ActorNumber ?? PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            self.RecordDeathWitnesses(avatar, actor);
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
            if (obj != null && valueLost >= 10f) NoteLocalEvent($"break:{valueLost:F0}", 4f);
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
            if (obj != null && player != null && player.isLocal && obj.isValuable)
            {
                var v = obj.GetComponent<ValuableObject>();
                NoteLocalEvent($"pickup:{(v != null ? v.dollarValueCurrent : 0f):F0}", 3f);
            }
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

        // ─── Stage 1: generic lures from enemy state machines ─────────────────────
        // "Sneak" = creeping up while nobody sees it (Mentalist, Reaper, Trudge, Headman…);
        // "LookUnder*" = hunting a player hiding/crawling under furniture (Robe, Reaper, Trudge, big Elsa, Headman…).
        private static bool IsSneakState(string state) => state == "Sneak";

        private static bool IsLookUnderState(string state) =>
            state != null && state.StartsWith("LookUnder") && !state.Contains("Attack") && !state.Contains("Stop");

        private DirectorStateProbe GetStateProbe(GameObject enemyGo)
        {
            var key = enemyGo.GetInstanceID();
            if (_directorStateProbes.TryGetValue(key, out var probe) && probe.Owner != null) return probe;

            var enemy = enemyGo.GetComponentInChildren<Enemy>(true);
            if (enemy == null) return null;

            probe = new DirectorStateProbe { Enemy = enemy };
            foreach (var c in enemy.GetComponents<MonoBehaviour>())
            {
                if (c == null || c is Enemy) continue;
                var f = c.GetType().GetField("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null && f.FieldType.IsEnum)
                {
                    probe.Owner = c;
                    probe.Field = f;
                    break;
                }
            }
            if (probe.Owner == null) probe.Owner = enemy; // Headman & co.: generic EnemyState

            _directorStateProbes[key] = probe;
            return probe;
        }

        private static string ReadState(DirectorStateProbe probe)
        {
            if (probe.Field != null) return probe.Field.GetValue(probe.Owner)?.ToString();
            return probe.Enemy != null ? probe.Enemy.CurrentState.ToString() : null;
        }

        private void DirectorPollEnemyStates()
        {
            if (Time.time < _directorNextStatePollAt) return;
            _directorNextStatePollAt = Time.time + 0.25f; // Sneak only lasts ~2 s

            var sneakOn = Plugin.configEventSneakLure == null || Plugin.configEventSneakLure.Value;
            var lookUnderOn = Plugin.configEventLookUnderLure == null || Plugin.configEventLookUnderLure.Value;
            var specialsOn = Plugin.configEventSpecials == null || Plugin.configEventSpecials.Value;

            foreach (var enemyGo in GetEnemiesList())
            {
                if (enemyGo == null) continue;
                var probe = GetStateProbe(enemyGo);
                if (probe == null) continue;

                var state = ReadState(probe);
                var previous = probe.LastState;
                probe.LastState = state;

                // Bomb Thrower's head has its own state machine and flies away from the body.
                if (probe.Owner is EnemyBombThrower thrower && thrower.head != null)
                {
                    var headKey = thrower.head.GetInstanceID();
                    var headState = thrower.head.currentState.ToString();
                    var hadHead = _directorHeadStates.TryGetValue(headKey, out var previousHead);
                    _directorHeadStates[headKey] = headState;
                    if (specialsOn && hadHead && headState != previousHead && headState == "Active")
                    {
                        var headView = thrower.head.GetComponent<PhotonView>();
                        if (headView != null)
                            DirectorTryQueueSpecial(BombHeadSpecial, enemyGo, "head Active", thrower.head.transform.position, playOnViewId: headView.ViewID);
                    }
                }

                if (state == previous || previous == null) continue;

                if (probe.Owner is EnemySpinny spinny && state == "RouletteEnd")
                {
                    if (specialsOn) DirectorTrySpinny(spinny, enemyGo);
                    continue;
                }

                if (DirectorSpecials.TryGetValue((probe.Owner.GetType().Name, state), out var special))
                {
                    if (specialsOn) DirectorTryQueueSpecial(special, enemyGo, state);
                    continue;
                }

                DirectorEventKind kind;
                if (sneakOn && IsSneakState(state)) kind = DirectorEventKind.SneakLure;
                else if (lookUnderOn && IsLookUnderState(state) && !IsLookUnderState(previous)) kind = DirectorEventKind.LookUnderLure;
                else continue;

                DirectorTryQueueLure(kind, enemyGo, state);
            }
        }

        private void DirectorTryQueueLure(DirectorEventKind kind, GameObject enemyGo, string state)
        {
            var viewId = GetEnemyNetViewId(enemyGo);
            if (viewId < 0) return;

            var name = NormalizeEnemyName(enemyGo.name);
            var tag = kind == DirectorEventKind.SneakLure ? "sneak" : "lookunder";
            if (_directorLureCooldown.TryGetValue(viewId, out var until) && until > Time.time)
            {
                DirectorNote(tag, $"{tag}:{viewId}", $"skipped (cooldown {until - Time.time:F0}s): {name} → {state}");
                return;
            }
            if (UnityEngine.Random.value > 0.7f)
            {
                DirectorNote(tag, $"{tag}:{viewId}", $"skipped (70% chance): {name} → {state}");
                return;
            }

            _directorLureCooldown[viewId] = Time.time + 30f;
            _directorSpotCooldown[viewId] = Time.time + 30f;
            // The lure replaces a pending "spotted" for the same enemy — one fake voice per approach.
            _directorPending.RemoveAll(p => p.Kind == DirectorEventKind.Spotted && p.EnemyViewId == viewId);

            var label = kind == DirectorEventKind.SneakLure
                ? $"{name} sneaks up unseen"
                : $"{name} looks under things for a hiding player";
            DirectorQueue(kind, -1, enemyGo.transform.position, 0.1f, 0.4f, label, viewId);
        }

        // Spinny's roulette: a bad spin (red / black) makes its victim cry out, a lucky one gets a teammate cheering.
        private void DirectorTrySpinny(EnemySpinny spinny, GameObject enemyGo)
        {
            var color = spinny.GetCurrentColorbyAngle(spinny.targetAngleDegrees);
            var bad = color == EnemySpinny.Colors.Red || color == EnemySpinny.Colors.Black;
            var special = new DirectorSpecial
            {
                Label = $"{{enemy}}'s roulette lands on {color.ToString().ToLowerInvariant()} for {{victim}}",
                VictimVoice = bad,
                Chance = 0.8f,
                Cooldown = 20f
            };
            DirectorTryQueueSpecial(special, enemyGo, $"RouletteEnd ({color})", victimAvatar: spinny.playerTarget);
        }

        private void DirectorTryQueueSpecial(DirectorSpecial special, GameObject enemyGo, string state,
            Vector3? at = null, PlayerAvatar victimAvatar = null, int playOnViewId = -1)
        {
            var viewId = GetEnemyNetViewId(enemyGo);
            if (viewId < 0) return;

            var name = NormalizeEnemyName(enemyGo.name);
            var pos = at ?? enemyGo.transform.position;
            var alive = DirectorSnapshotPlayers().Where(p => p.Alive).ToList();
            var victim = (victimAvatar != null ? alive.FirstOrDefault(p => p.Avatar == victimAvatar) : null)
                ?? alive.OrderBy(p => Vector3.Distance(p.Pos, pos)).FirstOrDefault();
            if (victim == null) return;
            if (_directorSpecialCooldown.TryGetValue(viewId, out var until) && until > Time.time)
            {
                DirectorNote("special", $"special:{viewId}", $"skipped (cooldown {until - Time.time:F0}s): {name} → {state}");
                return;
            }
            if (UnityEngine.Random.value > special.Chance)
            {
                DirectorNote("special", $"special:{viewId}", $"skipped ({special.Chance * 100f:F0}% chance): {name} → {state}");
                return;
            }

            _directorSpecialCooldown[viewId] = Time.time + special.Cooldown;
            _directorSpotCooldown[viewId] = Time.time + 30f;
            _directorPending.RemoveAll(p => p.Kind == DirectorEventKind.Spotted && p.EnemyViewId == viewId);

            var label = special.Label.Replace("{enemy}", name).Replace("{victim}", victim.Name);
            DirectorQueueSpecial(victim.Actor, pos, viewId, label, special.VictimVoice, playOnViewId);
        }

        private void DirectorQueueSpecial(int victimActor, Vector3 pos, int viewId, string label, bool victimVoice, int playOnViewId = -1)
        {
            DirectorQueue(DirectorEventKind.Special, victimActor, pos, 0.1f, 0.5f, label, viewId);
            var ev = _directorPending[_directorPending.Count - 1];
            ev.VictimVoice = victimVoice;
            ev.PlayOnViewId = playOnViewId;
        }

        // Hunter is blind and hunts by sound: a mimic voice close enough makes it go check (and maybe shoot) the spot.
        internal void DirectorAlertHunters(int[] viewIds)
        {
            if (!IsDirectorHost || !DirectorEnabled || viewIds == null || !SemiFunc.RunIsLevel()) return;
            if (Plugin.configEventHunterHears != null && !Plugin.configEventHunterHears.Value) return;

            var hunters = FindObjectsByType<EnemyHunter>(FindObjectsSortMode.None);
            if (hunters.Length == 0) return;

            foreach (var id in viewIds)
            {
                var go = GetEnemyGameObjectByViewId(id);
                if (go == null) continue;
                var anchor = GetEnemyAudioAnchor(go);
                var pos = anchor != null ? anchor.position : go.transform.position;
                foreach (var hunter in hunters)
                {
                    var e = hunter != null ? hunter.enemy : null;
                    if (e == null || !e.HasStateInvestigate || (e.EnemyParent != null && e.EnemyParent.gameObject == go)) continue;
                    var dist = Vector3.Distance(pos, e.transform.position);
                    if (dist / Mathf.Max(0.01f, e.StateInvestigate.rangeMultiplier) > 20f) continue;
                    e.StateInvestigate.Set(pos, false);
                    DirectorFeed("hunter", $"Hunter heard the voice from {NormalizeEnemyName(go.name)} ({dist:F0}m) and goes to check", CYellow);
                }
            }
        }

        // Lure voice: someone other than the listener(s) when possible (a teammate "calling" them).
        private DirectorRequest BuildLureRequest(DirectorPendingEvent ev, string reason, bool lookUnder)
        {
            var players = DirectorSnapshotPlayers();
            var nearby = players.Where(p => p.Alive && Vector3.Distance(p.Pos, ev.Position) <= DirectorEventRadius).ToList();
            var crawling = lookUnder
                ? new HashSet<int>(nearby.Where(p => p.Avatar != null && (p.Avatar.isCrawling || p.Avatar.isTumbling)).Select(p => p.Actor))
                : new HashSet<int>();
            var targets = crawling.Count > 0 ? crawling : new HashSet<int>(nearby.Select(p => p.Actor));
            var otherVoices = DirectorEligibleSounds().Any(s => s.SpeakerActor >= 0 && !targets.Contains(s.SpeakerActor));

            return new DirectorRequest
            {
                Reason = reason,
                ForcedEnemyViewId = ev.EnemyViewId,
                SoundFilter = s => s.SpeakerActor >= 0 && (!otherVoices || !targets.Contains(s.SpeakerActor)),
                ListenerFilter = crawling.Count > 0 ? (Func<DirectorPlayer, bool>)(l => crawling.Contains(l.Actor)) : null
            };
        }

        // ─── Stage 3: enemies' own voice lines ────────────────────────────────────
        // Talkative enemies (Oogly, Birthday Boy, Tricycle, Bang) say a teammate's words instead of their own line;
        // Duck and Bomb Thrower lines are baked into animations, so the stolen voice follows right after them.
        // Called on the host just before the line's RPC goes out (or runs locally in singleplayer).
        // Returns true when the native line must not play: a mimic voice took its place, or one is already talking.
        internal static bool DirectorOnEnemyVoiceLine(Component controller, bool replace)
        {
            var self = DirectorOwner;
            if (self == null || controller == null || !IsDirectorHost || !SemiFunc.RunIsLevel()) return false;

            var enemy = controller.GetComponent<Enemy>() ?? controller.GetComponentInParent<Enemy>();
            if (enemy == null) return false;
            if (replace && MimicMouths.IsSpeaking(enemy)) return true; // don't talk over the stolen voice

            if (!DirectorEnabled || (Plugin.configEventVoiceLines != null && !Plugin.configEventVoiceLines.Value)) return false;

            var enemyGo = enemy.EnemyParent != null ? enemy.EnemyParent.gameObject : enemy.gameObject;
            var viewId = GetEnemyNetViewId(enemyGo);
            if (viewId < 0) return false;

            var name = NormalizeEnemyName(enemyGo.name);
            if (self._directorVoiceLineCooldown.TryGetValue(viewId, out var until) && until > Time.time)
            {
                self.DirectorNote("voiceline", $"voiceline:{viewId}", $"skipped (cooldown {until - Time.time:F0}s): {name} line");
                return false;
            }
            if (UnityEngine.Random.value > 0.5f)
            {
                self.DirectorNote("voiceline", $"voiceline:{viewId}", $"skipped (50% chance): {name} line");
                return false;
            }
            self._directorVoiceLineCooldown[viewId] = Time.time + 25f;

            if (!replace)
            {
                self.DirectorQueue(DirectorEventKind.VoiceLine, -1, enemyGo.transform.position, 1.2f, 2.2f, $"{name} answers its own line", viewId);
                return false;
            }

            // Replacing must be decided now: the line either goes out or it doesn't.
            var label = $"{name} speaks a stolen line";
            self.DirectorFeed("voiceline", label, CYellow);
            var ev = new DirectorPendingEvent { Kind = DirectorEventKind.VoiceLine, Position = enemyGo.transform.position, EnemyViewId = viewId, Label = label };
            var request = self.BuildEventRequest(ev);
            request.ContextWeight = s => ContextWeightFor(DirectorEventKind.VoiceLine, false, s);
            request.Radius = Mathf.Max(DirectorEventRadius, (float)(Plugin.configPlaybackNearRadius?.Value ?? 15));
            var pick = self.DirectorPick(request);
            if (pick == null) return false;

            self.SendDirectorPlayback(pick);
            self._directorLastEventPlaybackAt = Time.time;
            return true;
        }

        // ─── Events: resolution ───────────────────────────────────────────────────
        private void DirectorUpdate()
        {
            DirectorPollTalk();

            if (!IsDirectorHost || !SemiFunc.RunIsLevel())
            {
                _directorPending.Clear();
                _directorLastHealth.Clear();
                _directorStateProbes.Clear();
                DirectorResetPersona();
                return;
            }

            if (!DirectorEnabled) return;

            DirectorPollHealth();
            DirectorPollHeadGrabbers();
            DirectorPollEnemyStates();
            DirectorPollMobDialog();
            DirectorPollPlayerMoves();

            for (var i = _directorPending.Count - 1; i >= 0; i--)
            {
                var ev = _directorPending[i];
                if (Time.time < ev.FireAt) continue;

                // The talking head has its own pacing; other events keep a global gap.
                if (!IsMomentEvent(ev.Kind) && Time.time - _directorLastEventPlaybackAt < DirectorEventGap)
                {
                    if (ev.Retries++ < 2) { ev.FireAt = Time.time + 5f; continue; }
                    _directorPending.RemoveAt(i);
                    DirectorFeed(ev.Kind.ToString().ToLowerInvariant(), $"dropped (too soon after another event): {ev.Label}", CTextDim);
                    continue;
                }

                _directorPending.RemoveAt(i);
                var request = BuildEventRequest(ev);
                if (request.ContextWeight == null) request.ContextWeight = s => ContextWeightFor(ev.Kind, ev.VictimVoice, s);
                if (request.Radius <= 0f)
                    request.Radius = Mathf.Max(DirectorEventRadius, (float)(Plugin.configPlaybackNearRadius?.Value ?? 15));
                var pick = DirectorPick(request);
                if (pick == null)
                {
                    // Nobody close enough right now — give enemies/players a few seconds to come together.
                    if (_directorLastPickWantsEnemy && !IsMomentEvent(ev.Kind) && ev.EnemyRetries < DirectorEventEnemyRetries)
                    {
                        ev.EnemyRetries++;
                        ev.FireAt = Time.time + 4f;
                        _directorPending.Add(ev);
                        DirectorNote(ev.Kind.ToString().ToLowerInvariant(), $"wait:{ev.GetHashCode()}",
                            $"waiting for an enemy nearby ({ev.EnemyRetries}/{DirectorEventEnemyRetries}): {ev.Label}");
                    }
                    continue;
                }

                if (ev.PlayOnViewId >= 0) pick.EnemyViewIds = new[] { ev.PlayOnViewId };
                _directorPlaybackDepth = ev.Kind == DirectorEventKind.PersonaFollowUp ? ev.Depth : 0;
                SendDirectorPlayback(pick);
                _directorPlaybackDepth = 0;
                _directorLastEventPlaybackAt = Time.time;
                var minDelay = (float)(Plugin.configMinDelay?.Value ?? 10);
                _directorTimerSuppressedUntil = Time.time + Mathf.Max(5f, minDelay * 0.5f);
            }
        }

        // How well a clip's recording context suits an event. 1 = neutral (and for clips without context);
        // clamped so a bad fit is still usable when it's the only clip.
        private static float ContextWeightFor(DirectorEventKind kind, bool victimVoice, DirectorSound s)
        {
            var c = s.Context;
            if (c == null) return 1f;
            var w = 1f;
            switch (kind)
            {
                case DirectorEventKind.Death:
                case DirectorEventKind.Hurt:
                case DirectorEventKind.EnemyFight:
                case DirectorEventKind.HeadTalk:
                case DirectorEventKind.Special when victimVoice:
                    // Distress: screams and phrases said while hurt or in danger.
                    if (c.Has("scream")) w *= 2.2f;
                    else if (c.Has("loud")) w *= 1.6f;
                    else if (c.Has("whisper")) w *= 0.5f;
                    if (c.Has("hurt") || c.Has("lowHp")) w *= 1.5f;
                    if (c.Has("danger")) w *= 1.3f;
                    if (c.Has("sawDeath")) w *= 1.4f;
                    else if (c.Has("sawHurt") || c.Has("spotted")) w *= 1.2f;
                    if (c.Has("calm")) w *= 0.7f;
                    break;
                case DirectorEventKind.TalkAnswer:
                    // Answers: real replies and things players said to enemies.
                    if (c.Has("reply")) w *= 1.7f;
                    if (c.Has("toEnemy")) w *= 1.5f;
                    if (c.Has("firstSight")) w *= 1.3f;
                    if (c.Has("scream")) w *= 0.8f;
                    break;
                case DirectorEventKind.EnemyHitItem:
                    if (c.Has("holding") || c.Has("pickup") || c.Has("break")) w *= 1.6f;
                    if (c.Has("loud") || c.Has("scream")) w *= 1.3f;
                    break;
                case DirectorEventKind.PlayerFlee:
                    // "Wait! Where are you going?" — called out to someone.
                    if (c.Has("toPlayer")) w *= 1.5f;
                    if (c.Has("loud")) w *= 1.4f;
                    if (c.Has("greeting")) w *= 1.2f;
                    if (c.Has("whisper")) w *= 0.5f;
                    break;
                case DirectorEventKind.PlayerHide:
                    // "It's safe, come out" — whispered.
                    if (c.Has("whisper")) w *= 2f;
                    if (c.Has("calm")) w *= 1.3f;
                    if (c.Has("toPlayer")) w *= 1.3f;
                    if (c.Has("loud") || c.Has("scream")) w *= 0.4f;
                    break;
                case DirectorEventKind.PlayerApproach:
                    if (c.Has("greeting")) w *= 1.7f;
                    if (c.Has("toPlayer") || c.Has("calm")) w *= 1.3f;
                    if (c.Has("scream")) w *= 0.5f;
                    break;
                case DirectorEventKind.EnemySelfHurt:
                case DirectorEventKind.EnemyHurtByPlayer:
                    // Pain: screams and phrases said while hurt; a teammate's "stop, it's me" when it's said to someone.
                    if (c.Has("scream")) w *= 2f;
                    else if (c.Has("loud")) w *= 1.5f;
                    if (c.Has("hurt")) w *= 1.5f;
                    if (kind == DirectorEventKind.EnemyHurtByPlayer && c.Has("toPlayer")) w *= 1.4f;
                    if (c.Has("whisper")) w *= 0.5f;
                    break;
                case DirectorEventKind.Timer:
                    if (c.Has("scream")) w *= 0.7f;
                    if (c.Has("calm")) w *= 1.2f;
                    break;
                default:
                    // Lures: a teammate calling someone — phrases addressed to a player, calm, not screams.
                    if (c.Has("toPlayer")) w *= 1.6f;
                    if (c.Has("greeting")) w *= 1.5f; // "hey, is that you?" — said on meeting a teammate
                    if (c.Has("calm")) w *= 1.3f;
                    if (c.Has("scream")) w *= 0.5f;
                    if ((kind == DirectorEventKind.SneakLure || kind == DirectorEventKind.LookUnderLure) && c.Has("whisper")) w *= 1.4f;
                    break;
            }
            return Mathf.Clamp(w, 0.3f, 3f);
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
                case DirectorEventKind.SneakLure:
                    return BuildLureRequest(ev, reason, lookUnder: false);
                case DirectorEventKind.LookUnderLure:
                    return BuildLureRequest(ev, reason, lookUnder: true);
                case DirectorEventKind.VoiceLine:
                    return BuildLureRequest(ev, reason, lookUnder: false);
                case DirectorEventKind.PlayerFlee:
                case DirectorEventKind.PlayerApproach:
                case DirectorEventKind.PlayerHide:
                case DirectorEventKind.EnemySelfHurt:
                case DirectorEventKind.EnemyHurtByPlayer:
                    return BuildMoveRequest(ev, reason);
                case DirectorEventKind.PersonaFollowUp:
                    // Same enemy, same player's voice, another phrase.
                    return new DirectorRequest
                    {
                        Reason = reason,
                        SoundFilter = s => s.SpeakerActor == ev.Actor && s.Guid != ev.ExcludeGuid,
                        ForcedEnemyViewId = ev.EnemyViewId,
                        IgnoreEnemyVisibility = true
                    };
                case DirectorEventKind.TalkAnswer:
                case DirectorEventKind.EnemyFight:
                case DirectorEventKind.EnemyHitItem:
                    return BuildInteractionRequest(ev, reason);
                case DirectorEventKind.Special:
                {
                    if (!ev.VictimVoice) return BuildLureRequest(ev, reason, lookUnder: false);
                    // The victim's own voice comes out of the enemy holding them; teammates are the audience.
                    var othersNear = DirectorSnapshotPlayers().Any(p => p.Alive && p.Actor != ev.Actor);
                    return new DirectorRequest
                    {
                        Reason = reason,
                        SoundFilter = s => s.SpeakerActor == ev.Actor,
                        ListenerFilter = l => !othersNear || l.Actor != ev.Actor,
                        ForcedEnemyViewId = ev.EnemyViewId,
                        IgnoreEnemyVisibility = true
                    };
                }
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

            if (kind == DirectorEventKind.SneakLure || kind == DirectorEventKind.LookUnderLure || kind == DirectorEventKind.VoiceLine)
            {
                var nearestEnemy = DirectorSnapshotEnemies().OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault();
                if (nearestEnemy == null) { DirectorFeed("sim", "no free enemy to lure you", CTextDim); return; }
                DirectorQueue(kind, -1, nearestEnemy.Pos, 0.5f, 1f, label + $" by {nearestEnemy.Name}", nearestEnemy.ViewId);
                return;
            }

            if (kind >= DirectorEventKind.PlayerFlee)
            {
                DirectorSimulateMove(kind, local, actor, label);
                return;
            }

            if (kind == DirectorEventKind.TalkAnswer || kind == DirectorEventKind.EnemyFight || kind == DirectorEventKind.EnemyHitItem)
            {
                DirectorSimulateInteraction(kind, local, actor, label);
                return;
            }

            if (kind == DirectorEventKind.Special)
            {
                // Victim voice from the nearest enemy, as if it had grabbed you.
                var grabber = DirectorSnapshotEnemies().OrderBy(e => Vector3.Distance(e.Pos, local.transform.position)).FirstOrDefault();
                if (grabber == null) { DirectorFeed("sim", "no free enemy to grab you", CTextDim); return; }
                DirectorQueueSpecial(actor, local.transform.position, grabber.ViewId, label + $" grabbed by {grabber.Name}", victimVoice: true);
                return;
            }

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
