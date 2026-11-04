using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    /// <summary>
    /// Everything the mod knew when a phrase was spoken: loudness, the speaker's state, who they looked at,
    /// enemies and teammates around, recent events and whether it answered another player.
    /// Captured on the speaker's client, sent with the clip and saved next to it (MIMC3).
    /// Stored as plain key=value pairs so new keys never break old files or older mod versions.
    /// </summary>
    internal sealed class RecordingContext
    {
        internal const int FormatVersion = 1;

        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly HashSet<string> Tags = new HashSet<string>(StringComparer.Ordinal);

        public string Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

        public float GetFloat(string key, float fallback = 0f) =>
            Values.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

        public int GetInt(string key, int fallback = -1) =>
            Values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

        public bool Has(string tag) => Tags.Contains(tag);

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) Values.Remove(key);
            else Values[key] = value;
        }

        public void Set(string key, float value) => Values[key] = value.ToString("0.###", CultureInfo.InvariantCulture);
        public void Set(string key, int value) => Values[key] = value.ToString(CultureInfo.InvariantCulture);

        public string Serialize()
        {
            var sb = new StringBuilder();
            Values["tags"] = string.Join(",", Tags.OrderBy(t => t, StringComparer.Ordinal));
            foreach (var kv in Values)
                sb.Append(kv.Key).Append('=').Append(Escape(kv.Value)).Append('\n');
            return sb.ToString();
        }

        public static RecordingContext Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var ctx = new RecordingContext();
            foreach (var line in text.Split('\n'))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                ctx.Values[line.Substring(0, eq)] = Unescape(line.Substring(eq + 1));
            }
            var tags = ctx.Get("tags");
            if (!string.IsNullOrEmpty(tags))
                foreach (var t in tags.Split(',')) if (t.Length > 0) ctx.Tags.Add(t);
            return ctx;
        }

        /// <summary>Short one-line description for the Recordings tab and director decisions.</summary>
        public string Summary()
        {
            var parts = new List<string>();
            var loud = Get("loud");
            if (loud != null) parts.Add(loud);
            var look = Get("lookAt");
            if (look != null) parts.Add("→ " + look.Replace("enemy:", "").Replace("player:", ""));
            var reply = Get("replyToName");
            if (reply != null) parts.Add("↩ " + reply);
            foreach (var t in new[] { "hurt", "sawHurt", "danger", "spotted", "alone", "mimicHeard", "death", "sawDeath", "firstSight", "greeting", "dead", "holding" })
                if (Has(t)) parts.Add(t);
            var level = Get("level");
            if (level != null) parts.Add(level);
            return string.Join(" · ", parts);
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n");

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] == 'n' ? '\n' : s[i]);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }

    public partial class TFS_Mimics
    {
        // ─── Recording context capture (speaker's client) ─────────────────────────
        private RecordingContext _ctxCurrent;
        private float _ctxStartedAt;
        private readonly Dictionary<string, float> _ctxLookTime = new Dictionary<string, float>();
        private float _ctxMaxDangerEnemyDist = -1f;
        private float _ctxNextSampleAt;
        private float _ctxNextTickAt;

        // Short-lived event tags: each lives a few seconds; whatever is still alive when a phrase starts is what
        // the phrase most likely reacts to. Older ones stay in a 15 s history for the "recent" field.
        private readonly List<(float time, string text, float until)> _localEvents = new List<(float, string, float)>();

        // Sight memory for "first sight" style events.
        private readonly Dictionary<int, float> _ctxEnemySeenAt = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> _ctxEnemyDanger = new Dictionary<int, bool>();
        private readonly Dictionary<int, float> _ctxPlayerSeenAt = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _ctxOtherHealth = new Dictionary<int, int>();
        private float _ctxNextSightAt;

        // When each other player last stopped talking and where (to spot replies).
        private readonly Dictionary<int, bool> _otherTalking = new Dictionary<int, bool>();
        private readonly Dictionary<int, (float endedAt, Vector3 pos, string name)> _otherSpeechEnded = new Dictionary<int, (float, Vector3, string)>();
        private int _ctxLocalLastHealth = -1;
        private float _ctxLocalHurtAt = -999f;
        private int _ctxLocalLastDamage;

        private const float ContextRadius = 15f;
        private const float ReplyWindow = 3f;

        internal static void NoteLocalEvent(string text, float lifetime = 4f)
        {
            var self = patches.VoiceDataBus.LocalMimics;
            if (self == null || string.IsNullOrEmpty(text)) return;
            self._localEvents.Add((Time.time, text, Time.time + lifetime));
            if (self._localEvents.Count > 32) self._localEvents.RemoveAt(0);
        }

        // Can the local player see this point (players and the target itself don't block)?
        private static bool LocalCanSee(PlayerAvatar local, Vector3 point, float maxDistance, float maxAngle, Transform ignore = null)
        {
            var vision = local.PlayerVisionTarget != null ? local.PlayerVisionTarget.VisionTransform : null;
            var eye = vision != null ? vision.position : local.transform.position + Vector3.up * 1.5f;
            var to = point - eye;
            var dist = to.magnitude;
            if (dist > maxDistance) return false;
            if (dist < 0.1f) return true;
            var forward = GetLookDirection(local, eye);
            if (Vector3.Angle(forward, to) > maxAngle) return false;
            var count = Physics.RaycastNonAlloc(eye, to / dist, SightHits, dist, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var hit = SightHits[i].collider;
                if (hit == null || (ignore != null && hit.transform.IsChildOf(ignore))) continue;
                if (hit.GetComponentInParent<PlayerAvatar>() != null || hit.GetComponentInParent<EnemyParent>() != null) continue;
                return false;
            }
            return true;
        }

        // Witness / sight events, 4 Hz: someone getting hurt in view, an enemy turning on you,
        // first sight of an enemy, a teammate showing up again.
        private void DetectSightEvents(PlayerAvatar local)
        {
            if (Time.time < _ctxNextSightAt) return;
            _ctxNextSightAt = Time.time + 0.25f;

            foreach (var a in SemiFunc.PlayerGetList())
            {
                if (a == null || a.isLocal || a.photonView == null || a.photonView.Owner == null || a.playerHealth == null) continue;
                var actor = a.photonView.Owner.ActorNumber;
                var name = GetPlayerDisplayName(a.photonView.Owner);
                var head = a.PlayerVisionTarget != null && a.PlayerVisionTarget.VisionTransform != null
                    ? a.PlayerVisionTarget.VisionTransform.position : a.transform.position + Vector3.up * 1.5f;

                var hp = a.playerHealth.health;
                if (_ctxOtherHealth.TryGetValue(actor, out var prevHp) && hp < prevHp && !a.deadSet
                    && LocalCanSee(local, head, 15f, 70f, a.transform))
                    NoteLocalEvent($"sawHurt:{name}", 4f);
                _ctxOtherHealth[actor] = hp;

                if (a.deadSet || a.isDisabled) continue;
                if (!LocalCanSee(local, head, 12f, 60f, a.transform)) continue;
                if (!_ctxPlayerSeenAt.TryGetValue(actor, out var seenAt) || Time.time - seenAt > 30f)
                    NoteLocalEvent($"seePlayer:{name}", 3f);
                _ctxPlayerSeenAt[actor] = Time.time;
            }

            if (!SemiFunc.RunIsLevel()) return;
            foreach (var go in GetEnemiesList())
            {
                if (go == null) continue;
                var center = GetEnemyCenter(go);
                if (Vector3.Distance(center, local.transform.position) > 15f) continue;
                var id = go.GetInstanceID();
                var name = NormalizeEnemyName(go.name);

                var probe = GetStateProbe(go);
                var state = probe != null ? ReadState(probe) : null;
                var danger = state != null && IsDangerState(state);
                if (danger && (!_ctxEnemyDanger.TryGetValue(id, out var was) || !was))
                    NoteLocalEvent($"enemyNotice:{name}", 4f);
                _ctxEnemyDanger[id] = danger;

                if (!LocalCanSee(local, center, 10f, 60f, go.transform)) continue;
                if (!_ctxEnemySeenAt.TryGetValue(id, out var seen) || Time.time - seen > 30f)
                    NoteLocalEvent($"seeEnemy:{name}", 3f);
                _ctxEnemySeenAt[id] = Time.time;
            }
        }

        // A teammate died: seen it happen, or just heard about it.
        internal static void NoteDeathEvent(PlayerAvatar victim)
        {
            if (victim == null) return;
            var name = GetPlayerDisplayName(victim.photonView?.Owner);
            if (victim.isLocal) return;
            var local = GetLocalAvatar();
            var chest = victim.transform.position + Vector3.up;
            NoteLocalEvent(local != null && LocalCanSee(local, chest, 15f, 70f, victim.transform) ? $"sawDeath:{name}" : $"death:{name}", 6f);
        }

        // Called every frame from Update; does its work at 10 Hz.
        private void RecordingContextTick()
        {
            if (Time.time < _ctxNextTickAt) return;
            _ctxNextTickAt = Time.time + 0.1f;

            var local = GetLocalAvatar();
            if (local != null && local.playerHealth != null)
            {
                var hp = local.playerHealth.health;
                if (_ctxLocalLastHealth >= 0 && hp < _ctxLocalLastHealth)
                {
                    _ctxLocalHurtAt = Time.time;
                    _ctxLocalLastDamage = _ctxLocalLastHealth - hp;
                    NoteLocalEvent($"hurt:{_ctxLocalLastDamage}", 5f);
                }
                _ctxLocalLastHealth = hp;
            }

            foreach (var avatar in SemiFunc.PlayerGetList())
            {
                if (avatar == null || avatar.isLocal || avatar.voiceChat == null || avatar.photonView == null || avatar.photonView.Owner == null) continue;
                var actor = avatar.photonView.Owner.ActorNumber;
                var talking = avatar.voiceChat.isTalking;
                if (_otherTalking.TryGetValue(actor, out var was) && was && !talking)
                    _otherSpeechEnded[actor] = (Time.time, avatar.transform.position, GetPlayerDisplayName(avatar.photonView.Owner));
                _otherTalking[actor] = talking;
            }

            if (local != null) DetectSightEvents(local);
            while (_localEvents.Count > 0 && Time.time - _localEvents[0].time > 15f) _localEvents.RemoveAt(0);

            if (_ctxCurrent != null && capturingSpeech && Time.time >= _ctxNextSampleAt)
            {
                _ctxNextSampleAt = Time.time + 0.1f;
                SampleLookTarget();
            }
        }

        private static PlayerAvatar GetLocalAvatar()
        {
            foreach (var a in SemiFunc.PlayerGetList())
                if (a != null && a.isLocal) return a;
            return null;
        }

        // Speech start: snapshot of the world around the speaker.
        private void BeginRecordingContext()
        {
            try
            {
                var ctx = new RecordingContext();
                _ctxCurrent = ctx;
                _ctxStartedAt = Time.time;
                _ctxLookTime.Clear();
                _ctxNextSampleAt = 0f;

                ctx.Set("v", RecordingContext.FormatVersion);
                ctx.Set("utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

                var scene = SemiFunc.RunIsLevel() ? "level" : SemiFunc.RunIsShop() ? "shop" : SemiFunc.RunIsArena() ? "arena"
                    : SemiFunc.RunIsLobby() ? "lobby" : SemiFunc.RunIsLobbyMenu() ? "lobbymenu" : "other";
                ctx.Set("scene", scene);
                ctx.Tags.Add(scene);
                var level = RunManager.instance != null ? RunManager.instance.levelCurrent : null;
                if (level != null) ctx.Set("level", !string.IsNullOrWhiteSpace(level.NarrativeName) ? level.NarrativeName : level.name);
                ctx.Set("players", SemiFunc.PlayerGetList().Count);

                var local = GetLocalAvatar();
                if (local == null) return;

                ctx.Set("pos", $"{local.transform.position.x:F1},{local.transform.position.y:F1},{local.transform.position.z:F1}");
                if (local.playerHealth != null)
                {
                    ctx.Set("hp", local.playerHealth.health);
                    ctx.Set("hpMax", local.playerHealth.maxHealth);
                    if (local.playerHealth.health <= local.playerHealth.maxHealth * 0.3f) ctx.Tags.Add("lowHp");
                }
                var hurtAgo = Time.time - _ctxLocalHurtAt;
                if (hurtAgo < 10f)
                {
                    ctx.Set("hurtAgo", hurtAgo);
                    ctx.Set("hurtDamage", _ctxLocalLastDamage);
                    if (hurtAgo < 6f) ctx.Tags.Add("hurt");
                }
                if (local.deadSet || local.isDisabled) ctx.Tags.Add("dead");

                var pose = local.isTumbling ? "tumble" : local.isCrawling ? "crawl" : local.isSliding ? "slide"
                    : local.isCrouching ? "crouch" : local.isSprinting ? "sprint" : local.isMoving ? "walk" : "still";
                ctx.Set("pose", pose);

                if (local.RoomVolumeCheck != null && local.RoomVolumeCheck.CurrentRooms != null && local.RoomVolumeCheck.CurrentRooms.Count > 0)
                {
                    var room = local.RoomVolumeCheck.CurrentRooms[0];
                    if (room != null)
                    {
                        ctx.Set("room", room.name.Replace("(Clone)", "").Trim());
                        if (room.Truck) ctx.Tags.Add("truck");
                        if (room.Extraction) ctx.Tags.Add("extraction");
                    }
                }

                var held = local.physGrabber != null ? local.physGrabber.grabbedPhysGrabObject : null;
                if (held != null)
                {
                    ctx.Set("holding", held.name.Replace("(Clone)", "").Trim());
                    var valuable = held.GetComponent<ValuableObject>();
                    if (valuable != null) ctx.Set("holdingValue", valuable.dollarValueCurrent);
                    ctx.Tags.Add("holding");
                }

                // Teammates around
                var mates = new List<string>();
                foreach (var a in SemiFunc.PlayerGetList())
                {
                    if (a == null || a.isLocal || a.deadSet || a.photonView == null) continue;
                    if (Vector3.Distance(a.transform.position, local.transform.position) <= 12f)
                        mates.Add(GetPlayerDisplayName(a.photonView.Owner));
                }
                ctx.Set("mates", string.Join(",", mates));
                ctx.Set("matesCount", mates.Count);
                ctx.Tags.Add(mates.Count == 0 ? "alone" : mates.Count >= 2 ? "group" : "pair");

                // Enemies around, with their state
                var enemies = new List<(string name, float dist, string state)>();
                var danger = false;
                if (SemiFunc.RunIsLevel())
                {
                    foreach (var go in GetEnemiesList())
                    {
                        if (go == null) continue;
                        var dist = Vector3.Distance(GetEnemyCenter(go), local.transform.position);
                        if (dist > ContextRadius) continue;
                        var probe = GetStateProbe(go);
                        var state = probe != null ? ReadState(probe) : null;
                        enemies.Add((NormalizeEnemyName(go.name), dist, state));
                        if (state != null && IsDangerState(state)) danger = true;
                    }
                }
                enemies.Sort((a, b) => a.dist.CompareTo(b.dist));
                ctx.Set("enemies", string.Join(";", enemies.Take(5).Select(e => $"{e.name}:{e.dist:F0}:{e.state}")));
                if (enemies.Count > 0)
                {
                    ctx.Set("nearestEnemy", enemies[0].name);
                    ctx.Set("nearestEnemyDist", enemies[0].dist);
                    ctx.Tags.Add("enemyNear");
                }
                if (danger || (enemies.Count > 0 && enemies[0].dist < 5f)) ctx.Tags.Add("danger");

                // Live event tags = what the phrase reacts to; "recent" keeps the last 15 s for reference.
                var live = _localEvents.Where(e => e.until >= Time.time).ToList();
                if (live.Count > 0) ctx.Set("events", string.Join(",", live.Select(e => $"{e.text}@{Time.time - e.time:F1}s")));
                var recent = _localEvents.Where(e => e.until < Time.time).Select(e => $"{e.text}@{Time.time - e.time:F0}s").ToList();
                if (recent.Count > 0) ctx.Set("recent", string.Join(",", recent));
                foreach (var e in live)
                {
                    var kind = e.text.Split(':')[0];
                    switch (kind)
                    {
                        case "death": ctx.Tags.Add("death"); break;
                        case "sawDeath": ctx.Tags.Add("death"); ctx.Tags.Add("sawDeath"); break;
                        case "sawHurt": ctx.Tags.Add("sawHurt"); break;
                        case "mimic": ctx.Tags.Add("mimicHeard"); break;
                        case "break": ctx.Tags.Add("break"); break;
                        case "pickup": ctx.Tags.Add("pickup"); break;
                        case "enemyNotice": ctx.Tags.Add("spotted"); ctx.Tags.Add("danger"); break;
                        case "seeEnemy": ctx.Tags.Add("firstSight"); break;
                        case "seePlayer": ctx.Tags.Add("greeting"); break;
                    }
                }

                SampleLookTarget();
            }
            catch (Exception ex)
            {
                DLog($"BeginRecordingContext failed: {ex.Message}");
            }
        }

        private static bool IsDangerState(string state) =>
            state.Contains("Attack") || state.Contains("Chase") || state.Contains("Notice") || state.Contains("Aggro") ||
            state.Contains("Grab") || state.Contains("Lure") || state.Contains("Hunt") || state.Contains("Shoot");

        // Who the speaker looks at while talking: an enemy (same aim test as "talk to enemy") or a teammate.
        private void SampleLookTarget()
        {
            var local = GetLocalAvatar();
            if (local == null) return;
            var me = DirectorSnapshotPlayers().FirstOrDefault(p => p.Avatar == local);
            if (me == null) return;

            string key = null;
            var enemies = new List<(GameObject go, Vector3 center)>();
            if (SemiFunc.RunIsLevel())
                foreach (var go in GetEnemiesList())
                    if (go != null) enemies.Add((go, GetEnemyCenter(go)));
            var enemy = FindTalkTarget(me, enemies);
            if (enemy.go != null) key = "enemy:" + NormalizeEnemyName(enemy.go.name);

            if (key == null)
            {
                var bestScore = float.MaxValue;
                foreach (var a in SemiFunc.PlayerGetList())
                {
                    if (a == null || a.isLocal || a.photonView == null) continue;
                    var vision = a.PlayerVisionTarget != null ? a.PlayerVisionTarget.VisionTransform : null;
                    var head = vision != null ? vision.position : a.transform.position + Vector3.up * 1.5f;
                    var to = head - me.Eye;
                    var dist = to.magnitude;
                    if (dist > 15f || dist < 0.1f || Vector3.Angle(me.Forward, to) > 20f) continue;
                    var score = Vector3.Angle(me.Forward, to) / 20f + dist / 15f * 0.3f;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    key = $"player:{GetPlayerDisplayName(a.photonView.Owner)}|{a.photonView.Owner.ActorNumber}";
                }
            }

            if (key == null) return;
            _ctxLookTime.TryGetValue(key, out var t);
            _ctxLookTime[key] = t + 0.1f;
        }

        // Speech end: loudness, final look target and the reply link. Returns the serialized context.
        private string FinishRecordingContext(float[] samples, int sampleRateHz)
        {
            var ctx = _ctxCurrent;
            _ctxCurrent = null;
            if (ctx == null) return null;

            try
            {
                var duration = sampleRateHz > 0 ? samples.Length / (float)sampleRateHz : 0f;
                ctx.Set("dur", duration);
                if (duration < 1f) ctx.Tags.Add("short");
                else if (duration > 4f) ctx.Tags.Add("long");

                double sumSq = 0;
                var peak = 0f;
                foreach (var s in samples)
                {
                    sumSq += s * s;
                    var a = s < 0 ? -s : s;
                    if (a > peak) peak = a;
                }
                var rms = samples.Length > 0 ? (float)Math.Sqrt(sumSq / samples.Length) : 0f;
                ctx.Set("rms", rms);
                ctx.Set("peak", peak);
                // Relative to this player's own VAD threshold, so mic gain differences between players matter less.
                var ratio = rms / Mathf.Max(0.0005f, GetVadThreshold());
                ctx.Set("loudRatio", ratio);
                var loud = ratio < 1.5f ? "whisper" : ratio < 5f ? "normal" : ratio < 12f ? "loud" : "scream";
                ctx.Set("loud", loud);
                ctx.Tags.Add(loud);

                if (_ctxLookTime.Count > 0)
                {
                    var best = _ctxLookTime.OrderByDescending(kv => kv.Value).First();
                    if (best.Value >= Mathf.Min(0.4f, duration * 0.4f))
                    {
                        var look = best.Key;
                        var bar = look.IndexOf('|');
                        if (bar > 0)
                        {
                            ctx.Set("lookAtActor", look.Substring(bar + 1));
                            look = look.Substring(0, bar);
                        }
                        ctx.Set("lookAt", look);
                        ctx.Tags.Add(look.StartsWith("enemy:") ? "toEnemy" : "toPlayer");
                    }
                }

                ResolveReplyLink(ctx);

                if (!ctx.Has("danger") && !ctx.Has("hurt") && (loud == "normal" || loud == "whisper")) ctx.Tags.Add("calm");
                return ctx.Serialize();
            }
            catch (Exception ex)
            {
                DLog($"FinishRecordingContext failed: {ex.Message}");
                return null;
            }
        }

        // A reply: another player nearby stopped talking shortly before this phrase began.
        // Their clip has usually arrived by now, so it's linked by guid — the director replays real exchanges.
        private void ResolveReplyLink(RecordingContext ctx)
        {
            var local = GetLocalAvatar();
            var localPos = local != null ? local.transform.position : transform.position;
            var best = -1;
            var bestEnd = -999f;
            foreach (var kv in _otherSpeechEnded)
            {
                var (endedAt, pos, _) = kv.Value;
                var gap = _ctxStartedAt - endedAt;
                if (gap < -0.5f || gap > ReplyWindow || Vector3.Distance(pos, localPos) > 25f) continue;
                if (endedAt > bestEnd) { bestEnd = endedAt; best = kv.Key; }
            }
            if (best < 0) return;

            var info = _otherSpeechEnded[best];
            ctx.Set("replyToActor", best);
            ctx.Set("replyToName", info.name);
            ctx.Set("replyGap", _ctxStartedAt - info.endedAt);
            ctx.Tags.Add("reply");

            var clip = cachedAudio
                .Where(e => e != null && e.SourceActor == best && !string.IsNullOrEmpty(e.SoundGuid) && e.ReceivedAt >= info.endedAt - 1.5f)
                .OrderByDescending(e => e.ReceivedAt)
                .FirstOrDefault();
            if (clip != null) ctx.Set("replyTo", clip.SoundGuid);
        }
    }
}
