using System.Linq;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Voice Log ────────────────────────────────────────────────────────────
        internal void PushVoiceLog(bool isIncoming, string txId, string playerId, string playerName, int bytes, bool isComplete)
        {
            if (_voiceLog.Count >= VoiceLogMaxEntries)
            {
                _voiceLog.RemoveAt(0);
            }

            _voiceLog.Add(new VoiceLogEntry
            {
                IsIncoming = isIncoming,
                TransmissionId = txId,
                PlayerId = playerId,
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName,
                Bytes = bytes,
                IsComplete = isComplete,
                ReceivedAt = Time.time
            });
        }

        private void DrawVoiceLogTab(float scrollH)
        {
            var inCount = _voiceLog.Count(e => e.IsIncoming);
            var outCount = _voiceLog.Count - inCount;

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Total: {_voiceLog.Count}  ", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = CAccent;
            GUILayout.Label($"▼ IN {inCount}", _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.Space(8f);
            GUI.color = CYellow;
            GUILayout.Label($"▲ OUT {outCount}", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", _gsBtn, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
            {
                _voiceLog.Clear();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            _scrollVoiceLog = GUILayout.BeginScrollView(_scrollVoiceLog, GUILayout.Height(scrollH));

            if (_voiceLog.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No voice transmissions recorded yet.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                // Draw newest-first
                for (var i = _voiceLog.Count - 1; i >= 0; i--)
                {
                    DrawVoiceLogRow(_voiceLog[i]);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawVoiceLogRow(VoiceLogEntry e)
        {
            var age = Time.time - e.ReceivedAt;
            var ageStr = age < 60f ? $"{age:F0}s" : $"{age / 60f:F1}m";
            var kb = e.Bytes / 1024f;
            var sizeStr = kb >= 1f ? $"{kb:F1}kb" : $"{e.Bytes}b";

            GUILayout.BeginHorizontal();

            // Direction badge
            GUI.color = e.IsIncoming ? CAccent : CYellow;
            GUILayout.Label(e.IsIncoming ? "▼" : "▲", _gsSmall, GUILayout.Width(14f));

            // Complete/incomplete
            GUI.color = e.IsComplete ? CGreen : CRed;
            GUILayout.Label(e.IsComplete ? "✓" : "✗", _gsSmall, GUILayout.Width(14f));
            GUI.color = Color.white;

            // Player name
            GUILayout.Label(FitHudText(e.PlayerName, 16), _gsSmall, GUILayout.Width(110f));

            // TX id (short)
            GUI.color = CTextDim;
            GUILayout.Label(FitHudText(e.TransmissionId, 10), _gsSmall, GUILayout.Width(80f));
            GUI.color = Color.white;

            // Size
            GUI.color = CTextDim;
            GUILayout.Label(sizeStr, _gsSmall, GUILayout.Width(44f));

            // Age
            GUILayout.Label(ageStr + " ago", _gsSmall, GUILayout.Width(54f));
            GUI.color = Color.white;

            GUILayout.EndHorizontal();
        }
    }
}
