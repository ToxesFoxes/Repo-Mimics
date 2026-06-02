using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Voice Log ────────────────────────────────────────────────────────────
        internal void PushVoiceLog(bool isIncoming, string txId, string playerId, string playerName, int bytes, bool isComplete, int chunksDone = 0, int chunksTotal = 0, bool isFailed = false)
        {
            var existing = _voiceLog.FindIndex(e => e.TransmissionId == txId);
            if (existing >= 0)
            {
                var e = _voiceLog[existing];
                if (bytes > 0) e.Bytes = bytes;
                e.IsComplete = isComplete;
                e.IsFailed = isFailed;
                e.UpdatedAt = Time.time;
                if (chunksDone > 0) e.ChunksDone = chunksDone;
                if (chunksTotal > 0) e.ChunksTotal = chunksTotal;
                return;
            }

            if (_voiceLog.Count >= VoiceLogMaxEntries)
                _voiceLog.RemoveAt(0);

            _voiceLog.Add(new VoiceLogEntry
            {
                IsIncoming = isIncoming,
                TransmissionId = txId,
                PlayerId = playerId,
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName,
                Bytes = bytes,
                IsComplete = isComplete,
                ReceivedAt = Time.time,
                UpdatedAt = Time.time,
                ChunksDone = chunksDone,
                ChunksTotal = chunksTotal
            });
        }

        private void DrawVoiceLogTab(float scrollH)
        {
            var inEntries = _cachedVoiceLogIn;
            var outEntries = _cachedVoiceLogOut;
            var inProgress = _cachedVoiceLogInProgress;

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Total: {_voiceLog.Count}", _gsSmall, GUILayout.ExpandWidth(false));
            if (inProgress > 0)
            {
                GUILayout.Space(6f);
                GUI.color = CYellow;
                GUILayout.Label($"● {inProgress} in progress", _gsSmall, GUILayout.ExpandWidth(false));
            }
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", _gsBtn, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
                _voiceLog.Clear();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            var halfH = (scrollH - 24f) * 0.5f;

            // ── Incoming section ──────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label($"▼ INCOMING  {inEntries.Count}", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            _scrollVoiceLog = GUILayout.BeginScrollView(_scrollVoiceLog, GUILayout.Height(halfH));
            if (inEntries.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No incoming transmissions recorded yet.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                for (var i = inEntries.Count - 1; i >= 0; i--)
                    DrawVoiceLogRow(inEntries[i]);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);

            // ── Outgoing section ──────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUI.color = CYellow;
            GUILayout.Label($"▲ OUTGOING  {outEntries.Count}", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            _scrollVoiceLogOut = GUILayout.BeginScrollView(_scrollVoiceLogOut, GUILayout.Height(halfH));
            if (outEntries.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No outgoing transmissions recorded yet.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                for (var i = outEntries.Count - 1; i >= 0; i--)
                    DrawVoiceLogRow(outEntries[i]);
            }
            GUILayout.EndScrollView();
        }

        private void DrawVoiceLogRow(VoiceLogEntry e)
        {
            var age = Time.time - e.ReceivedAt;
            var ageStr = age < 60f ? $"{age:F0}s" : $"{age / 60f:F1}m";
            var kb = e.Bytes / 1024f;
            var sizeStr = e.Bytes == 0 ? "—" : kb >= 1f ? $"{kb:F1}kb" : $"{e.Bytes}b";

            GUILayout.BeginHorizontal();

            // Complete / in-progress / failed indicator
            if (e.IsFailed)
            {
                GUI.color = CRed;
                GUILayout.Label("✗", _gsSmall, GUILayout.Width(14f));
            }
            else if (e.IsComplete)
            {
                GUI.color = CGreen;
                GUILayout.Label("✓", _gsSmall, GUILayout.Width(14f));
            }
            else
            {
                GUI.color = CYellow;
                GUILayout.Label("…", _gsSmall, GUILayout.Width(14f));
            }
            GUI.color = Color.white;

            // Player name
            GUILayout.Label(FitHudText(e.PlayerName, 16), _gsSmall, GUILayout.Width(110f));

            // TX id (short)
            GUI.color = CTextDim;
            GUILayout.Label(FitHudText(e.TransmissionId, 10), _gsSmall, GUILayout.Width(76f));
            GUI.color = Color.white;

            // Progress bar or size
            if (!e.IsComplete && e.ChunksTotal > 0)
            {
                DrawChunkProgressBar(e.ChunksDone, e.ChunksTotal, 80f);
                GUI.color = CYellow;
                GUILayout.Label($"{e.ChunksDone}/{e.ChunksTotal}", _gsSmall, GUILayout.Width(38f));
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = CTextDim;
                GUILayout.Label(sizeStr, _gsSmall, GUILayout.Width(44f));
                GUILayout.Label(ageStr + " ago", _gsSmall, GUILayout.Width(54f));
                GUI.color = Color.white;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawChunkProgressBar(int done, int total, float width)
        {
            var rect = GUILayoutUtility.GetRect(width, 14f, GUILayout.Width(width));
            var filled = total > 0 ? (float)done / total : 0f;

            // Background
            var prevColor = GUI.color;
            GUI.color = new Color(0.15f, 0.18f, 0.22f, 1f);
            GUI.DrawTexture(rect, _txWhite);

            // Fill
            var fillRect = new Rect(rect.x, rect.y, rect.width * filled, rect.height);
            GUI.color = done >= total ? CGreen : CYellow;
            GUI.DrawTexture(fillRect, _txWhite);

            GUI.color = prevColor;
        }

        private void RebuildVoiceLogCache()
        {
            _cachedVoiceLogIn.Clear();
            _cachedVoiceLogOut.Clear();
            _cachedVoiceLogInProgress = 0;
            foreach (var e in _voiceLog)
            {
                if (e.IsIncoming) _cachedVoiceLogIn.Add(e);
                else _cachedVoiceLogOut.Add(e);
                if (!e.IsComplete) _cachedVoiceLogInProgress++;
            }
        }
    }
}
