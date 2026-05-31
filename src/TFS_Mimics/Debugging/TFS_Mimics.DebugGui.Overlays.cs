using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Tab 5: Overlays ──────────────────────────────────────────────────────
        private void DrawOverlaysTab(float scrollH)
        {
            _scrollOverlays = GUILayout.BeginScrollView(_scrollOverlays, GUILayout.Height(scrollH));

            // ── Billboard overlays ─────────────────────────────────────────────
            DrawSettingsSection("Billboard Overlays");

            // Master enable / disable
            GUILayout.BeginHorizontal(_gsPanelBox);
            GUI.color = CText;
            GUILayout.Label("Show Overlays", _gsLabel, GUILayout.Width(200f));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            var before = _showGizmos;
            var after = GUILayout.Toggle(before, before ? "  ON" : "  OFF", _gsLabel, GUILayout.ExpandWidth(false));
            if (after != before)
            {
                _showGizmos = after;
                if (!_showGizmos) DestroyAllOverlays();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);

            GUI.color = CTextDim;
            GUILayout.Label("  Floating billboards above each mob — name, HP, state, distance, playback status.", _gsSmall);
            GUI.color = Color.white;
            GUILayout.Space(6f);

            // ── Visible sections ───────────────────────────────────────────────
            DrawSettingsSection("Visible Sections");

            DrawOverlayToggle("Mob Name", ref OverlaySettings.ShowName);
            DrawOverlayToggle("HP Bar", ref OverlaySettings.ShowHp);
            DrawOverlayToggle("AI State", ref OverlaySettings.ShowState);
            DrawOverlayToggle("Distance", ref OverlaySettings.ShowDistance);
            DrawOverlayToggle("Playing Indicator", ref OverlaySettings.ShowPlaying);
            GUILayout.Space(6f);

            // ── Audio Source Marker ────────────────────────────────────────────
            DrawSettingsSection("Audio Source Marker");

            DrawOverlayToggle("Show Audio Marker", ref OverlaySettings.ShowAudioMarker);
            GUI.color = CTextDim;
            GUILayout.Label("  \u266b billboard at the exact AudioSource position on each mob.", _gsSmall);
            GUI.color = Color.white;

            GUILayout.Space(8f);
            GUILayout.EndScrollView();
        }

        private void DrawOverlayToggle(string label, ref bool value)
        {
            GUILayout.BeginHorizontal(_gsPanelBox);
            GUI.color = CText;
            GUILayout.Label(label, _gsLabel, GUILayout.Width(200f));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            value = GUILayout.Toggle(value, value ? "  ON" : "  OFF", _gsLabel, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }
    }
}
