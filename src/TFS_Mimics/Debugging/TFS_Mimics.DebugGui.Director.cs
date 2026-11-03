using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Tab: Director (host decisions, event feed, simulations) ──────────────
        private Vector2 _scrollDirector;
        private int _directorExpandedDecision = -1;

        private void DrawDirectorTab(float scrollH)
        {
            var isHost = IsDirectorHost;

            GUILayout.BeginHorizontal();
            DrawChip(DirectorEnabled ? "smart director on" : "smart director off (random)", DirectorEnabled ? CGreen : CTextDim);
            DrawChip(isHost ? "you are the host" : "runs on the host", isHost ? CAccent : CTextDim);
            if (_directorPending.Count > 0) DrawChip($"{_directorPending.Count} pending", CYellow);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            if (!isHost)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  Only the host decides what plays. Open this tab as host to see decisions and events.", _gsLabel);
                GUI.color = Color.white;
                return;
            }

            // ── Simulations ───────────────────────────────────────────────────
            var inLevel = SemiFunc.RunIsLevel();
            GUILayout.BeginHorizontal();
            GUI.enabled = inLevel;
            if (GUILayout.Button("Tick now", _gsBtnPrimary, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) HostAuthorityTick();
            GUILayout.Space(6f);
            GUILayout.Label("simulate:", _gsSmall, GUILayout.ExpandWidth(false));
            if (GUILayout.Button("death", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) DirectorSimulate(DirectorEventKind.Death);
            if (GUILayout.Button("hurt", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) DirectorSimulate(DirectorEventKind.Hurt);
            if (GUILayout.Button("spotted", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) DirectorSimulate(DirectorEventKind.Spotted);
            if (GUILayout.Button("break", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) DirectorSimulate(DirectorEventKind.Break);
            if (GUILayout.Button("head", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) DirectorSimulate(DirectorEventKind.HeadTalk);
            if (GUILayout.Button("pickup", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false))) DirectorSimulate(DirectorEventKind.Pickup);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label("  Simulations use your own voice and position, fire in ~1s and skip the real delays.", _gsSmall);
            GUI.color = Color.white;
            GUILayout.Space(3f);

            _scrollDirector = GUILayout.BeginScrollView(_scrollDirector, GUILayout.Height(Mathf.Max(80f, scrollH - 62f)));

            // ── Pending events ────────────────────────────────────────────────
            if (_directorPending.Count > 0)
            {
                DrawSettingsSection("Pending events");
                foreach (var ev in _directorPending)
                {
                    GUILayout.BeginHorizontal(_gsRow);
                    GUI.color = CYellow;
                    GUILayout.Label($"{Mathf.Max(0f, ev.FireAt - Time.time):F1}s", _gsSmall, GUILayout.Width(40f));
                    GUI.color = CText;
                    GUILayout.Label($"{ev.Kind}: {ev.Label}", _gsSmall);
                    GUI.color = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(4f);
            }

            // ── Decisions ─────────────────────────────────────────────────────
            DrawSettingsSection("Decisions");
            if (_directorDecisions.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  Nothing decided yet. Press \"Tick now\" or wait for the timer.", _gsSmall);
                GUI.color = Color.white;
            }
            for (var i = _directorDecisions.Count - 1; i >= 0; i--)
            {
                var d = _directorDecisions[i];
                var expanded = _directorExpandedDecision == i;
                GUILayout.BeginVertical(_gsPanelBox);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(expanded ? "▼" : "▶", _gsIconBtn, GUILayout.Width(20f), GUILayout.Height(18f)))
                    _directorExpandedDecision = expanded ? -1 : i;
                GUI.color = CTextDim;
                GUILayout.Label(FormatAgo(d.Time), _gsSmall, GUILayout.Width(52f));
                GUI.color = d.Reason == "timer" ? CAccent : CYellow;
                GUILayout.Label(FitHudText(d.Reason, 34), _gsSmall, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                GUI.color = CTextDim;
                GUILayout.Label($"score {d.Score:F2}", _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(24f);
                GUI.color = CGreen;
                GUILayout.Label($"{FitHudText(d.Sound, 22)}  →  {FitHudText(d.Enemies, 30)}", _gsLabel, GUILayout.ExpandWidth(false));
                GUI.color = CTextDim;
                GUILayout.Label($"{d.Listeners} listener{(d.Listeners != 1 ? "s" : "")}", _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                if (expanded)
                {
                    foreach (var line in d.Details)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(24f);
                        GUI.color = CTextDim;
                        GUILayout.Label(line, _gsSmall);
                        GUI.color = Color.white;
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            // ── Feed ──────────────────────────────────────────────────────────
            GUILayout.Space(4f);
            DrawSettingsSection("Event feed");
            if (_directorFeed.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No events yet.", _gsSmall);
                GUI.color = Color.white;
            }
            for (var i = _directorFeed.Count - 1; i >= 0; i--)
            {
                var f = _directorFeed[i];
                GUILayout.BeginHorizontal(i % 2 == 0 ? _gsRow : _gsRowAlt);
                GUI.color = CTextDim;
                GUILayout.Label(FormatAgo(f.Time), _gsSmall, GUILayout.Width(52f));
                GUI.color = f.Color;
                GUILayout.Label(FitHudText(f.Kind, 12), _gsSmall, GUILayout.Width(70f));
                GUI.color = CText;
                GUILayout.Label(f.Text, _gsSmall);
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private static string FormatAgo(float time)
        {
            var age = Time.time - time;
            return age < 60f ? $"{age:F0}s ago" : $"{age / 60f:F0}m ago";
        }
    }
}
