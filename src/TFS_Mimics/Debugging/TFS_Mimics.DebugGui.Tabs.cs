using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Shared Helpers ───────────────────────────────────────────────────────
        private void DrawWindowTitleBar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(22f));
            GUILayout.Label("MIMICS", _gsTitle, GUILayout.ExpandWidth(false));
            GUILayout.Space(6f);

            // Status chips: role, steam networking, players, where we are
            var inRoom = PhotonNetwork.InRoom;
            DrawChip(!inRoom ? "offline" : PhotonNetwork.IsMasterClient ? "host" : "client",
                PhotonNetwork.IsMasterClient ? CAccent : CTextDim);

            var rsnUp = patches.SteamNetworkingCompat.IsClientConnected();
            var rsnLabel = rsnUp ? "rsn" : "rsn off";
            if (patches.SteamNetworkingCompat.Active) rsnLabel += " · compat";
            DrawChip(rsnLabel, rsnUp ? CGreen : (inRoom ? CRed : CTextDim));

            var players = PhotonNetwork.PlayerList?.Length ?? 0;
            if (inRoom) DrawChip($"{players} player{(players != 1 ? "s" : string.Empty)}", CTextDim);

            DrawChip(SemiFunc.RunIsLevel() ? "level" : SemiFunc.RunIsShop() ? "shop" : "lobby", CTextDim);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Plugin.configDebugMenuKey.Value} · shift+{Plugin.configDebugMenuKey.Value} focus", _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.Space(6f);
            if (GUILayout.Button(_debugWindowCollapsed ? "▢" : "–", _gsIconBtn, GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                SetWindowCollapsed(!_debugWindowCollapsed);
            }
            GUILayout.Space(2f);
            if (GUILayout.Button("×", _gsBtnDanger, GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                _debugWindowOpen = false;
                StopRecordingPreview();
            }
            GUILayout.EndHorizontal();
            DrawHRule();
        }

        private void DrawChip(string text, Color color)
        {
            GUI.color = color;
            GUILayout.Label(text, _gsChip, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
        }

        private void DrawHRule()
        {
            GUILayout.Space(2f);
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            GUI.color = new Color(0.22f, 0.28f, 0.36f);
            GUI.DrawTexture(r, _txWhite);
            GUI.color = Color.white;
            GUILayout.Space(2f);
        }

        private void DrawProgressBar(float t, string label, Color barColor)
        {
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(13f));
            GUI.color = new Color(0.15f, 0.19f, 0.24f);
            GUI.DrawTexture(r, _txWhite);
            var fillW = r.width * Mathf.Clamp01(t);
            if (fillW > 0f)
            {
                GUI.color = barColor;
                GUI.DrawTexture(new Rect(r.x, r.y, fillW, r.height), _txWhite);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 4f, r.y - 1f, r.width - 8f, r.height + 2f), label, _gsSmall);
        }

        private static string FitHudText(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "—";
            }

            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 2) + "…";
        }

        // ─── Tab 1: Nearest Mobs ──────────────────────────────────────────────────
        private void DrawNearestMobsTab(float scrollH)
        {
            if (!SemiFunc.RunIsLevel())
            {
                GUI.color = CTextDim;
                GUILayout.Label("  Enemies only exist inside levels.", _gsLabel);
                GUI.color = Color.white;
                return;
            }

            var nearRadius = Plugin.configPlaybackNearRadius != null ? Plugin.configPlaybackNearRadius.Value : 12f;

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Radius: {nearRadius:F0}m   Player: {FormatHudVector(transform.position)}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            _scrollMobs = GUILayout.BeginScrollView(_scrollMobs, GUILayout.Height(scrollH));
            if (nearestPlaybackCandidatesHud.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No eligible enemies in scene.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                foreach (var c in nearestPlaybackCandidatesHud)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    DrawMobRow(c, nearRadius);
                    GUILayout.Space(2f);
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawMobRow(HudPlaybackCandidate c, float nearRadius)
        {
            var inRadius = c.Distance <= nearRadius;
            GUILayout.BeginVertical(_gsPanelBox);

            GUILayout.BeginHorizontal();
            GUI.color = c.IsPlaying ? CGreen : (inRadius ? CAccent : CTextDim);
            GUILayout.Label(c.IsPlaying ? "▶" : (inRadius ? "●" : "○"), _gsLabel, GUILayout.Width(14f));
            GUI.color = Color.white;

            GUILayout.Label(c.EnemyName, c.IsPlaying ? new GUIStyle(_gsLabel) { fontStyle = FontStyle.Bold } : _gsLabel);

            GUILayout.FlexibleSpace();

            GUI.color = inRadius ? CText : CTextDim;
            GUILayout.Label($"{c.Distance:F1}m", _gsSmall, GUILayout.Width(46f));
            GUI.color = Color.white;

            if (!inRadius)
            {
                GUI.color = new Color(0.35f, 0.35f, 0.35f);
                GUILayout.Label("out of radius", _gsSmall, GUILayout.Width(78f));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            if (c.IsPlaying)
            {
                var key = GetPlaybackTargetKey(c.Enemy, c.Target);
                var startAt = playbackStartedAtByTargetKey.TryGetValue(key, out var sa) ? sa : Time.time;
                var clipLen = playbackClipLengthByTargetKey.TryGetValue(key, out var cl) ? cl : 0f;
                var elapsed = Mathf.Clamp(Time.time - startAt, 0f, clipLen);
                GUILayout.Space(2f);
                DrawProgressBar(clipLen > 0f ? elapsed / clipLen : 0f, $"{elapsed:F1}s / {clipLen:F1}s", CGreen);
            }

            GUI.color = CTextDim;
            GUILayout.Label($"  {FormatHudVector(c.Position)}", _gsSmall);
            GUI.color = Color.white;

            GUILayout.EndVertical();
        }

        // ─── Tab 2: Players ───────────────────────────────────────────────────────
        private void DrawPlayersTab(float scrollH)
        {
            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Online: {_cachedPlayersOnlineIds.Count}   Players with clips: {_cachedPlayersWithClips}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            const int playersPerPage = 15;
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(_cachedPlayersSorted.Count / (float)playersPerPage));
            _playersPage = Mathf.Clamp(_playersPage, 0, totalPages - 1);

            if (totalPages > 1)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = _playersPage > 0;
                if (GUILayout.Button("◀", _gsBtn, GUILayout.Width(30f), GUILayout.Height(20f))) _playersPage--;
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Page {_playersPage + 1} / {totalPages}", _gsSmall);
                GUILayout.FlexibleSpace();
                GUI.enabled = _playersPage < totalPages - 1;
                if (GUILayout.Button("▶", _gsBtn, GUILayout.Width(30f), GUILayout.Height(20f))) _playersPage++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            _scrollPlayers = GUILayout.BeginScrollView(_scrollPlayers, GUILayout.Height(scrollH));

            if (_cachedPlayersSorted.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No player data available.", _gsLabel);
                GUI.color = Color.white;
            }

            var startIdx = _playersPage * playersPerPage;
            var endIdx = Mathf.Min(startIdx + playersPerPage, _cachedPlayersSorted.Count);

            for (var i = startIdx; i < endIdx; i++)
            {
                var (pid, pname, count) = _cachedPlayersSorted[i];
                var isOnline = _cachedPlayersOnlineIds.Contains(pid);
                var isLocal = string.Equals(pid, _cachedPlayersLocalId, System.StringComparison.OrdinalIgnoreCase);
                _avatarMapCache.TryGetValue(pid, out var rowAvatar);
                DrawPlayerRow(pid, pname, count, isOnline, isLocal, rowAvatar);
                GUILayout.Space(2f);
            }

            GUILayout.EndScrollView();
        }

        // ─── Avatar Map Cache ─────────────────────────────────────────────────────
        private Dictionary<string, PlayerAvatar> _avatarMapCache = new Dictionary<string, PlayerAvatar>(StringComparer.OrdinalIgnoreCase);
        private float _avatarMapNextRebuild;
        private const float AvatarMapRebuildInterval = 1f;

        private void RebuildAvatarMapIfNeeded()
        {
            if (Time.unscaledTime < _avatarMapNextRebuild) return;
            _avatarMapNextRebuild = Time.unscaledTime + AvatarMapRebuildInterval;

            _avatarMapCache.Clear();
            var avatars = UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var avatar in avatars)
            {
                if (avatar == null) continue;

                if (!string.IsNullOrWhiteSpace(avatar.steamID) && avatar.steamID != "0")
                    _avatarMapCache[avatar.steamID] = avatar;

                var pv = avatar.photonView;
                if (pv?.Owner != null)
                {
                    _avatarMapCache[$"actor_{pv.Owner.ActorNumber}"] = avatar;
                    if (!string.IsNullOrWhiteSpace(pv.Owner.UserId))
                        _avatarMapCache[pv.Owner.UserId] = avatar;
                }
            }
        }

        private void DrawPlayerRow(string pid, string name, int clipCount, bool isOnline, bool isLocal, PlayerAvatar avatar)
        {
            GUILayout.BeginVertical(_gsPanelBox);
            GUILayout.BeginHorizontal();

            GUI.color = isOnline ? (isLocal ? CYellow : CGreen) : CTextDim;
            GUILayout.Label(isOnline ? "●" : "○", _gsLabel, GUILayout.Width(14f));
            GUI.color = Color.white;

            GUILayout.Label(name + (isLocal ? "  (you)" : string.Empty),
                isLocal ? new GUIStyle(_gsLabel) { fontStyle = FontStyle.Bold } : _gsLabel);
            GUILayout.FlexibleSpace();

            GUI.color = clipCount > 0 ? CAccent : CTextDim;
            GUILayout.Label($"{clipCount} clip{(clipCount != 1 ? "s" : string.Empty)}", _gsSmall, GUILayout.Width(56f));
            GUI.color = Color.white;

            if (!isOnline)
            {
                GUI.color = CTextDim;
                GUILayout.Label("[offline]", _gsSmall, GUILayout.Width(52f));
                GUI.color = Color.white;
            }

            var isMuted = avatar != null && avatar.voiceChatFetched && avatar.voiceChat != null && avatar.voiceChat.toggleMute;
            if (isMuted)
            {
                GUI.color = CRed;
                GUILayout.Label("✕ mic", _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            GUI.color = CTextDim;
            GUILayout.Label($"  id: {FitHudText(pid, 40)}", _gsSmall);
            GUI.color = Color.white;

            if (avatar != null)
            {
                // ── State + Head + Movement ───────────────────────────────
                GUILayout.BeginHorizontal();
                GUI.color = CTextDim;
                GUILayout.Label("  state:", _gsSmall, GUILayout.Width(42f));
                GUI.color = Color.white;

                if (avatar.isDisabled)
                {
                    GUI.color = CRed;
                    GUILayout.Label("DEAD", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }
                else if (avatar.deadSet)
                {
                    GUI.color = CYellow;
                    GUILayout.Label("DYING", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }
                else if (avatar.spawned)
                {
                    GUI.color = CGreen;
                    GUILayout.Label("ALIVE", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = CTextDim;
                    GUILayout.Label("SPAWNING", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }

                if (avatar.playerDeathHead != null && avatar.playerDeathHead.triggered)
                {
                    GUILayout.Space(8f);
                    GUI.color = CYellow;
                    var headLoc = avatar.playerDeathHead.inTruck ? "truck"
                                : avatar.playerDeathHead.inExtractionPoint ? "extraction" : "world";
                    GUILayout.Label($"HEAD ({headLoc})", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }

                var moveParts = new List<string>();
                if (avatar.isTumbling) moveParts.Add("tumble");
                if (avatar.isCrouching) moveParts.Add("crouch");
                if (avatar.isSprinting) moveParts.Add("sprint");
                if (avatar.isCrawling) moveParts.Add("crawl");
                if (avatar.isSliding) moveParts.Add("slide");
                if (moveParts.Count > 0)
                {
                    GUILayout.Space(8f);
                    GUI.color = CTextDim;
                    GUILayout.Label(string.Join(" ", moveParts), _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }

                GUILayout.EndHorizontal();

                // ── Health bar ────────────────────────────────────────────
                if (!avatar.isDisabled && avatar.playerHealth != null)
                {
                    var hp = avatar.playerHealth.health;
                    var maxHp = avatar.playerHealth.maxHealth;
                    var t = maxHp > 0 ? (float)hp / maxHp : 0f;
                    var barColor = t > 0.5f ? CGreen : t > 0.25f ? CYellow : CRed;
                    DrawProgressBar(t, $"HP  {hp} / {maxHp}", barColor);
                }
            }

            if (clipCount > 0 || isOnline)
            {
                GUILayout.Space(2f);
                DrawPlayerVolumeRow(pid);
            }
            GUILayout.EndVertical();
        }

        // Per-player mimic volume override (-1 / absent = global volume)
        private void DrawPlayerVolumeRow(string pid)
        {
            if (!_playerVolBuf.TryGetValue(pid, out var volBuf))
            {
                var currentVol = playerVolumeOverrides.TryGetValue(pid, out var v) && v >= 0 ? v : -1;
                volBuf = currentVol >= 0 ? currentVol.ToString() : string.Empty;
                _playerVolBuf[pid] = volBuf;
            }
            var storedVol = playerVolumeOverrides.TryGetValue(pid, out var sv) && sv >= 0 ? sv : -1;

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label("  mimic vol:", _gsSmall, GUILayout.Width(64f));
            GUI.color = Color.white;

            var sliderCurrent = storedVol >= 0 ? storedVol : (Plugin.configVoiceVolume?.Value ?? 100);
            var newSlider = GUILayout.HorizontalSlider(sliderCurrent, 0f, 100f, GUILayout.Width(110f), GUILayout.Height(12f));
            var newInt = Mathf.RoundToInt(newSlider);
            if (newInt != storedVol && newInt != sliderCurrent)
            {
                playerVolumeOverrides[pid] = Mathf.Clamp(newInt, 0, 100);
                _playerVolBuf[pid] = playerVolumeOverrides[pid].ToString();
                SavePlayersIndexToDisk();
            }

            GUI.SetNextControlName("pvol_" + pid);
            var newBuf = GUILayout.TextField(volBuf, 3, _gsLabel, GUILayout.Width(32f));
            if (newBuf != volBuf)
            {
                _playerVolBuf[pid] = newBuf;
                if (int.TryParse(newBuf, out var parsed))
                {
                    playerVolumeOverrides[pid] = Mathf.Clamp(parsed, 0, 100);
                    SavePlayersIndexToDisk();
                }
            }

            GUI.color = CTextDim;
            GUILayout.Label(storedVol >= 0 ? "%" : "% (global)", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;

            if (storedVol >= 0 && GUILayout.Button("↺", _gsIconBtn, GUILayout.Width(20f), GUILayout.Height(16f)))
            {
                playerVolumeOverrides.Remove(pid);
                _playerVolBuf.Remove(pid);
                SavePlayersIndexToDisk();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawReadinessSection()
        {
            var totalPlayers = PhotonNetwork.PlayerList?.Length ?? 0;

            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label("SOUND READINESS", _gsH1, GUILayout.ExpandWidth(false));
            GUI.color = CTextDim;
            GUILayout.Label($"({soundReadinessMap.Count} guids  |  {totalPlayers} player{(totalPlayers != 1 ? "s" : "")})", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", _gsBtnDanger, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
                soundReadinessMap.Clear();
            GUILayout.EndHorizontal();
            DrawHRule();

            if (soundReadinessMap.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No sound GUIDs registered yet.", _gsSmall);
                GUI.color = Color.white;
                return;
            }

            _scrollReadiness = GUILayout.BeginScrollView(_scrollReadiness, GUILayout.Height(Mathf.Min(130f, soundReadinessMap.Count * 19f + 6f)));

            foreach (var kv in soundReadinessMap)
            {
                var guid = kv.Key;
                var have = kv.Value.Count;
                _cachedReadinessEligible.TryGetValue(guid, out var eligible);

                GUILayout.BeginHorizontal();
                GUI.color = eligible ? CGreen : CYellow;
                GUILayout.Label(eligible ? "●" : "○", _gsSmall, GUILayout.Width(12f));
                GUI.color = CText;
                var shortGuid = guid.Length > 16 ? guid.Substring(0, 16) + "…" : guid;
                GUILayout.Label(shortGuid, _gsSmall, GUILayout.Width(180f));
                GUI.color = CTextDim;
                GUILayout.Label($"{have}/{totalPlayers}", _gsSmall, GUILayout.Width(40f));
                if (eligible)
                {
                    GUI.color = CGreen;
                    GUILayout.Label("eligible", _gsSmall, GUILayout.ExpandWidth(false));
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private void PlayCacheEntryOnNearest(CachedAudioEntry entry)
        {
            if (entry == null || entry.AudioData == null || entry.AudioData.Length == 0)
            {
                DLog($"PlayCacheEntryOnNearest: entry empty {DebugContext()}");
                return;
            }

            RefreshHudTargetsSnapshot();

            var nearest = nearestPlaybackCandidatesHud
                .Where(c => c != null)
                .OrderBy(c => c.Distance)
                .FirstOrDefault();

            if (nearest == null)
            {
                DLog($"PlayCacheEntryOnNearest: no enemy targets found {DebugContext()}");
                return;
            }

            var viewId = GetEnemyNetViewId(nearest.Enemy);
            if (viewId < 0)
            {
                DLog($"PlayCacheEntryOnNearest: nearest enemy has no ViewID {DebugContext()}");
                return;
            }

            DLog($"PlayCacheEntryOnNearest: playing on {nearest.EnemyName} {DebugContext()}");

            // Use Host Authority Tick logic to synchronize playback
            // Pass -2 to use the default host-side random filter selection
            HostAuthorityTick(entry.SoundGuid, new int[] { viewId }, -2);
        }

        // ─── Tab Cache Rebuild ────────────────────────────────────────────────────
        private void RebuildActiveTabCache()
        {
            switch (_debugTab)
            {
                case TabRecordings: RebuildRecordingsCacheIfNeeded(); break;
                case TabPlayers: RebuildPlayersTabCache(); break;
                case TabNetwork:
                    RebuildVoiceLogCache();
                    RebuildReadinessCache();
                    break;
            }
        }

        private void RebuildPlayersTabCache()
        {
            _cachedPlayersOnlineIds.Clear();
            foreach (var id in GetOnlinePlayerIds())
                _cachedPlayersOnlineIds.Add(id);

            _cachedPlayersLocalId = PhotonNetwork.LocalPlayer != null
                ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer)
                : string.Empty;

            var byPlayer = new Dictionary<string, (string name, int count)>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in cachedAudio)
            {
                if (e == null) continue;
                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                var name = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid;
                byPlayer[pid] = byPlayer.TryGetValue(pid, out var cur) ? (cur.name, cur.count + 1) : (name, 1);
            }

            if (PhotonNetwork.PlayerList != null)
            {
                foreach (var p in PhotonNetwork.PlayerList)
                {
                    if (p == null) continue;
                    var pid = GetPlayerPersistentId(p);
                    if (!byPlayer.ContainsKey(pid))
                        byPlayer[pid] = (string.IsNullOrWhiteSpace(p.NickName) ? pid : p.NickName, 0);
                }
            }

            _cachedPlayersSorted.Clear();
            _cachedPlayersWithClips = 0;
            foreach (var kv in byPlayer.OrderByDescending(kv => _cachedPlayersOnlineIds.Contains(kv.Key)).ThenBy(kv => kv.Value.name))
            {
                _cachedPlayersSorted.Add((kv.Key, kv.Value.name, kv.Value.count));
                if (kv.Value.count > 0) _cachedPlayersWithClips++;
            }
        }

        private void RebuildReadinessCache()
        {
            var allPlayers = PhotonNetwork.PlayerList;
            var totalPlayers = allPlayers?.Length ?? 0;

            _cachedReadinessEligibleCount = 0;
            _cachedReadinessEligible.Clear();
            foreach (var kv in soundReadinessMap)
            {
                var eligible = totalPlayers > 0 && allPlayers != null
                    && System.Array.TrueForAll(allPlayers, p => kv.Value.Contains(p.ActorNumber));
                _cachedReadinessEligible[kv.Key] = eligible;
                if (eligible) _cachedReadinessEligibleCount++;
            }
        }
    }
}
