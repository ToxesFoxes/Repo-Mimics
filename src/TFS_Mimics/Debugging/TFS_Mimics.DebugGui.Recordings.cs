using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Tab: Recordings (manager for cached voices + custom audio) ───────────
        private sealed class RecordingRow
        {
            public CachedAudioEntry Voice;    // exactly one of Voice / Custom is set
            public CustomAudioEntry Custom;
            public string Owner;
            public bool IsMine;
            public float Duration;
            public float ReceivedAt;
            public object Key => (object)Voice ?? Custom;
            public bool Pinned => Voice != null && Voice.Pinned;
            public int PlayCount => Voice?.PlayCount ?? Custom?.PlayCount ?? 0;
            public float[] Waveform => Voice?.Waveform ?? Custom?.Waveform;
        }

        private static readonly string[] RecordingFilterNames = { "All", "Mine", "Others", "Custom", "Pinned" };
        private static readonly string[] RecordingSortNames = { "newest", "most played", "longest" };
        private const int RecordingsPerPage = 25;
        private const int WaveformBuckets = 48;
        private const int WaveformsPerRebuild = 6;
        private const float ConfirmWindowSeconds = 3f;

        private int _recFilter;
        private int _recSort;
        private int _recPage;
        private readonly List<RecordingRow> _recRows = new List<RecordingRow>();
        private readonly int[] _recFilterCounts = new int[5];

        private object _pendingDeleteKey;
        private float _pendingDeleteUntil;
        private float _pendingClearUntil;

        private AudioSource _previewSource;
        private AudioClip _previewOwnedClip;   // clip we created for a voice preview (custom clips are not ours)
        private object _previewKey;

        private void RebuildRecordingsCache()
        {
            var localId = PhotonNetwork.LocalPlayer != null ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer) : string.Empty;
            var all = new List<RecordingRow>(cachedAudio.Count + _customAudioClips.Count);

            foreach (var e in cachedAudio)
            {
                if (e?.AudioData == null) continue;
                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                all.Add(new RecordingRow
                {
                    Voice = e,
                    Owner = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid,
                    IsMine = string.Equals(pid, localId, StringComparison.OrdinalIgnoreCase),
                    Duration = e.SampleRate > 0 ? e.AudioData.Length / (float)(e.SampleRate * 2) : 0f,
                    ReceivedAt = e.ReceivedAt
                });
            }

            foreach (var c in _customAudioClips)
            {
                if (c?.Clip == null) continue;
                all.Add(new RecordingRow
                {
                    Custom = c,
                    Owner = c.SourceMod != null ? $"{c.FileName} [{c.SourceMod}]" : c.FileName,
                    Duration = c.Clip.length,
                    ReceivedAt = float.MinValue
                });
            }

            Array.Clear(_recFilterCounts, 0, _recFilterCounts.Length);
            foreach (var r in all)
            {
                for (var f = 0; f < _recFilterCounts.Length; f++)
                {
                    if (MatchesRecordingFilter(r, f)) _recFilterCounts[f]++;
                }
            }

            IEnumerable<RecordingRow> rows = all.Where(r => MatchesRecordingFilter(r, _recFilter));
            switch (_recSort)
            {
                case 1: rows = rows.OrderByDescending(r => r.PlayCount).ThenByDescending(r => r.ReceivedAt); break;
                case 2: rows = rows.OrderByDescending(r => r.Duration); break;
                default: rows = rows.OrderByDescending(r => r.ReceivedAt); break;
            }

            _recRows.Clear();
            _recRows.AddRange(rows);

            // Build a few waveforms per rebuild (visible page first) to avoid frame spikes.
            var built = 0;
            var start = _recPage * RecordingsPerPage;
            for (var i = start; i < _recRows.Count && built < WaveformsPerRebuild; i++)
            {
                if (_recRows[i].Waveform == null)
                {
                    BuildWaveform(_recRows[i]);
                    built++;
                }
            }
        }

        private static bool MatchesRecordingFilter(RecordingRow r, int filter)
        {
            switch (filter)
            {
                case 1: return r.Voice != null && r.IsMine;
                case 2: return r.Voice != null && !r.IsMine;
                case 3: return r.Custom != null;
                case 4: return r.Pinned;
                default: return true;
            }
        }

        private static void BuildWaveform(RecordingRow row)
        {
            var peaks = new float[WaveformBuckets];
            if (row.Voice != null)
            {
                var data = row.Voice.AudioData;
                var samples = data.Length / 2;
                for (var b = 0; b < WaveformBuckets && samples > 0; b++)
                {
                    var from = (int)((long)samples * b / WaveformBuckets);
                    var to = (int)((long)samples * (b + 1) / WaveformBuckets);
                    var max = 0;
                    for (var i = from; i < to; i += 4)
                    {
                        var s = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                        var a = s < 0 ? -s : s;
                        if (a > max) max = a;
                    }
                    peaks[b] = max / 32768f;
                }
                row.Voice.Waveform = NormalizePeaks(peaks);
            }
            else if (row.Custom?.Clip != null)
            {
                var clip = row.Custom.Clip;
                var total = clip.samples * clip.channels;
                if (total > 0 && total <= 48000 * 2 * 120)
                {
                    var buf = new float[total];
                    if (clip.GetData(buf, 0))
                    {
                        for (var b = 0; b < WaveformBuckets; b++)
                        {
                            var from = (int)((long)total * b / WaveformBuckets);
                            var to = (int)((long)total * (b + 1) / WaveformBuckets);
                            var max = 0f;
                            for (var i = from; i < to; i += 4)
                            {
                                var a = Mathf.Abs(buf[i]);
                                if (a > max) max = a;
                            }
                            peaks[b] = max;
                        }
                    }
                }
                row.Custom.Waveform = NormalizePeaks(peaks);
            }
        }

        private static float[] NormalizePeaks(float[] peaks)
        {
            var max = peaks.Max();
            if (max > 0f)
            {
                for (var i = 0; i < peaks.Length; i++) peaks[i] /= max;
            }
            return peaks;
        }

        private void DrawRecordingsTab(float scrollH)
        {
            // ── Filters + sort ────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            for (var f = 0; f < RecordingFilterNames.Length; f++)
            {
                var style = _recFilter == f ? _gsTabBtnActive : _gsTabBtn;
                if (GUILayout.Button($"{RecordingFilterNames[f]} {_recFilterCounts[f]}", style, GUILayout.Height(22f), GUILayout.ExpandWidth(false)))
                {
                    _recFilter = f;
                    _recPage = 0;
                    _tabCacheNextRebuild = 0f;
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"sort: {RecordingSortNames[_recSort]}", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false)))
            {
                _recSort = (_recSort + 1) % RecordingSortNames.Length;
                _tabCacheNextRebuild = 0f;
            }
            GUILayout.EndHorizontal();

            // ── Toolbar ───────────────────────────────────────────────────────
            GUILayout.Space(3f);
            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            var persist = Plugin.configPersistAudioCache != null && Plugin.configPersistAudioCache.Value;
            GUILayout.Label(persist ? "saved to disk" : "session only (persistence off)", _gsSmall, GUILayout.ExpandWidth(false));
            if (incomingAudioTransmissions.Count > 0)
            {
                GUI.color = CYellow;
                GUILayout.Label($"● {incomingAudioTransmissions.Count} incoming", _gsSmall, GUILayout.ExpandWidth(false));
            }
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↺ Reload custom", _gsBtn, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
            {
                ReloadCustomAudio();
            }
            GUILayout.Space(4f);
            var confirmClear = Time.unscaledTime < _pendingClearUntil;
            if (GUILayout.Button(confirmClear ? "Sure? delete unpinned" : "Delete unpinned", _gsBtnDanger, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
            {
                if (confirmClear)
                {
                    StopRecordingPreview();
                    foreach (var e in cachedAudio.Where(e => e != null && !e.Pinned).ToList())
                        DeleteCachedEntry(e);
                    _pendingClearUntil = 0f;
                    _tabCacheNextRebuild = 0f;
                }
                else
                {
                    _pendingClearUntil = Time.unscaledTime + ConfirmWindowSeconds;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            // ── Pagination ────────────────────────────────────────────────────
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(_recRows.Count / (float)RecordingsPerPage));
            _recPage = Mathf.Clamp(_recPage, 0, totalPages - 1);
            var listH = scrollH - 56f;
            if (totalPages > 1)
            {
                listH -= 24f;
                GUILayout.BeginHorizontal();
                GUI.enabled = _recPage > 0;
                if (GUILayout.Button("◀", _gsIconBtn, GUILayout.Width(26f), GUILayout.Height(20f))) { _recPage--; _tabCacheNextRebuild = 0f; }
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"page {_recPage + 1} / {totalPages}", _gsSmall);
                GUILayout.FlexibleSpace();
                GUI.enabled = _recPage < totalPages - 1;
                if (GUILayout.Button("▶", _gsIconBtn, GUILayout.Width(26f), GUILayout.Height(20f))) { _recPage++; _tabCacheNextRebuild = 0f; }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // ── List ──────────────────────────────────────────────────────────
            if (_previewKey != null && (_previewSource == null || !_previewSource.isPlaying))
            {
                _previewKey = null;
            }

            _scrollRecordings = GUILayout.BeginScrollView(_scrollRecordings, GUILayout.Height(Mathf.Max(80f, listH)));
            if (_recRows.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label(_recFilter == 3
                    ? "  Drop .mp3 or .wav files into BepInEx/plugins/ToxesFoxes-Mimics/custom-audio/"
                    : "  No recordings here yet. Talk near your teammates and they'll show up.", _gsLabel);
                GUI.color = Color.white;
            }

            var start = _recPage * RecordingsPerPage;
            var end = Mathf.Min(start + RecordingsPerPage, _recRows.Count);
            for (var i = start; i < end; i++)
            {
                DrawRecordingRow(_recRows[i], i % 2 == 1);
            }
            GUILayout.EndScrollView();

            GUI.color = CTextDim;
            GUILayout.Label("▶ listen locally · ♫ play on nearest mob (everyone hears) · ★ keep from auto-cleanup · ✕ delete", _gsSmall);
            GUI.color = Color.white;
        }

        private void DrawRecordingRow(RecordingRow row, bool alt)
        {
            GUILayout.BeginHorizontal(alt ? _gsRowAlt : _gsRow);

            // Preview
            var previewing = _previewKey != null && ReferenceEquals(_previewKey, row.Key);
            GUI.color = previewing ? CGreen : Color.white;
            if (GUILayout.Button(previewing ? "■" : "▶", _gsIconBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
            {
                if (previewing) StopRecordingPreview();
                else StartRecordingPreview(row);
            }
            GUI.color = Color.white;
            GUILayout.Space(4f);

            // Owner
            GUI.color = row.Custom != null ? CCustom : (row.IsMine ? CYellow : CText);
            GUILayout.Label(FitHudText(row.Owner, 14), _gsLabel, GUILayout.Width(96f));
            GUI.color = Color.white;

            // Waveform
            var wr = GUILayoutUtility.GetRect(100f, 18f, GUILayout.Width(100f), GUILayout.Height(18f));
            DrawWaveform(wr, row.Waveform, row.Custom != null ? CCustom : CAccent,
                previewing && _previewSource != null && _previewSource.clip != null && _previewSource.clip.length > 0f
                    ? _previewSource.time / _previewSource.clip.length : -1f);
            GUILayout.Space(6f);

            GUI.color = CText;
            GUILayout.Label($"{row.Duration:F1}s", _gsSmall, GUILayout.Width(38f));
            GUI.color = row.PlayCount > 0 ? CTextDim : CAccent;
            GUILayout.Label(row.PlayCount > 0 ? $"×{row.PlayCount}" : "new", _gsSmall, GUILayout.Width(30f));
            GUI.color = CTextDim;
            if (row.Voice != null)
            {
                var age = Time.time - row.ReceivedAt;
                GUILayout.Label(age < 60f ? $"{age:F0}s ago" : age < 3600f ? $"{age / 60f:F0}m ago" : $"{age / 3600f:F1}h ago", _gsSmall, GUILayout.Width(52f));
            }
            else
            {
                GUILayout.Label(row.Custom.SourceMod != null ? "mod" : "folder", _gsSmall, GUILayout.Width(52f));
            }
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();

            // Pin (voices only)
            if (row.Voice != null)
            {
                GUI.color = row.Pinned ? CYellow : CTextDim;
                if (GUILayout.Button(row.Pinned ? "★" : "☆", _gsIconBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    SetEntryPinned(row.Voice, !row.Pinned);
                    _tabCacheNextRebuild = 0f;
                }
            }
            else
            {
                GUILayout.Space(22f + 4f);
            }

            // Play on nearest mob
            GUI.color = Color.white;
            GUI.enabled = SemiFunc.RunIsLevel();
            if (GUILayout.Button("♫", _gsIconBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
            {
                if (row.Voice != null) PlayCacheEntryOnNearest(row.Voice);
                else PlayCustomAudioEntry(row.Custom);
            }
            GUI.enabled = true;

            // Delete (voices only), two clicks to confirm
            if (row.Voice != null)
            {
                var confirming = ReferenceEquals(_pendingDeleteKey, row.Key) && Time.unscaledTime < _pendingDeleteUntil;
                if (GUILayout.Button(confirming ? "sure?" : "✕", confirming ? _gsBtnDanger : _gsIconBtn,
                        GUILayout.Width(confirming ? 44f : 22f), GUILayout.Height(20f)))
                {
                    if (confirming)
                    {
                        if (previewing) StopRecordingPreview();
                        DeleteCachedEntry(row.Voice);
                        _pendingDeleteKey = null;
                        _tabCacheNextRebuild = 0f;
                    }
                    else
                    {
                        _pendingDeleteKey = row.Key;
                        _pendingDeleteUntil = Time.unscaledTime + ConfirmWindowSeconds;
                    }
                }
            }
            else
            {
                GUILayout.Space(22f + 4f);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawWaveform(Rect r, float[] peaks, Color color, float progress)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.03f);
            GUI.DrawTexture(r, _txWhite);

            if (peaks == null)
            {
                GUI.color = CTextDim;
                GUI.DrawTexture(new Rect(r.x, r.center.y, r.width, 1f), _txWhite);
                GUI.color = Color.white;
                return;
            }

            var barW = r.width / peaks.Length;
            for (var i = 0; i < peaks.Length; i++)
            {
                var h = Mathf.Max(1f, peaks[i] * (r.height - 2f));
                var played = progress >= 0f && (i + 0.5f) / peaks.Length <= progress;
                GUI.color = played ? CGreen : color;
                GUI.DrawTexture(new Rect(r.x + i * barW, r.center.y - h * 0.5f, Mathf.Max(1f, barW - 1f), h), _txWhite);
            }
            GUI.color = Color.white;
        }

        private void StartRecordingPreview(RecordingRow row)
        {
            StopRecordingPreview();

            if (_previewSource == null)
            {
                var go = new GameObject("MimicsPreview");
                go.transform.SetParent(transform, false);
                _previewSource = go.AddComponent<AudioSource>();
                _previewSource.playOnAwake = false;
                _previewSource.spatialBlend = 0f;
                _previewSource.loop = false;
                _previewSource.outputAudioMixerGroup = null;
            }

            AudioClip clip;
            if (row.Voice != null)
            {
                var samples = ConvertByteArrayToFloatArray(row.Voice.AudioData, -1, row.Voice.SampleRate);
                clip = AudioClip.Create("MimicsPreviewClip", samples.Length, 1, row.Voice.SampleRate, false);
                clip.SetData(samples, 0);
                _previewOwnedClip = clip;
            }
            else
            {
                clip = row.Custom.Clip;
            }

            _previewSource.clip = clip;
            _previewSource.volume = Mathf.Clamp01((Plugin.configVoiceVolume?.Value ?? 30) / 100f);
            _previewSource.Play();
            _previewKey = row.Key;
        }

        private void StopRecordingPreview()
        {
            if (_previewSource != null)
            {
                _previewSource.Stop();
                _previewSource.clip = null;
            }

            if (_previewOwnedClip != null)
            {
                Destroy(_previewOwnedClip);
                _previewOwnedClip = null;
            }

            _previewKey = null;
        }
    }
}
