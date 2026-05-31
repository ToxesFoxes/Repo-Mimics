using System.Collections.Generic;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── IMGUI Gizmos — AudioSource dots + player marker ─────────────────────
        private GUIStyle _gsGizmoLabel;

        private void DrawWorldGizmos()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (_gsGizmoLabel == null)
            {
                _gsGizmoLabel = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                    normal = { textColor = Color.white }
                };
            }

            // Local player marker (IMGUI, screen-space)
            var psp = cam.WorldToScreenPoint(transform.position);
            if (psp.z > 0f)
            {
                var psy = Screen.height - psp.y;
                GUI.color = CYellow;
                DrawScreenRect(psp.x - 5f, psy - 5f, 10f, 10f, 1f);
                GUI.color = Color.white;
                GUI.Label(new Rect(psp.x - 30f, psy + 7f, 60f, 16f), "YOU", _gsGizmoLabel);
            }
        }

        // Draws a hollow rectangle outline of given thickness
        private void DrawScreenRect(float x, float y, float w, float h, float thickness)
        {
            GUI.DrawTexture(new Rect(x, y, w, thickness), _txWhite);
            GUI.DrawTexture(new Rect(x, y + h - thickness, w, thickness), _txWhite);
            GUI.DrawTexture(new Rect(x, y, thickness, h), _txWhite);
            GUI.DrawTexture(new Rect(x + w - thickness, y, thickness, h), _txWhite);
        }

        // ─── Enemy Overlay + AudioMarker Manager ─────────────────────────────────
        private void RefreshEnemyOverlays()
        {
            if (Time.time < _overlayNextRefresh) return;
            _overlayNextRefresh = Time.time + 0.5f;

            // ── EnemyParent overlays (name / HP / state / distance) ───────────
            var parents = FindObjectsByType<EnemyParent>(FindObjectsSortMode.None);
            var seenEnemies = new HashSet<int>();

            foreach (var ep in parents)
            {
                if (ep == null) continue;
                var id = ep.GetInstanceID();
                seenEnemies.Add(id);

                if (!_enemyOverlays.TryGetValue(id, out var overlay) || overlay == null)
                {
                    var go = new GameObject("_MimicsOverlayHost");
                    overlay = go.AddComponent<MimicsEnemyOverlay>();
                    overlay.Init(ep, transform);
                    _enemyOverlays[id] = overlay;
                }
                else
                {
                    overlay.IsPlayingAudio = IsEnemyCurrentlyPlaying(ep.gameObject);
                }
            }

            var toRemoveE = new List<int>();
            foreach (var kv in _enemyOverlays)
            {
                if (!seenEnemies.Contains(kv.Key))
                {
                    if (kv.Value != null) Destroy(kv.Value.gameObject);
                    toRemoveE.Add(kv.Key);
                }
            }
            foreach (var k in toRemoveE) _enemyOverlays.Remove(k);

            // ── AudioSource markers (♪ on the exact attach-point) ────────────
            var seenAudio = new HashSet<int>();

            foreach (var kv in reusableEnemyAudioSources)
            {
                var src = kv.Value;
                if (src == null || src.gameObject == null) continue;

                var id = kv.Key;
                seenAudio.Add(id);

                if (!_audioMarkers.TryGetValue(id, out var marker) || marker == null)
                {
                    // Add directly to the AudioSource's own GameObject — follows it for free
                    marker = src.gameObject.AddComponent<MimicsAudioMarker>();
                    marker.Init(src);
                    _audioMarkers[id] = marker;
                }
                marker.SetVisible(OverlaySettings.ShowAudioMarker);
            }

            var toRemoveA = new List<int>();
            foreach (var kv in _audioMarkers)
            {
                if (!seenAudio.Contains(kv.Key))
                {
                    // Destroy only the child canvas, not the host GO (we don't own it)
                    if (kv.Value != null) Destroy(kv.Value);
                    toRemoveA.Add(kv.Key);
                }
            }
            foreach (var k in toRemoveA) _audioMarkers.Remove(k);
        }

        private bool IsEnemyCurrentlyPlaying(GameObject enemy)
        {
            foreach (var c in nearestPlaybackCandidatesHud)
            {
                if (c != null && c.Enemy == enemy && c.IsPlaying) return true;
            }
            return false;
        }

        internal void DestroyAllOverlays()
        {
            foreach (var kv in _enemyOverlays)
            {
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            _enemyOverlays.Clear();

            foreach (var kv in _audioMarkers)
            {
                if (kv.Value != null) Destroy(kv.Value);  // OnDestroy will also destroy the canvas child
            }
            _audioMarkers.Clear();

            // Reset timer so overlays recreate immediately when Gizmos are re-enabled
            _overlayNextRefresh = 0f;
        }
    }
}
