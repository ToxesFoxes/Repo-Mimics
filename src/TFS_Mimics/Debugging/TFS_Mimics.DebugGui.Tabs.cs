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
        private void DrawWindowTitleBar(string title, string hint)
        {
            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label(title, _gsH1, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.Space(6f);
            GUI.color = CTextDim;
            GUILayout.Label(hint, _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.Space(4f);
            if (GUILayout.Button("×", _gsBtnDanger, GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                _debugWindowOpen = false;
            }
            GUILayout.EndHorizontal();
            DrawHRule();
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

        // ─── Playback Header ──────────────────────────────────────────────────────
        private void DrawPlaybackHeader()
        {
            var isActive = Time.time <= currentPlaybackEndsAt;
            GUILayout.BeginVertical(_gsPanelBox);

            GUILayout.BeginHorizontal();
            GUI.color = isActive ? CGreen : CTextDim;
            GUILayout.Label(isActive ? "▶  NOW PLAYING" : "■  IDLE", _gsH1, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            // Recording indicator
            if (capturingSpeech)
            {
                var recDur = sampleRate > 0 ? bufferPosition / (float)sampleRate : 0f;
                var localNick = PhotonNetwork.LocalPlayer != null && !string.IsNullOrWhiteSpace(PhotonNetwork.LocalPlayer.NickName)
                    ? PhotonNetwork.LocalPlayer.NickName
                    : "You";
                GUI.color = CRed;
                GUILayout.Label($"● REC  {FitHudText(localNick, 16)}  {recDur:F1}s", _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            if (isActive)
            {
                GUILayout.BeginHorizontal();
                GUI.color = CTextDim;
                GUILayout.Label("Enemy:", _gsSmall, GUILayout.Width(44f));
                GUI.color = Color.white;
                GUILayout.Label(FitHudText(currentPlaybackEnemyName, 20), _gsLabel);
                GUILayout.Space(12f);
                GUI.color = CTextDim;
                GUILayout.Label("Source:", _gsSmall, GUILayout.Width(44f));
                GUI.color = Color.white;
                GUILayout.Label(FitHudText(currentPlaybackSourcePlayerId, 24), _gsLabel);
                GUILayout.EndHorizontal();

                float clipLen = 0f;
                float startedAt = 0f;
                foreach (var kv in playbackClipLengthByTargetKey)
                {
                    clipLen = kv.Value;
                    break;
                }
                foreach (var kv in playbackStartedAtByTargetKey)
                {
                    startedAt = kv.Value;
                    break;
                }
                if (clipLen <= 0f)
                {
                    clipLen = Mathf.Max(0f, currentPlaybackEndsAt - Time.time);
                }
                var elapsed = Mathf.Clamp(Time.time - startedAt, 0f, clipLen);
                GUILayout.Space(3f);
                DrawProgressBar(clipLen > 0f ? elapsed / clipLen : 0f, $"{elapsed:F1}s / {clipLen:F1}s", CGreen);
            }
            else
            {
                GUI.color = CTextDim;
                GUILayout.Label("No active playback", _gsSmall);
                GUI.color = Color.white;
            }

            GUILayout.EndVertical();
        }

        // ─── Tab Row ──────────────────────────────────────────────────────────────
        private void DrawTabRow()
        {
            GUILayout.BeginHorizontal();
            for (var i = 0; i < TabNames.Length; i++)
            {
                var style = _debugTab == i ? _gsTabBtnActive : _gsTabBtn;
                if (GUILayout.Button(TabNames[i], style, GUILayout.Height(26f)))
                {
                    _debugTab = i;
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶  Force Play…", _gsBtnYellow, GUILayout.Height(26f), GUILayout.ExpandWidth(false)))
            {
                OpenForcePlayModal();
            }
            GUILayout.EndHorizontal();
            DrawHRule();
        }

        // ─── Tab 1: Nearest Mobs ──────────────────────────────────────────────────
        private void DrawNearestMobsTab(float scrollH)
        {
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
            var onlineIds = GetOnlinePlayerIds();
            var localId = PhotonNetwork.LocalPlayer != null ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer) : string.Empty;

            var byPlayer = new Dictionary<string, (string name, int count)>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in cachedAudio)
            {
                if (e == null)
                {
                    continue;
                }

                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                var name = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid;
                byPlayer[pid] = byPlayer.TryGetValue(pid, out var cur) ? (cur.name, cur.count + 1) : (name, 1);
            }

            if (PhotonNetwork.PlayerList != null)
            {
                foreach (var p in PhotonNetwork.PlayerList)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    var pid = GetPlayerPersistentId(p);
                    if (!byPlayer.ContainsKey(pid))
                    {
                        byPlayer[pid] = (string.IsNullOrWhiteSpace(p.NickName) ? pid : p.NickName, 0);
                    }
                }
            }

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Online: {onlineIds.Count}   Players with clips: {byPlayer.Count(kv => kv.Value.count > 0)}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            _scrollPlayers = GUILayout.BeginScrollView(_scrollPlayers, GUILayout.Height(scrollH));

            var sorted = byPlayer
                .OrderByDescending(kv => onlineIds.Contains(kv.Key))
                .ThenBy(kv => kv.Value.name)
                .ToList();

            if (sorted.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No player data available.", _gsLabel);
                GUI.color = Color.white;
            }

            foreach (var kv in sorted)
            {
                var pid = kv.Key;
                var (pname, count) = kv.Value;
                var isOnline = onlineIds.Contains(pid);
                var isLocal = string.Equals(pid, localId, System.StringComparison.OrdinalIgnoreCase);
                DrawPlayerRow(pid, pname, count, isOnline, isLocal);
                GUILayout.Space(2f);
            }

            GUILayout.EndScrollView();
        }

        private void DrawPlayerRow(string pid, string name, int clipCount, bool isOnline, bool isLocal)
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
            GUILayout.EndHorizontal();

            GUI.color = CTextDim;
            GUILayout.Label($"  id: {FitHudText(pid, 40)}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndVertical();
        }

        // ─── Tab 3: Cache ─────────────────────────────────────────────────────────
        private void DrawCacheTab(float scrollH)
        {
            // ── Header: cache stats + toolbar ────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            var totalPlayers = PhotonNetwork.PlayerList?.Length ?? 0;
            var eligibleCount = totalPlayers > 0
                ? soundReadinessMap.Count(kv => System.Array.TrueForAll(PhotonNetwork.PlayerList, p => kv.Value.Contains(p.ActorNumber)))
                : 0;
            GUILayout.Label($"Total: {cachedAudio.Count} clips   In-progress: {incomingAudioTransmissions.Count} transmissions   Custom: {_customAudioClips.Count}   Readiness: {eligibleCount}/{soundReadinessMap.Count} eligible", _gsSmall);
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUI.color = CAccentDim;
            if (GUILayout.Button("↺ Reload Custom", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false)))
            {
                ReloadCustomAudio();
            }
            GUI.color = Color.white;
            GUILayout.Space(4f);
            if (GUILayout.Button("Clear Cache", _gsBtnDanger, GUILayout.Height(22f), GUILayout.ExpandWidth(false)))
            {
                cachedAudio.Clear();
                _cacheExpandedPlayers.Clear();
                DLog($"Debug HUD: cache manually cleared {DebugContext()}");
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            // Build per-player groups (ordered: most clips first)
            var byPlayer = new Dictionary<string, (string name, List<int> indices, float lastAt)>(System.StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cachedAudio.Count; i++)
            {
                var e = cachedAudio[i];
                if (e == null) continue;
                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                var name = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid;
                if (byPlayer.TryGetValue(pid, out var cur))
                {
                    cur.indices.Add(i);
                    byPlayer[pid] = (cur.name, cur.indices, Mathf.Max(cur.lastAt, e.ReceivedAt));
                }
                else
                {
                    byPlayer[pid] = (name, new List<int> { i }, e.ReceivedAt);
                }
            }

            var sorted = byPlayer
                .OrderByDescending(kv => kv.Value.indices.Count)
                .ToList();

            _scrollCache = GUILayout.BeginScrollView(_scrollCache, GUILayout.Height(scrollH));

            // ── Custom Audio section ─────────────────────────────────────────────
            var customClips = _customAudioClips;
            {
                var isExpanded = _cacheExpandedPlayers.Contains("__custom__");
                var chevron = isExpanded ? "▼" : "▶";

                GUILayout.BeginVertical(_gsPanelBox);
                GUILayout.BeginHorizontal();

                GUI.color = new Color(0.55f, 0.85f, 0.40f);
                if (GUILayout.Button(chevron, _gsBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (isExpanded) _cacheExpandedPlayers.Remove("__custom__");
                    else _cacheExpandedPlayers.Add("__custom__");
                    isExpanded = !isExpanded;
                }
                GUI.color = new Color(0.55f, 0.85f, 0.40f);
                GUILayout.Label("Custom Audio", _gsLabel);
                GUI.color = Color.white;
                GUILayout.FlexibleSpace();
                // Show breakdown: folder clips vs API-registered clips
                var folderCount = customClips.Count(e => e?.SourceMod == null);
                var apiCount = customClips.Count(e => e?.SourceMod != null);
                GUI.color = CTextDim;
                if (folderCount > 0)
                    GUILayout.Label($"{folderCount} folder", _gsSmall);
                if (apiCount > 0)
                {
                    if (folderCount > 0) { GUI.color = CTextDim; GUILayout.Label("|", _gsSmall, GUILayout.Width(10f)); }
                    GUI.color = CAccent;
                    GUILayout.Label($"{apiCount} mod", _gsSmall);
                }
                GUI.color = new Color(0.55f, 0.85f, 0.40f);
                GUILayout.Label($"{customClips.Count} file{(customClips.Count != 1 ? "s" : string.Empty)}", _gsSmall, GUILayout.Width(50f));
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                if (isExpanded)
                {
                    GUILayout.Space(2f);
                    var lineColor = new Color(0.22f, 0.36f, 0.24f);
                    var lr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                    GUI.color = lineColor;
                    GUI.DrawTexture(lr, _txWhite);
                    GUI.color = Color.white;
                    GUILayout.Space(2f);

                    if (customClips.Count == 0)
                    {
                        GUI.color = CTextDim;
                        GUILayout.Label("  Drop .mp3 or .wav files into BepInEx/plugins/ToxesFoxes-Mimics/custom-audio/", _gsSmall);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        for (var ci = 0; ci < customClips.Count; ci++)
                        {
                            var ce = customClips[ci];
                            if (ce?.Clip == null) continue;

                            GUILayout.BeginHorizontal();
                            GUILayout.Space(4f);

                            GUI.color = new Color(0.55f, 0.85f, 0.40f);
                            if (GUILayout.Button("▶", _gsBtnYellow, GUILayout.Width(24f), GUILayout.Height(18f)))
                            {
                                PlayCustomAudioEntry(ce);
                            }
                            GUI.color = Color.white;

                            GUILayout.Space(4f);
                            GUI.color = CTextDim;
                            GUILayout.Label($"#{ci + 1:D2}", _gsSmall, GUILayout.Width(30f));
                            GUI.color = CText;
                            GUILayout.Label($"{ce.Clip.length:F2}s", _gsSmall, GUILayout.Width(44f));
                            GUI.color = CTextDim;
                            // Filename — truncated to leave room for the source tag
                            GUILayout.Label(FitHudText(ce.FileName, ce.SourceMod != null ? 22 : 30), _gsSmall);
                            GUILayout.FlexibleSpace();
                            // Source tag: dim gray for folder, accent blue for API mods
                            if (ce.SourceMod != null)
                            {
                                GUI.color = CAccent;
                                GUILayout.Label($"[{FitHudText(ce.SourceMod, 20)}]", _gsSmall, GUILayout.Width(130f));
                            }
                            else
                            {
                                GUI.color = CTextDim;
                                GUILayout.Label("[folder]", _gsSmall, GUILayout.Width(55f));
                            }
                            // SoundGuid indicator
                            if (!string.IsNullOrEmpty(ce.SoundGuid))
                            {
                                GUI.color = new Color(0.4f, 0.7f, 0.4f);
                                GUILayout.Label("✓", _gsSmall, GUILayout.Width(14f));
                            }
                            GUI.color = Color.white;
                            GUILayout.EndHorizontal();
                        }
                    }
                    GUILayout.Space(2f);
                }

                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            if (sorted.Count == 0 && customClips.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  Cache is empty.", _gsLabel);
                GUI.color = Color.white;
            }

            foreach (var kv in sorted)
            {
                var pid = kv.Key;
                var (pname, indices, lastAt) = kv.Value;
                var age = Time.time - lastAt;
                var ageStr = age < 60f ? $"{age:F0}s ago" : $"{age / 60f:F1}m ago";
                var isExpanded = _cacheExpandedPlayers.Contains(pid);
                var chevron = isExpanded ? "▼" : "▶";

                // ── Header row ──────────────────────────────────────────────────
                GUILayout.BeginVertical(_gsPanelBox);
                GUILayout.BeginHorizontal();

                // Expand/collapse button — sits outside the panel background edge
                GUI.color = CAccentDim;
                if (GUILayout.Button(chevron, _gsBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (isExpanded) _cacheExpandedPlayers.Remove(pid);
                    else _cacheExpandedPlayers.Add(pid);
                    isExpanded = !isExpanded;
                }
                GUI.color = Color.white;

                GUI.color = CAccent;
                GUILayout.Label(pname, _gsLabel);
                GUI.color = Color.white;
                GUILayout.FlexibleSpace();
                GUI.color = CTextDim;
                GUILayout.Label(ageStr, _gsSmall, GUILayout.Width(58f));
                GUI.color = CYellow;
                GUILayout.Label($"{indices.Count} clip{(indices.Count != 1 ? "s" : string.Empty)}", _gsSmall, GUILayout.Width(50f));
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                // ID row
                GUILayout.BeginHorizontal();
                GUILayout.Space(26f);
                GUI.color = CTextDim;
                GUILayout.Label($"id: {FitHudText(pid, 38)}", _gsSmall);
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                // ── Per-player volume slider ─────────────────────────────────────
                {
                    if (!_playerVolBuf.TryGetValue(pid, out var volBuf))
                    {
                        var currentVol = playerVolumeOverrides.TryGetValue(pid, out var v) && v >= 0 ? v : -1;
                        volBuf = currentVol >= 0 ? currentVol.ToString() : string.Empty;
                        _playerVolBuf[pid] = volBuf;
                    }
                    var storedVol = playerVolumeOverrides.TryGetValue(pid, out var sv) && sv >= 0 ? sv : -1;

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(26f);
                    GUI.color = CTextDim;
                    GUILayout.Label("Vol:", _gsSmall, GUILayout.Width(26f));
                    GUI.color = Color.white;

                    // Slider: -1 = global, 0-100 = override
                    var sliderCurrent = storedVol >= 0 ? storedVol : (Plugin.configVoiceVolume?.Value ?? 100);
                    var newSlider = GUILayout.HorizontalSlider(sliderCurrent, 0f, 100f, GUILayout.Width(90f), GUILayout.Height(12f));
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
                    GUILayout.Label("%", _gsSmall, GUILayout.Width(14f));
                    GUI.color = Color.white;

                    if (storedVol >= 0)
                    {
                        if (GUILayout.Button("↺", _gsBtn, GUILayout.Width(20f), GUILayout.Height(16f)))
                        {
                            playerVolumeOverrides.Remove(pid);
                            _playerVolBuf.Remove(pid);
                            SavePlayersIndexToDisk();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }

                // ── Clip list (expanded) ─────────────────────────────────────────
                if (isExpanded)
                {
                    GUILayout.Space(2f);
                    var lineColor = new Color(0.22f, 0.28f, 0.36f);
                    var lr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                    GUI.color = lineColor;
                    GUI.DrawTexture(lr, _txWhite);
                    GUI.color = Color.white;
                    GUILayout.Space(2f);

                    for (var ci = 0; ci < indices.Count; ci++)
                    {
                        var idx = indices[ci];
                        if (idx < 0 || idx >= cachedAudio.Count) continue;
                        var entry = cachedAudio[idx];
                        if (entry == null) continue;

                        var dur = entry.SampleRate > 0 && entry.AudioData != null
                            ? entry.AudioData.Length / (float)(entry.SampleRate * 2)
                            : 0f;
                        var clipAge = Time.time - entry.ReceivedAt;
                        var clipAgeStr = clipAge < 60f ? $"{clipAge:F0}s" : $"{clipAge / 60f:F1}m";

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(4f);

                        // Play button
                        GUI.color = CYellow;
                        if (GUILayout.Button("▶", _gsBtnYellow, GUILayout.Width(24f), GUILayout.Height(18f)))
                        {
                            PlayCacheEntryOnNearest(entry);
                        }
                        GUI.color = Color.white;

                        GUILayout.Space(4f);
                        // Index
                        GUI.color = CTextDim;
                        GUILayout.Label($"#{ci + 1:D2}", _gsSmall, GUILayout.Width(30f));
                        // Duration
                        GUI.color = CText;
                        GUILayout.Label($"{dur:F2}s", _gsSmall, GUILayout.Width(44f));
                        // Age
                        GUI.color = CTextDim;
                        GUILayout.Label($"{clipAgeStr} ago", _gsSmall, GUILayout.Width(52f));
                        // Bytes
                        var kb = entry.AudioData != null ? entry.AudioData.Length / 1024f : 0f;
                        GUILayout.Label(kb >= 1f ? $"{kb:F1}kb" : $"{entry.AudioData?.Length ?? 0}b", _gsSmall, GUILayout.Width(40f));
                        GUI.color = Color.white;
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Space(2f);
                }

                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            GUILayout.EndScrollView();

            // ── Sound Readiness Map ─────────────────────────────────────────────
            GUILayout.Space(4f);
            DrawReadinessSection();
        }

        private void DrawReadinessSection()
        {
            var allPlayers = PhotonNetwork.PlayerList;
            var totalPlayers = allPlayers?.Length ?? 0;

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
                var eligible = totalPlayers > 0 && allPlayers != null &&
                               System.Array.TrueForAll(allPlayers, p => kv.Value.Contains(p.ActorNumber));

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

            DLog($"PlayCacheEntryOnNearest: playing on {nearest.EnemyName} dist={nearest.Distance:F1}m {DebugContext()}");
            PlayReceivedAudioOnTarget(entry, nearest.Enemy, nearest.Target);
        }
    }
}
