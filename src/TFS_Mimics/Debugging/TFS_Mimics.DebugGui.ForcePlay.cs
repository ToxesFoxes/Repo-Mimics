using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Audio;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Force-Play Modal ─────────────────────────────────────────────────────
        private void OpenForcePlayModal()
        {
            _fpmOpen = true;
            _fpmPage = 0;
            _fpmPlayerIdx = -1;
            _fpmClipIdx = -1;
            _fpmEnemyIdx = -1;
            _fpmFilterIdx = -1;
            _fpmHearYourself = true; // Default to true so random works in solo testing
            _fpmScrollPlayer = Vector2.zero;
            _fpmScrollClip = Vector2.zero;
            _fpmScrollEnemy = Vector2.zero;
            _fpmScrollFilter = Vector2.zero;
            _fpmPagePlayer = 0;
            _fpmPageClip = 0;
            _fpmPageEnemy = 0;
            _fpmPageFilter = 0;
            BuildFpmPlayerList();
        }

        private void BuildFpmPlayerList()
        {
            var localId = PhotonNetwork.LocalPlayer != null ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer) : string.Empty;
            var onlineIds = GetOnlinePlayerIds();
            var byPlayer = new Dictionary<string, FpmPlayerEntry>(System.StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < cachedAudio.Count; i++)
            {
                var e = cachedAudio[i];
                if (e == null)
                {
                    continue;
                }

                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                var name = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid;

                if (!byPlayer.TryGetValue(pid, out var entry))
                {
                    entry = new FpmPlayerEntry
                    {
                        PlayerId = pid,
                        DisplayName = name,
                        IsLocalPlayer = string.Equals(pid, localId, System.StringComparison.OrdinalIgnoreCase),
                        IsOnline = onlineIds.Contains(pid)
                    };
                    byPlayer[pid] = entry;
                }

                entry.CacheIndices.Add(i);
            }

            _fpmPlayers = byPlayer.Values
                .OrderByDescending(p => p.IsOnline)
                .ThenBy(p => p.DisplayName)
                .ToList();
        }

        private void BuildFpmEnemyList()
        {
            _fpmEnemies = new List<FpmEnemyEntry>();
            var listenerPos = transform.position;
            var enemies = GetEnemiesList().Where(e => e != null).ToList();

            foreach (var enemy in enemies)
            {
                var target = GetEnemyAudioTarget(enemy);
                var pos = GetEnemyDistancePosition(enemy, target);
                var key = GetPlaybackTargetKey(enemy, target);
                var isPlaying = key != 0
                    && playbackBusyUntilByTargetKey.TryGetValue(key, out var bt)
                    && bt > Time.time;

                _fpmEnemies.Add(new FpmEnemyEntry
                {
                    Enemy = enemy,
                    Target = target,
                    Name = NormalizeEnemyName(enemy.name),
                    Distance = Vector3.Distance(pos, listenerPos),
                    IsPlaying = isPlaying
                });
            }

            _fpmEnemies = _fpmEnemies.OrderBy(e => e.Distance).ToList();
        }

        private void DrawForcePlayModal(int id)
        {
            // Title bar
            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label("FORCE  PLAY", _gsH1);
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", _gsBtnDanger, GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                _fpmOpen = false;
            }
            GUILayout.EndHorizontal();
            DrawHRule();
            GUILayout.Space(2f);

            // Page indicator
            DrawFpmPageIndicator();
            GUILayout.Space(2f);
            DrawHRule();
            GUILayout.Space(4f);

            switch (_fpmPage)
            {
                case 0: DrawFpmPagePlayer(); break;
                case 1: DrawFpmPageClip(); break;
                case 2: DrawFpmPageEnemy(); break;
                case 3: DrawFpmPageFilter(); break;
            }
        }

        private void DrawFpmPageIndicator()
        {
            var pages = new[] { "1. Player", "2. Clip", "3. Enemy", "4. Filter" };
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (var i = 0; i < pages.Length; i++)
            {
                // Skip 'Clip' page if 'Random' source is selected
                if (_fpmPlayerIdx == -1 && i == 1)
                {
                    continue;
                }

                if (i < _fpmPage)
                {
                    GUI.color = CGreen;
                }
                else if (i == _fpmPage)
                {
                    GUI.color = CAccent;
                }
                else
                {
                    GUI.color = CTextDim;
                }

                GUILayout.Label(i < _fpmPage ? $"✓ {pages[i]}" : pages[i], _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;

                if (i < pages.Length - 1)
                {
                    GUI.color = CTextDim;
                    GUILayout.Label(" ›", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ── Modal Page 1: Player ────────────────────────────────────────────────
        private void DrawFpmPagePlayer()
        {
            GUI.color = CTextDim;
            GUILayout.Label("Select player to play from:", _gsSmall);
            GUI.color = Color.white;
            GUILayout.Space(4f);

            _fpmScrollPlayer = GUILayout.BeginScrollView(_fpmScrollPlayer, GUILayout.Height(210f));

            if (GUILayout.Button("▶  Random  (any player in cache)", _fpmPlayerIdx == -1 ? _gsListItemSel : _gsListItem))
            {
                _fpmPlayerIdx = -1;
            }
            GUILayout.Space(2f);

            if (_fpmPlayers == null || _fpmPlayers.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  Cache is empty — no players available.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                var pageSize = 10;
                var totalPages = Mathf.Max(1, Mathf.CeilToInt(_fpmPlayers.Count / (float)pageSize));
                if (_fpmPagePlayer >= totalPages) _fpmPagePlayer = totalPages - 1;

                var start = _fpmPagePlayer * pageSize;
                var end = Mathf.Min(start + pageSize, _fpmPlayers.Count);

                for (var i = start; i < end; i++)
                {
                    var p = _fpmPlayers[i];
                    var selected = _fpmPlayerIdx == i;
                    var dot = p.IsOnline ? "●" : "○";
                    var selfTag = p.IsLocalPlayer ? " (you)" : string.Empty;
                    var label = $"{dot}  {p.DisplayName}{selfTag}  [{p.CacheIndices.Count} clip{(p.CacheIndices.Count != 1 ? "s" : string.Empty)}]";

                    var prevColor = GUI.color;
                    if (GUILayout.Button(label, selected ? _gsListItemSel : _gsListItem))
                    {
                        _fpmPlayerIdx = i;
                        _fpmClipIdx = -1;
                        _fpmPageClip = 0;
                    }
                    GUI.color = prevColor;
                    GUILayout.Space(2f);
                }

                if (totalPages > 1)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUI.enabled = _fpmPagePlayer > 0;
                    if (GUILayout.Button("«", _gsBtn, GUILayout.Width(30f))) _fpmPagePlayer--;
                    GUI.enabled = true;
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"Page {_fpmPagePlayer + 1} / {totalPages}", _gsSmall);
                    GUILayout.FlexibleSpace();
                    GUI.enabled = _fpmPagePlayer < totalPages - 1;
                    if (GUILayout.Button("»", _gsBtn, GUILayout.Width(30f))) _fpmPagePlayer++;
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();

            // Hear yourself checkbox
            var showHearYourself = _fpmPlayerIdx == -1
                || (_fpmPlayers != null && _fpmPlayerIdx >= 0 && _fpmPlayerIdx < _fpmPlayers.Count && _fpmPlayers[_fpmPlayerIdx].IsLocalPlayer);

            if (showHearYourself)
            {
                GUILayout.Space(4f);
                GUILayout.BeginVertical(_gsPanelBox);
                var label = _fpmPlayerIdx == -1
                    ? "Hear Yourself  (allow own clips in random)"
                    : "Hear Yourself  (this is your clip)";
                _fpmHearYourself = GUILayout.Toggle(_fpmHearYourself, $"  {label}", _gsLabel);
                GUILayout.EndVertical();
            }
            else
            {
                _fpmHearYourself = true; // not own clip — always fine
            }

            DrawHRule();
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", _gsBtn, GUILayout.Height(28f), GUILayout.ExpandWidth(false), GUILayout.Width(80f)))
            {
                _fpmOpen = false;
            }
            GUILayout.FlexibleSpace();

            var canNext = _fpmPlayerIdx == -1
                || (_fpmPlayers != null && _fpmPlayerIdx >= 0 && _fpmPlayerIdx < _fpmPlayers.Count);

            GUI.enabled = canNext;
            var nextLabel = _fpmPlayerIdx == -1 ? "Next: Enemy →" : "Next: Clip →";
            if (GUILayout.Button(nextLabel, _gsBtnPrimary, GUILayout.Height(28f), GUILayout.Width(130f)))
            {
                if (_fpmPlayerIdx == -1)
                {
                    BuildFpmEnemyList();
                    _fpmPage = 2;
                }
                else
                {
                    _fpmClipIdx = -1;
                    _fpmPageClip = 0;
                    _fpmPage = 1;
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // ── Modal Page 2: Clip ──────────────────────────────────────────────────
        private void DrawFpmPageClip()
        {
            var player = _fpmPlayers != null && _fpmPlayerIdx >= 0 && _fpmPlayerIdx < _fpmPlayers.Count
                ? _fpmPlayers[_fpmPlayerIdx]
                : null;

            if (player == null)
            {
                _fpmPage = 0;
                return;
            }

            GUI.color = CTextDim;
            GUILayout.Label($"Clips from:  ", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label(player.DisplayName, _gsLabel, GUILayout.ExpandWidth(false));
            GUI.color = CTextDim;
            GUILayout.Label($"  ({player.CacheIndices.Count} clips)", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            _fpmScrollClip = GUILayout.BeginScrollView(_fpmScrollClip, GUILayout.Height(230f));

            var pageSize = 10;
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(player.CacheIndices.Count / (float)pageSize));
            if (_fpmPageClip >= totalPages) _fpmPageClip = totalPages - 1;

            var start = _fpmPageClip * pageSize;
            var end = Mathf.Min(start + pageSize, player.CacheIndices.Count);

            for (var i = start; i < end; i++)
            {
                var ci = player.CacheIndices[i];
                if (ci < 0 || ci >= cachedAudio.Count)
                {
                    continue;
                }

                var entry = cachedAudio[ci];
                if (entry == null)
                {
                    continue;
                }

                var dur = entry.SampleRate > 0 && entry.AudioData != null
                    ? entry.AudioData.Length / (float)(entry.SampleRate * 2)
                    : 0f;
                var age = Time.time - entry.ReceivedAt;
                var ageStr = age < 60f ? $"{age:F0}s ago" : $"{age / 60f:F1}m ago";
                var label = $"  [{ci + 1:D3}]   {dur:F2}s   received {ageStr}";

                if (GUILayout.Button(label, _fpmClipIdx == i ? _gsListItemSel : _gsListItem))
                {
                    _fpmClipIdx = i;
                }
                GUILayout.Space(2f);
            }

            if (totalPages > 1)
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUI.enabled = _fpmPageClip > 0;
                if (GUILayout.Button("«", _gsBtn, GUILayout.Width(30f))) _fpmPageClip--;
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Page {_fpmPageClip + 1} / {totalPages}", _gsSmall);
                GUILayout.FlexibleSpace();
                GUI.enabled = _fpmPageClip < totalPages - 1;
                if (GUILayout.Button("»", _gsBtn, GUILayout.Width(30f))) _fpmPageClip++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            DrawHRule();
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← Back", _gsBtn, GUILayout.Height(28f), GUILayout.Width(80f)))
            {
                _fpmPage = 0;
            }
            GUILayout.FlexibleSpace();
            GUI.enabled = _fpmClipIdx >= 0 && _fpmClipIdx < player.CacheIndices.Count;
            if (GUILayout.Button("Next: Enemy →", _gsBtnPrimary, GUILayout.Height(28f), GUILayout.Width(130f)))
            {
                BuildFpmEnemyList();
                _fpmPage = 2;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // ── Modal Page 3: Enemy ─────────────────────────────────────────────────
        private void DrawFpmPageEnemy()
        {
            GUI.color = CTextDim;
            GUILayout.Label("Select enemy to play on:", _gsSmall);
            GUI.color = Color.white;
            GUILayout.Space(4f);

            _fpmScrollEnemy = GUILayout.BeginScrollView(_fpmScrollEnemy, GUILayout.Height(210f));

            if (GUILayout.Button("★  Nearest available  (auto-select)", _fpmEnemyIdx == -1 ? _gsListItemSel : _gsListItem))
            {
                _fpmEnemyIdx = -1;
            }
            GUILayout.Space(2f);

            if (_fpmEnemies == null || _fpmEnemies.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No enemies in scene.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                var pageSize = 10;
                var totalPages = Mathf.Max(1, Mathf.CeilToInt(_fpmEnemies.Count / (float)pageSize));
                if (_fpmPageEnemy >= totalPages) _fpmPageEnemy = totalPages - 1;

                var start = _fpmPageEnemy * pageSize;
                var end = Mathf.Min(start + pageSize, _fpmEnemies.Count);

                for (var i = start; i < end; i++)
                {
                    var e = _fpmEnemies[i];
                    var selected = _fpmEnemyIdx == i;
                    var playingTag = e.IsPlaying ? "  ▶ playing" : string.Empty;
                    GUI.color = e.IsPlaying ? CGreen : Color.white;
                    var label = $"  {e.Name}   {e.Distance:F1}m{playingTag}";
                    if (GUILayout.Button(label, selected ? _gsListItemSel : _gsListItem))
                    {
                        _fpmEnemyIdx = i;
                    }
                    GUI.color = Color.white;
                    GUILayout.Space(2f);
                }

                if (totalPages > 1)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUI.enabled = _fpmPageEnemy > 0;
                    if (GUILayout.Button("«", _gsBtn, GUILayout.Width(30f))) _fpmPageEnemy--;
                    GUI.enabled = true;
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"Page {_fpmPageEnemy + 1} / {totalPages}", _gsSmall);
                    GUILayout.FlexibleSpace();
                    GUI.enabled = _fpmPageEnemy < totalPages - 1;
                    if (GUILayout.Button("»", _gsBtn, GUILayout.Width(30f))) _fpmPageEnemy++;
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            DrawHRule();
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← Back", _gsBtn, GUILayout.Height(28f), GUILayout.Width(80f)))
            {
                _fpmPage = _fpmPlayerIdx == -1 ? 0 : 1;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Next: Filter →", _gsBtnPrimary, GUILayout.Height(28f), GUILayout.Width(110f)))
            {
                _fpmPage = 3;
            }
            GUILayout.EndHorizontal();
        }

        // ── Modal Page 4: Filter ────────────────────────────────────────────────
        private void DrawFpmPageFilter()
        {
            GUI.color = CTextDim;
            GUILayout.Label("Select audio effect:", _gsSmall);
            GUI.color = Color.white;
            GUILayout.Space(4f);

            _fpmScrollFilter = GUILayout.BeginScrollView(_fpmScrollFilter, GUILayout.Height(210f));

            // Filter selection list
            // -1 = Default (Random chance or None)
            if (GUILayout.Button("Default / Random", _fpmFilterIdx == -1 ? _gsListItemSel : _gsListItem))
            {
                _fpmFilterIdx = -1;
            }
            GUILayout.Space(2f);

            // Use the registry list from AudioFilters
            var count = AudioFilters.Count;
            var pageSize = 10;
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(count / (float)pageSize));
            if (_fpmPageFilter >= totalPages) _fpmPageFilter = totalPages - 1;

            var start = _fpmPageFilter * pageSize;
            var end = Mathf.Min(start + pageSize, count);

            for (var i = start; i < end; i++)
            {
                var label = AudioFilters.Registry[i].Key;
                if (GUILayout.Button($"Effect: {label}", _fpmFilterIdx == i ? _gsListItemSel : _gsListItem))
                {
                    _fpmFilterIdx = i;
                }
                GUILayout.Space(2f);
            }

            if (totalPages > 1)
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUI.enabled = _fpmPageFilter > 0;
                if (GUILayout.Button("«", _gsBtn, GUILayout.Width(30f))) _fpmPageFilter--;
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Page {_fpmPageFilter + 1} / {totalPages}", _gsSmall);
                GUILayout.FlexibleSpace();
                GUI.enabled = _fpmPageFilter < totalPages - 1;
                if (GUILayout.Button("»", _gsBtn, GUILayout.Width(30f))) _fpmPageFilter++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            DrawHRule();
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← Back", _gsBtn, GUILayout.Height(28f), GUILayout.Width(80f)))
            {
                _fpmPage = 2;
            }
            GUILayout.FlexibleSpace();
            GUI.color = CYellow;
            if (GUILayout.Button("▶  Play!", _gsBtnYellow, GUILayout.Height(28f), GUILayout.Width(100f)))
            {
                ExecuteForcePlay();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        // ── Execute ─────────────────────────────────────────────────────────────
        private void ExecuteForcePlay()
        {
            _fpmOpen = false;

            // Resolve entry
            string soundGuid = string.Empty;
            var localId = PhotonNetwork.LocalPlayer != null
                ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer)
                : string.Empty;

            if (_fpmPlayerIdx == -1)
            {
                var allPlayers = PhotonNetwork.PlayerList;
                if (allPlayers == null || allPlayers.Length == 0) return;

                var allActors = new HashSet<int>(allPlayers.Select(p => p.ActorNumber));
                // Random
                var eligibleSounds = soundReadinessMap
                    .Where(kv => allActors.All(a => kv.Value.Contains(a)))
                    .Select(kv => kv.Key)
                    .ToList();

                if (eligibleSounds.Count == 0)
                {
                    DLog($"HostAuthorityTick: no eligible sounds (map={soundReadinessMap.Count} entries, players={allActors.Count})");
                    return;
                }

                soundGuid = eligibleSounds[UnityEngine.Random.Range(0, eligibleSounds.Count)];
            }
            else if (_fpmPlayers != null && _fpmPlayerIdx >= 0 && _fpmPlayerIdx < _fpmPlayers.Count)
            {
                var p = _fpmPlayers[_fpmPlayerIdx];
                if (_fpmClipIdx >= 0 && _fpmClipIdx < p.CacheIndices.Count)
                {
                    var ci = p.CacheIndices[_fpmClipIdx];
                    if (ci >= 0 && ci < cachedAudio.Count)
                    {
                        soundGuid = cachedAudio[ci].SoundGuid;
                    }
                }
            }

            if (string.IsNullOrEmpty(soundGuid))
            {
                DLog($"ForcePlay: entry is null or empty {DebugContext()}");
                return;
            }

            // Resolve enemy
            GameObject enemy = null, target = null;
            if (_fpmEnemyIdx == -1)
            {
                var nearest = nearestPlaybackCandidatesHud
                    .Where(c => c != null && !c.IsPlaying)
                    .OrderBy(c => c.Distance)
                    .FirstOrDefault();
                if (nearest != null)
                {
                    enemy = nearest.Enemy;
                    target = nearest.Target;
                }
            }
            else if (_fpmEnemies != null && _fpmEnemyIdx >= 0 && _fpmEnemyIdx < _fpmEnemies.Count)
            {
                enemy = _fpmEnemies[_fpmEnemyIdx].Enemy;
                target = _fpmEnemies[_fpmEnemyIdx].Target;
            }

            if (enemy == null)
            {
                DLog($"ForcePlay: could not resolve enemy target {DebugContext()}");
                return;
            }

            var viewId = GetEnemyNetViewId(enemy);
            if (viewId < 0)
            {
                DLog($"ForcePlay: enemy has no ViewID {DebugContext()}");
                return;
            }

            DLog($"ForcePlay: soundGuid={soundGuid} enemy={enemy.name} {DebugContext()}");

            // Use Host Authority Tick logic to synchronize playback across all clients
            // If filter is -1 (Default/Random), pass -2 to trigger the host-side random selection logic
            HostAuthorityTick(soundGuid, [viewId], _fpmFilterIdx == -1 ? -2 : _fpmFilterIdx);
        }

        /* PlayReceivedAudioOnTarget removed - logic now handled via HostAuthorityTick -> SyncPlayCommandPacket */
    }
}
