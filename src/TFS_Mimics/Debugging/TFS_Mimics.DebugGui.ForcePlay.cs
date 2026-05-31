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
            _fpmHearYourself = false;
            _fpmScrollPlayer = Vector2.zero;
            _fpmScrollClip = Vector2.zero;
            _fpmScrollEnemy = Vector2.zero;
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
            }
        }

        private void DrawFpmPageIndicator()
        {
            var pages = new[] { "1. Player", "2. Clip", "3. Enemy" };
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (var i = 0; i < pages.Length; i++)
            {
                if (i == 0 && _fpmPlayerIdx == -1 && _fpmPage > 0)
                {
                    // Skip clip page for Random
                    if (i == 1)
                    {
                        continue;
                    }
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
                for (var i = 0; i < _fpmPlayers.Count; i++)
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
                    }
                    GUI.color = prevColor;
                    GUILayout.Space(2f);
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

            for (var i = 0; i < player.CacheIndices.Count; i++)
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
                var label = $"  [{i + 1:D2}]   {dur:F2}s   received {ageStr}";

                if (GUILayout.Button(label, _fpmClipIdx == i ? _gsListItemSel : _gsListItem))
                {
                    _fpmClipIdx = i;
                }
                GUILayout.Space(2f);
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
                for (var i = 0; i < _fpmEnemies.Count; i++)
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
            CachedAudioEntry entry = null;
            var localId = PhotonNetwork.LocalPlayer != null
                ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer)
                : string.Empty;

            if (_fpmPlayerIdx == -1)
            {
                // Random
                var pool = cachedAudio.Where(e => e != null && e.AudioData != null && e.AudioData.Length > 0).ToList();
                if (!_fpmHearYourself && !string.IsNullOrEmpty(localId))
                {
                    pool = pool.Where(e => !string.Equals(e.SourcePlayerId, localId, System.StringComparison.OrdinalIgnoreCase)).ToList();
                }

                if (pool.Count == 0)
                {
                    DLog($"ForcePlay: no eligible clips (hearYourself={_fpmHearYourself}) {DebugContext()}");
                    return;
                }

                entry = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            else if (_fpmPlayers != null && _fpmPlayerIdx >= 0 && _fpmPlayerIdx < _fpmPlayers.Count)
            {
                var p = _fpmPlayers[_fpmPlayerIdx];
                if (_fpmClipIdx >= 0 && _fpmClipIdx < p.CacheIndices.Count)
                {
                    var ci = p.CacheIndices[_fpmClipIdx];
                    if (ci >= 0 && ci < cachedAudio.Count)
                    {
                        entry = cachedAudio[ci];
                    }
                }
            }

            if (entry == null || entry.AudioData == null || entry.AudioData.Length == 0)
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

            DLog($"ForcePlay: entry={entry.SourceActor}:{entry.SourceName} enemy={enemy.name} bytes={entry.AudioData.Length} {DebugContext()}");
            PlayReceivedAudioOnTarget(entry, enemy, target);
        }

        private void PlayReceivedAudioOnTarget(CachedAudioEntry entry, GameObject enemy, GameObject target)
        {
            var applyFilter = (Plugin.configPlaybackVoiceFilterEnabled == null || Plugin.configPlaybackVoiceFilterEnabled.Value)
                && UnityEngine.Random.value > 0.9f;

            var samples = ConvertByteArrayToFloatArray(entry.AudioData, applyFilter, entry.SampleRate);
            var clip = AudioClip.Create("ForcePlayClip", samples.Length, 1, entry.SampleRate, false);
            clip.SetData(samples, 0);

            var position = GetEnemyDistancePosition(enemy, target);
            var source = GetOrCreateReusableEnemyAudioSource(enemy, target, position);
            if (source == null)
            {
                DLog($"ForcePlay: failed to get audio source for enemy={enemy.name} {DebugContext()}");
                return;
            }

            source.clip = clip;
            source.volume = GetVolumeForPlayer(entry.SourcePlayerId);
            source.mute = false;
            source.pitch = 1f;
            source.loop = false;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0.5f;
            source.minDistance = 1f;
            source.maxDistance = 20f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.outputAudioMixerGroup = null;
            source.Play();

            var playbackEndsAt = Time.time + clip.length + 0.1f;
            var targetKey = GetPlaybackTargetKey(enemy, target);
            if (targetKey != 0)
            {
                playbackBusyUntilByTargetKey[targetKey] = playbackEndsAt;
                playbackStartedAtByTargetKey[targetKey] = Time.time;
                playbackClipLengthByTargetKey[targetKey] = clip.length;
            }

            currentPlaybackEnemyName = NormalizeEnemyName(enemy.name);
            currentPlaybackSourcePlayerId = !string.IsNullOrWhiteSpace(entry.SourcePlayerId)
                ? entry.SourcePlayerId
                : (entry.SourceActor >= 0 ? $"actor_{entry.SourceActor}" : "unknown");
            hudTrackedEnemy = enemy;
            hudTrackedTarget = target;
            hudLastSelectedEnemyPos = position;
            hudHasSelectedEnemyPos = true;
            currentPlaybackEndsAt = playbackEndsAt;

            DLog($"ForcePlay: playing on enemy={enemy.name} source={entry.SourceActor}:{entry.SourceName} clipLen={clip.length:F2}s {DebugContext()}");
            StartCoroutine(ResetReusableAudioSourceAfterDelay(source, clip.length + 0.1f));
        }
    }
}
