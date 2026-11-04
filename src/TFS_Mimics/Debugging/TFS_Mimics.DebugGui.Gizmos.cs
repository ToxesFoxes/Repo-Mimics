using System.Collections.Generic;
using System.Linq;
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

            // Lines and dots are real world objects (see UpdateWorldGizmos); only their labels are screen text.
            if (Event.current.type != EventType.Repaint) return;
            if (OverlaySettings.ShowTalk)
            {
                foreach (var track in _talkTracks.Values)
                    if (TryGetTalkCurve(track, out _, out var control, out _))
                        DrawWorldLabel(cam, control, $"{track.Name} → {NormalizeEnemyName(track.ShownEnemy.name)}: {track.Status}");
            }
            if (OverlaySettings.ShowTalk && _mobDialog != null && _mobDialog.GoA != null && _mobDialog.GoB != null)
            {
                var played = _mobDialog.Lines.Count(l => l.Played);
                var mid = (GetEnemyCenter(_mobDialog.GoA) + GetEnemyCenter(_mobDialog.GoB)) * 0.5f + Vector3.up * 1.6f;
                DrawWorldLabel(cam, mid, $"dialog {_mobDialog.NameA} ↔ {_mobDialog.NameB}: line {played}/{_mobDialog.Lines.Count} ({_mobDialog.Kind})");
            }
            if (OverlaySettings.ShowHits)
            {
                foreach (var link in _hitLinks)
                    if (Time.time <= link.Until)
                        DrawWorldLabel(cam, (link.From + link.To) * 0.5f + Vector3.up * 1.3f, link.Label);
            }
        }

        private void DrawWorldLabel(Camera cam, Vector3 world, string text)
        {
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.05f) return;
            GUI.Label(new Rect(sp.x - 150f, Screen.height - sp.y - 8f, 300f, 18f), text, _gsGizmoLabel);
        }

        // ─── Director gizmos (world space, like Imperium: LineRenderers + primitive spheres) ───
        private readonly List<Enemy> _gizmoEnemies = new List<Enemy>();
        private GizmoPool _gizmoPool;
        private readonly List<Vector3> _gizmoPoints = new List<Vector3>();

        private static readonly Color GizmoPathColor = new Color(0.3f, 0.9f, 1f, 0.9f);
        private static readonly Color GizmoHitColor = new Color(1f, 0.3f, 0.3f, 1f);

        private float _gizmoNextUpdateAt;

        // Called from Update while overlays are on; rebuilds every line from the current state at 20 Hz
        // (reading agent.path allocates a fresh NavMeshPath per enemy, too much for every frame).
        private void UpdateWorldGizmos()
        {
            // The pool's root lives in the scene, so a level change destroys it.
            var fresh = _gizmoPool == null || !_gizmoPool.IsAlive;
            if (!fresh && Time.time < _gizmoNextUpdateAt) return;
            _gizmoNextUpdateAt = Time.time + 0.05f;
            if (fresh) _gizmoPool = new GizmoPool();
            _gizmoPool.Begin();

            if (OverlaySettings.ShowPaths)
            {
                foreach (var enemy in _gizmoEnemies)
                {
                    if (enemy == null || !enemy.HasNavMeshAgent || enemy.NavMeshAgent == null) continue;
                    var agent = enemy.NavMeshAgent.Agent;
                    if (agent == null || !agent.enabled || !agent.isOnNavMesh || !agent.hasPath) continue;

                    _gizmoPoints.Clear();
                    _gizmoPoints.Add(enemy.transform.position);
                    _gizmoPoints.AddRange(agent.path.corners);
                    _gizmoPool.Line(_gizmoPoints, 0.04f, GizmoPathColor);
                    for (var i = 1; i < _gizmoPoints.Count; i++)
                        _gizmoPool.Dot(_gizmoPoints[i], i == _gizmoPoints.Count - 1 ? 0.25f : 0.12f, GizmoPathColor);
                }
            }

            if (OverlaySettings.ShowTalk)
            {
                foreach (var track in _talkTracks.Values)
                {
                    // Where the mod thinks the player looks while talking.
                    if (Time.time <= track.RayUntil)
                    {
                        _gizmoPoints.Clear();
                        _gizmoPoints.Add(track.RayOrigin);
                        _gizmoPoints.Add(track.RayEnd);
                        _gizmoPool.Line(_gizmoPoints, 0.015f, new Color(1f, 1f, 1f, 0.6f));
                    }

                    if (!TryGetTalkCurve(track, out var from, out var control, out var to)) continue;
                    _gizmoPoints.Clear();
                    for (var i = 0; i <= 20; i++)
                    {
                        var t = i / 20f;
                        _gizmoPoints.Add((1 - t) * (1 - t) * from + 2 * (1 - t) * t * control + t * t * to);
                    }
                    var width = track.Enemy != null ? 0.02f + Mathf.Min(track.TalkTime / TalkMinSpeech, 1f) * 0.04f : 0.04f;
                    _gizmoPool.Line(_gizmoPoints, width, track.StatusColor);
                    _gizmoPool.Dot(to, 0.2f, track.StatusColor);
                }
            }

            if (OverlaySettings.ShowHits)
            {
                foreach (var link in _hitLinks)
                {
                    if (Time.time > link.Until) continue;
                    var color = link.Color;
                    color.a = Mathf.Clamp01(link.Until - Time.time);
                    _gizmoPoints.Clear();
                    _gizmoPoints.Add(link.From + Vector3.up);
                    _gizmoPoints.Add(link.To + Vector3.up);
                    _gizmoPool.Line(_gizmoPoints, 0.05f, color);
                    _gizmoPool.Dot(link.To + Vector3.up, 0.3f, color);
                }
            }

            if (OverlaySettings.ShowTalk && _mobDialog != null && _mobDialog.GoA != null && _mobDialog.GoB != null)
            {
                var a = GetEnemyCenter(_mobDialog.GoA) + Vector3.up * 0.5f;
                var b = GetEnemyCenter(_mobDialog.GoB) + Vector3.up * 0.5f;
                var color = new Color(0.75f, 0.45f, 1f, 0.9f);
                _gizmoPoints.Clear();
                for (var i = 0; i <= 16; i++)
                {
                    var t = i / 16f;
                    _gizmoPoints.Add(Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.8f);
                }
                _gizmoPool.Line(_gizmoPoints, 0.05f, color);
                _gizmoPool.Dot(a, 0.25f, color);
                _gizmoPool.Dot(b, 0.25f, color);
            }

            _gizmoPool.End();
        }

        private bool TryGetTalkCurve(TalkTrack track, out Vector3 from, out Vector3 control, out Vector3 to)
        {
            from = control = to = Vector3.zero;
            if (track.Avatar == null || track.ShownEnemy == null || Time.time > track.StatusUntil) return false;

            var vision = track.Avatar.PlayerVisionTarget != null ? track.Avatar.PlayerVisionTarget.VisionTransform : null;
            from = vision != null ? vision.position : track.Avatar.transform.position + Vector3.up * 1.5f;
            to = GetEnemyCenter(track.ShownEnemy);
            control = (from + to) * 0.5f + Vector3.up * (0.5f + Vector3.Distance(from, to) * 0.15f);
            return true;
        }

        private void HideWorldGizmos()
        {
            if (_gizmoPool == null || !_gizmoPool.IsAlive) return;
            _gizmoPool.Begin();
            _gizmoPool.End();
        }

        /// <summary>Reusable world-space LineRenderers and spheres; whatever isn't used in a frame is hidden.</summary>
        private sealed class GizmoPool
        {
            private readonly GameObject _root = new GameObject("_MimicsGizmos");
            private readonly List<LineRenderer> _lines = new List<LineRenderer>();
            private readonly List<MeshRenderer> _dots = new List<MeshRenderer>();
            private readonly Material _material = new Material(Shader.Find("Sprites/Default"));
            private int _linesUsed;
            private int _dotsUsed;

            internal bool IsAlive => _root != null;

            internal void Begin()
            {
                _linesUsed = 0;
                _dotsUsed = 0;
            }

            internal void Line(List<Vector3> points, float width, Color color)
            {
                if (_linesUsed == _lines.Count)
                {
                    var go = new GameObject("Line");
                    go.transform.SetParent(_root.transform, false);
                    var created = go.AddComponent<LineRenderer>();
                    created.sharedMaterial = _material;
                    created.useWorldSpace = true;
                    created.numCapVertices = 2;
                    created.numCornerVertices = 2;
                    created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    created.receiveShadows = false;
                    _lines.Add(created);
                }

                var line = _lines[_linesUsed++];
                line.gameObject.SetActive(true);
                line.startWidth = line.endWidth = width;
                line.startColor = line.endColor = color;
                line.positionCount = points.Count;
                for (var i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            }

            internal void Dot(Vector3 position, float size, Color color)
            {
                if (_dotsUsed == _dots.Count)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(go.GetComponent<Collider>());
                    go.name = "Dot";
                    go.transform.SetParent(_root.transform, false);
                    var created = go.GetComponent<MeshRenderer>();
                    created.material = new Material(_material);
                    created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    created.receiveShadows = false;
                    _dots.Add(created);
                }

                var dot = _dots[_dotsUsed++];
                dot.gameObject.SetActive(true);
                dot.transform.position = position;
                dot.transform.localScale = Vector3.one * size;
                dot.material.color = color;
            }

            internal void End()
            {
                for (var i = _linesUsed; i < _lines.Count; i++)
                    if (_lines[i].gameObject.activeSelf) _lines[i].gameObject.SetActive(false);
                for (var i = _dotsUsed; i < _dots.Count; i++)
                    if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
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

            _gizmoEnemies.Clear();
            foreach (var ep in parents)
                if (ep != null && ep.Enemy != null) _gizmoEnemies.Add(ep.Enemy);

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
