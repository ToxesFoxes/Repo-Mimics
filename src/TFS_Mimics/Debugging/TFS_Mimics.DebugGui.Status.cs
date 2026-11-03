using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Status strip: mic meter (left) + playbacks (right) ───────────────────
        // Concurrent playbacks are rare (very low delays), so the first one is shown in full
        // and the rest as compact rows, then "+N more".
        private const int MaxExtraPlaybackRows = 2;
        private const float MeterMinDb = -60f;
        private readonly List<ActivePlayback> _activePlaybackScratch = new List<ActivePlayback>();

        private int CountActivePlaybacks()
        {
            var now = Time.time;
            var count = 0;
            foreach (var p in activePlaybacks.Values)
            {
                if (p != null && now <= p.StartedAt + p.Length + 0.1f) count++;
            }
            return count;
        }

        private void DrawStatusStrip()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(_gsPanelBox, GUILayout.Width((_debugWindowRect.width - 28f) * 0.5f));
            DrawMicMeter();
            GUILayout.EndVertical();

            GUILayout.Space(6f);

            GUILayout.BeginVertical(_gsPanelBox);
            DrawPlaybackStatus();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawMicMeter()
        {
            var threshold = GetVadThreshold();
            var stale = Time.time - _micLevelAt > 0.5f;
            var muted = !stale && IsMicrophoneMuted();

            GUILayout.BeginHorizontal();
            GUILayout.Label("MIC", _gsH1, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            if (stale)
            {
                GUI.color = CTextDim;
                GUILayout.Label("no input", _gsSmall, GUILayout.ExpandWidth(false));
            }
            else if (muted)
            {
                GUI.color = CRed;
                GUILayout.Label("✕ muted", _gsSmall, GUILayout.ExpandWidth(false));
            }
            else if (capturingSpeech)
            {
                var recDur = sampleRate > 0 ? bufferPosition / (float)sampleRate : 0f;
                GUI.color = CRed;
                GUILayout.Label($"● REC  {recDur:F1}s", _gsSmall, GUILayout.ExpandWidth(false));
            }
            else
            {
                GUI.color = isRecording ? CGreen : CTextDim;
                GUILayout.Label(isRecording ? "listening" : "waiting", _gsSmall, GUILayout.ExpandWidth(false));
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            // Bar on a dB scale: speech sits around -40..-25 dBFS on the raw mic stream.
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(12f));
            GUI.color = CBgTrack;
            GUI.DrawTexture(r, _txWhite);

            if (!stale)
            {
                var level = MeterT(_micLevel);
                var peak = MeterT(_micPeakHold);
                var above = _micLevel > threshold;
                GUI.color = muted ? CTextDim : (above ? CGreen : CAccentDim);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * level, r.height), _txWhite);
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(r.x + r.width * peak - 1f, r.y, 2f, r.height), _txWhite);
            }

            GUI.color = CTextDim;
            GUI.DrawTexture(new Rect(r.x + r.width * MeterT(vadNoiseFloor) - 1f, r.y - 2f, 2f, r.height + 4f), _txWhite);
            GUI.color = CYellow;
            GUI.DrawTexture(new Rect(r.x + r.width * MeterT(threshold) - 1f, r.y - 3f, 2f, r.height + 6f), _txWhite);
            GUI.color = Color.white;

            GUILayout.Space(2f);
            GUILayout.Label($"noise {vadNoiseFloor:F4} · threshold {threshold:F4} · sens {Plugin.configVadSensitivity?.Value ?? 85}", _gsSmall);
        }

        private static float MeterT(float rms)
        {
            if (rms <= 0.000001f) return 0f;
            var db = 20f * Mathf.Log10(rms);
            return Mathf.Clamp01((db - MeterMinDb) / -MeterMinDb);
        }

        private void DrawPlaybackStatus()
        {
            var now = Time.time;
            _activePlaybackScratch.Clear();
            foreach (var p in activePlaybacks.Values)
            {
                if (p != null && now <= p.StartedAt + p.Length + 0.1f) _activePlaybackScratch.Add(p);
            }
            var playing = _activePlaybackScratch.OrderByDescending(p => p.StartedAt).ToList();

            GUILayout.BeginHorizontal();
            if (playing.Count == 0)
            {
                GUILayout.Label("IDLE", _gsH1, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                if (_hostAuthorityLoopRunning && _nextTickAt > 0f && SemiFunc.RunIsLevel())
                {
                    var remaining = Mathf.Max(0f, _nextTickAt - now);
                    GUI.color = remaining < 3f ? CYellow : CTextDim;
                    GUILayout.Label($"next in {remaining:F1}s", _gsSmall, GUILayout.ExpandWidth(false));
                    GUI.color = Color.white;
                }
                GUILayout.EndHorizontal();

                var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(12f));
                GUI.color = CBgTrack;
                GUI.DrawTexture(r, _txWhite);
                GUI.color = Color.white;
                GUILayout.Space(2f);
                GUILayout.Label(SemiFunc.RunIsLevel()
                    ? (PhotonNetwork.IsMasterClient ? "host picks a clip and an enemy" : "waiting for the host")
                    : "playback only happens in levels", _gsSmall);
                return;
            }

            var first = playing[0];
            GUI.color = CGreen;
            GUILayout.Label($"▶ {FitHudText(first.EnemyName, 18)}", _gsH1, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.Label(FitHudText(first.Source, 18), _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            var elapsed = Mathf.Clamp(now - first.StartedAt, 0f, first.Length);
            DrawProgressBar(first.Length > 0f ? elapsed / first.Length : 0f, $"{elapsed:F1}s / {first.Length:F1}s", CGreen);
            GUILayout.Space(2f);

            if (playing.Count == 1)
            {
                GUILayout.Label(_hostAuthorityLoopRunning && _nextTickAt > now
                    ? $"next in {_nextTickAt - now:F1}s" : " ", _gsSmall);
                return;
            }

            for (var i = 1; i < playing.Count && i <= MaxExtraPlaybackRows; i++)
            {
                var p = playing[i];
                var t = p.Length > 0f ? Mathf.Clamp01((now - p.StartedAt) / p.Length) : 0f;
                GUILayout.BeginHorizontal();
                GUI.color = CGreen;
                GUILayout.Label("▶", _gsSmall, GUILayout.Width(12f));
                GUI.color = CText;
                GUILayout.Label(FitHudText(p.EnemyName, 14), _gsSmall, GUILayout.Width(88f));
                GUI.color = CTextDim;
                GUILayout.Label(FitHudText(p.Source, 12), _gsSmall, GUILayout.Width(74f));
                var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(6f));
                r.y += 5f;
                GUI.color = CBgTrack;
                GUI.DrawTexture(r, _txWhite);
                GUI.color = CGreen;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * t, r.height), _txWhite);
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }

            if (playing.Count - 1 > MaxExtraPlaybackRows)
            {
                GUILayout.Label($"+{playing.Count - 1 - MaxExtraPlaybackRows} more", _gsSmall);
            }
        }
    }
}
