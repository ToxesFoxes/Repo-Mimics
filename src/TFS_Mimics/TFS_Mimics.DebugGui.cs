using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Audio;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Window State ────────────────────────────────────────────────────────
        private bool _debugWindowOpen;
        private bool _showGizmos;
        private Rect _debugWindowRect = new Rect(20f, 20f, 520f, 500f);
        private int _debugTab;
        private Vector2 _scrollMobs, _scrollPlayers, _scrollCache, _scrollVoiceLog;
        private const int VoiceLogMaxEntries = 200;
        private readonly List<VoiceLogEntry> _voiceLog = new List<VoiceLogEntry>();
        private readonly HashSet<string> _cacheExpandedPlayers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private bool _isResizing;
        private Vector2 _resizeMouseStart;
        private Rect _resizeRectStart;

        // ─── Enemy Overlay Manager ────────────────────────────────────────────────
        private readonly Dictionary<int, MimicsEnemyOverlay> _enemyOverlays = new Dictionary<int, MimicsEnemyOverlay>();
        private readonly Dictionary<int, MimicsAudioMarker>  _audioMarkers  = new Dictionary<int, MimicsAudioMarker>();
        private float _overlayNextRefresh;

        private static readonly string[] TabNames = { "Mobs", "Players", "Cache", "Voice Log", "Settings" };

        // ─── Settings Tab State ───────────────────────────────────────────────────
        private string _settingVolumeBuf;
        private string _settingRadiusBuf;
        private string _settingMinDelayBuf;
        private string _settingMaxDelayBuf;
        private string _settingMaxFilesBuf;
        private string _settingSamplingRateBuf;
        private string _settingNormalizeBuf;
        private bool _settingsDirty;
        private Vector2 _scrollSettings;

        // ─── Force-Play Modal State ──────────────────────────────────────────────
        private bool _fpmOpen;
        private int _fpmPage;       // 0 = Player, 1 = Clip, 2 = Enemy
        private int _fpmPlayerIdx;  // -1 = Random
        private int _fpmClipIdx;    // -1 = Random within player
        private int _fpmEnemyIdx;   // -1 = Nearest
        private bool _fpmHearYourself;
        private Vector2 _fpmScrollPlayer, _fpmScrollClip, _fpmScrollEnemy;
        private List<FpmPlayerEntry> _fpmPlayers;
        private List<FpmEnemyEntry> _fpmEnemies;

        private sealed class FpmPlayerEntry
        {
            public string PlayerId;
            public string DisplayName;
            public bool IsLocalPlayer;
            public bool IsOnline;
            public readonly List<int> CacheIndices = new List<int>();
        }

        private sealed class FpmEnemyEntry
        {
            public GameObject Enemy;
            public GameObject Target;
            public string Name;
            public float Distance;
            public bool IsPlaying;
        }

        private sealed class VoiceLogEntry
        {
            public bool IsIncoming;     // true = received, false = sent
            public string TransmissionId;
            public string PlayerId;
            public string PlayerName;
            public int Bytes;
            public bool IsComplete;
            public float ReceivedAt;
        }

        // ─── GUI Styles & Textures ───────────────────────────────────────────────
        private bool _guiStylesInit;
        private GUIStyle _gsWindow, _gsH1, _gsLabel, _gsLabelDim, _gsSmall;
        private GUIStyle _gsBtn, _gsBtnPrimary, _gsBtnDanger, _gsBtnYellow;
        private GUIStyle _gsTabBtn, _gsTabBtnActive;
        private GUIStyle _gsListItem, _gsListItemSel;
        private GUIStyle _gsPanelBox, _gsModalTitle;

        private Texture2D _txWhite, _txBgDark, _txBgPanel, _txBgHover;
        private Texture2D _txAccent, _txAccentDim, _txGreen, _txRed, _txYellow;

        private static readonly Color CAccent = new Color(0.29f, 0.67f, 1.00f);
        private static readonly Color CAccentDim = new Color(0.16f, 0.38f, 0.62f);
        private static readonly Color CGreen = new Color(0.27f, 0.87f, 0.42f);
        private static readonly Color CRed = new Color(1.00f, 0.33f, 0.33f);
        private static readonly Color CYellow = new Color(1.00f, 0.84f, 0.20f);
        private static readonly Color CText = new Color(0.90f, 0.92f, 0.94f);
        private static readonly Color CTextDim = new Color(0.50f, 0.54f, 0.58f);
        private static readonly Color CBgDark = new Color(0.07f, 0.09f, 0.11f, 0.97f);
        private static readonly Color CBgPanel = new Color(0.11f, 0.14f, 0.17f, 1.00f);
        private static readonly Color CBgHover = new Color(0.18f, 0.22f, 0.27f, 1.00f);

        // ─── Cursor State ─────────────────────────────────────────────────────────
        private CursorLockMode _savedCursorLockMode;
        private bool _savedCursorVisible;
        private bool _cursorOverrideActive;

        private void SetCursorForGui(bool guiActive)
        {
            if (guiActive && !_cursorOverrideActive)
            {
                _savedCursorLockMode = Cursor.lockState;
                _savedCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorOverrideActive = true;
            }
            else if (!guiActive && _cursorOverrideActive)
            {
                Cursor.lockState = _savedCursorLockMode;
                Cursor.visible = _savedCursorVisible;
                _cursorOverrideActive = false;
            }
        }

        // ─── Update Hook ─────────────────────────────────────────────────────────
        private bool _debugWindowFocused = true;

        private void DebugGuiUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift && _debugWindowOpen)
                {
                    // Shift+F8 — снять фокус (вернуть курсор игре), окно остаётся видимым
                    _debugWindowFocused = false;
                }
                else
                {
                    // F8 — переключить окно
                    _debugWindowOpen = !_debugWindowOpen;
                    _debugWindowFocused = _debugWindowOpen;
                    if (_debugWindowOpen)
                    {
                        hudNextRefreshAt = 0f;
                    }
                    else
                    {
                        _fpmOpen = false;
                    }
                }
            }

            // Клик внутри области окна восстанавливает фокус
            if (_debugWindowOpen && !_debugWindowFocused && Input.GetMouseButtonDown(0))
            {
                var mp = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (_debugWindowRect.Contains(mp))
                {
                    _debugWindowFocused = true;
                }
            }

            var needsCursor = _debugWindowOpen && _debugWindowFocused;
            SetCursorForGui(needsCursor);

            if (!_debugWindowOpen)
            {
                return;
            }

            if (Time.time >= hudNextRefreshAt)
            {
                hudNextRefreshAt = Time.time + 0.25f;
                RefreshHudTargetsSnapshot();
            }

            if (_showGizmos)
            {
                RefreshEnemyOverlays();
            }
        }

        // ─── OnGUI ───────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (photonView == null || !photonView.IsMine || !SemiFunc.RunIsLevel())
            {
                return;
            }

            EnsureGuiStyles();

            if (!_debugWindowOpen)
            {
                return;
            }

            if (_showGizmos)
            {
                DrawWorldGizmos();
            }

            if (_fpmOpen)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _txWhite);
                GUI.color = Color.white;

                var mw = 440f;
                var mh = 390f;
                var modalRect = new Rect((Screen.width - mw) * 0.5f, (Screen.height - mh) * 0.5f, mw, mh);
                GUI.Window(9901, modalRect, DrawForcePlayModal, string.Empty, _gsWindow);
                return;
            }

            _debugWindowRect = GUI.Window(9900, _debugWindowRect, DrawMainWindow, string.Empty, _gsWindow);
            HandleResizeInput();

            // Resize handle indicator (drawn over the window frame)
            var handleRect = new Rect(
                _debugWindowRect.x + _debugWindowRect.width - 18f,
                _debugWindowRect.y + _debugWindowRect.height - 18f,
                18f, 18f);
            GUI.color = _isResizing ? CAccent : CTextDim;
            GUI.Label(handleRect, "◢", _gsSmall);
            GUI.color = Color.white;
        }

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
                    var go  = new GameObject("_MimicsOverlayHost");
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

        private void HandleResizeInput()
        {
            var handleRect = new Rect(
                _debugWindowRect.x + _debugWindowRect.width - 18f,
                _debugWindowRect.y + _debugWindowRect.height - 18f,
                18f, 18f);

            var ev = Event.current;

            if (!_isResizing && ev.type == EventType.MouseDown && ev.button == 0 && handleRect.Contains(ev.mousePosition))
            {
                _isResizing = true;
                _resizeMouseStart = ev.mousePosition;
                _resizeRectStart = _debugWindowRect;
                ev.Use();
            }

            if (_isResizing)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    var delta = ev.mousePosition - _resizeMouseStart;
                    _debugWindowRect.width = Mathf.Max(420f, _resizeRectStart.width + delta.x);
                    _debugWindowRect.height = Mathf.Max(320f, _resizeRectStart.height + delta.y);
                    ev.Use();
                }

                if (ev.type == EventType.MouseUp)
                {
                    _isResizing = false;
                    ev.Use();
                }
            }
        }

        // ─── Main Window ─────────────────────────────────────────────────────────
        private void DrawMainWindow(int id)
        {
            DrawWindowTitleBar("MIMICS  DEBUG", "[F8]");
            GUILayout.Space(4f);
            DrawPlaybackHeader();
            GUILayout.Space(4f);
            DrawTabRow();
            GUILayout.Space(4f);

            var scrollH = Mathf.Max(100f, _debugWindowRect.height - 228f);
            switch (_debugTab)
            {
                case 0: DrawNearestMobsTab(scrollH); break;
                case 1: DrawPlayersTab(scrollH); break;
                case 2: DrawCacheTab(scrollH); break;
                case 3: DrawVoiceLogTab(scrollH); break;
                case 4: DrawSettingsTab(scrollH); break;
            }

            GUI.DragWindow(new Rect(0f, 0f, _debugWindowRect.width - 28f, 26f));
        }

        // ─── Shared Helpers ───────────────────────────────────────────────────────
        private void DrawWindowTitleBar(string title, string hint)
        {
            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label(title, _gsH1, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            // Gizmo toggle
            GUI.color = _showGizmos ? CYellow : CTextDim;
            if (GUILayout.Button(_showGizmos ? "◆ Gizmos" : "◇ Gizmos", _gsBtn, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
            {
                _showGizmos = !_showGizmos;
                if (!_showGizmos) DestroyAllOverlays();
            }
            GUI.color = Color.white;
            GUILayout.Space(6f);
            GUI.color = CTextDim;
            GUILayout.Label(hint, _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.Space(4f);
            if (GUILayout.Button("×", _gsBtnDanger, GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                _debugWindowOpen = false;
            }
            GUILayout.EndHorizontal();
            DrawHRule();
        }

        private void DrawHRule()
        {
            GUILayout.Space(2f);
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            GUI.color = new Color(0.22f, 0.28f, 0.36f);
            GUI.DrawTexture(r, _txWhite);
            GUI.color = Color.white;
            GUILayout.Space(2f);
        }

        private void DrawProgressBar(float t, string label, Color barColor)
        {
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(13f));
            GUI.color = new Color(0.15f, 0.19f, 0.24f);
            GUI.DrawTexture(r, _txWhite);
            var fillW = r.width * Mathf.Clamp01(t);
            if (fillW > 0f)
            {
                GUI.color = barColor;
                GUI.DrawTexture(new Rect(r.x, r.y, fillW, r.height), _txWhite);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 4f, r.y - 1f, r.width - 8f, r.height + 2f), label, _gsSmall);
        }

        private static string FitHudText(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "—";
            }

            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 2) + "…";
        }

        // ─── Playback Header ──────────────────────────────────────────────────────
        private void DrawPlaybackHeader()
        {
            var isActive = Time.time <= currentPlaybackEndsAt;
            GUILayout.BeginVertical(_gsPanelBox);

            GUILayout.BeginHorizontal();
            GUI.color = isActive ? CGreen : CTextDim;
            GUILayout.Label(isActive ? "▶  NOW PLAYING" : "■  IDLE", _gsH1, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            // Recording indicator
            if (capturingSpeech)
            {
                var recDur = sampleRate > 0 ? bufferPosition / (float)sampleRate : 0f;
                var localNick = PhotonNetwork.LocalPlayer != null && !string.IsNullOrWhiteSpace(PhotonNetwork.LocalPlayer.NickName)
                    ? PhotonNetwork.LocalPlayer.NickName
                    : "You";
                GUI.color = CRed;
                GUILayout.Label($"● REC  {FitHudText(localNick, 16)}  {recDur:F1}s", _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            if (isActive)
            {
                GUILayout.BeginHorizontal();
                GUI.color = CTextDim;
                GUILayout.Label("Enemy:", _gsSmall, GUILayout.Width(44f));
                GUI.color = Color.white;
                GUILayout.Label(FitHudText(currentPlaybackEnemyName, 20), _gsLabel);
                GUILayout.Space(12f);
                GUI.color = CTextDim;
                GUILayout.Label("Source:", _gsSmall, GUILayout.Width(44f));
                GUI.color = Color.white;
                GUILayout.Label(FitHudText(currentPlaybackSourcePlayerId, 24), _gsLabel);
                GUILayout.EndHorizontal();

                float clipLen = 0f;
                float startedAt = 0f;
                foreach (var kv in playbackClipLengthByTargetKey)
                {
                    clipLen = kv.Value;
                    break;
                }
                foreach (var kv in playbackStartedAtByTargetKey)
                {
                    startedAt = kv.Value;
                    break;
                }
                if (clipLen <= 0f)
                {
                    clipLen = Mathf.Max(0f, currentPlaybackEndsAt - Time.time);
                }
                var elapsed = Mathf.Clamp(Time.time - startedAt, 0f, clipLen);
                GUILayout.Space(3f);
                DrawProgressBar(clipLen > 0f ? elapsed / clipLen : 0f, $"{elapsed:F1}s / {clipLen:F1}s", CGreen);
            }
            else
            {
                GUI.color = CTextDim;
                GUILayout.Label("No active playback", _gsSmall);
                GUI.color = Color.white;
            }

            GUILayout.EndVertical();
        }

        // ─── Tab Row ──────────────────────────────────────────────────────────────
        private void DrawTabRow()
        {
            GUILayout.BeginHorizontal();
            for (var i = 0; i < TabNames.Length; i++)
            {
                var style = _debugTab == i ? _gsTabBtnActive : _gsTabBtn;
                if (GUILayout.Button(TabNames[i], style, GUILayout.Height(26f)))
                {
                    _debugTab = i;
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶  Force Play…", _gsBtnYellow, GUILayout.Height(26f), GUILayout.ExpandWidth(false)))
            {
                OpenForcePlayModal();
            }
            GUILayout.EndHorizontal();
            DrawHRule();
        }

        // ─── Tab 1: Nearest Mobs ──────────────────────────────────────────────────
        private void DrawNearestMobsTab(float scrollH)
        {
            var nearRadius = Plugin.configPlaybackNearRadius != null ? Plugin.configPlaybackNearRadius.Value : 12f;

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Radius: {nearRadius:F0}m   Player: {FormatHudVector(transform.position)}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            _scrollMobs = GUILayout.BeginScrollView(_scrollMobs, GUILayout.Height(scrollH));
            if (nearestPlaybackCandidatesHud.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No eligible enemies in scene.", _gsLabel);
                GUI.color = Color.white;
            }
            else
            {
                foreach (var c in nearestPlaybackCandidatesHud)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    DrawMobRow(c, nearRadius);
                    GUILayout.Space(2f);
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawMobRow(HudPlaybackCandidate c, float nearRadius)
        {
            var inRadius = c.Distance <= nearRadius;
            GUILayout.BeginVertical(_gsPanelBox);

            GUILayout.BeginHorizontal();
            GUI.color = c.IsPlaying ? CGreen : (inRadius ? CAccent : CTextDim);
            GUILayout.Label(c.IsPlaying ? "▶" : (inRadius ? "●" : "○"), _gsLabel, GUILayout.Width(14f));
            GUI.color = Color.white;

            GUILayout.Label(c.EnemyName, c.IsPlaying ? new GUIStyle(_gsLabel) { fontStyle = FontStyle.Bold } : _gsLabel);

            GUILayout.FlexibleSpace();

            GUI.color = inRadius ? CText : CTextDim;
            GUILayout.Label($"{c.Distance:F1}m", _gsSmall, GUILayout.Width(46f));
            GUI.color = Color.white;

            if (!inRadius)
            {
                GUI.color = new Color(0.35f, 0.35f, 0.35f);
                GUILayout.Label("out of radius", _gsSmall, GUILayout.Width(78f));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            if (c.IsPlaying)
            {
                var key = GetPlaybackTargetKey(c.Enemy, c.Target);
                var startAt = playbackStartedAtByTargetKey.TryGetValue(key, out var sa) ? sa : Time.time;
                var clipLen = playbackClipLengthByTargetKey.TryGetValue(key, out var cl) ? cl : 0f;
                var elapsed = Mathf.Clamp(Time.time - startAt, 0f, clipLen);
                GUILayout.Space(2f);
                DrawProgressBar(clipLen > 0f ? elapsed / clipLen : 0f, $"{elapsed:F1}s / {clipLen:F1}s", CGreen);
            }

            GUI.color = CTextDim;
            GUILayout.Label($"  {FormatHudVector(c.Position)}", _gsSmall);
            GUI.color = Color.white;

            GUILayout.EndVertical();
        }

        // ─── Tab 2: Players ───────────────────────────────────────────────────────
        private void DrawPlayersTab(float scrollH)
        {
            var onlineIds = GetOnlinePlayerIds();
            var localId = PhotonNetwork.LocalPlayer != null ? GetPlayerPersistentId(PhotonNetwork.LocalPlayer) : string.Empty;

            var byPlayer = new Dictionary<string, (string name, int count)>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in cachedAudio)
            {
                if (e == null)
                {
                    continue;
                }

                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                var name = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid;
                byPlayer[pid] = byPlayer.TryGetValue(pid, out var cur) ? (cur.name, cur.count + 1) : (name, 1);
            }

            if (PhotonNetwork.PlayerList != null)
            {
                foreach (var p in PhotonNetwork.PlayerList)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    var pid = GetPlayerPersistentId(p);
                    if (!byPlayer.ContainsKey(pid))
                    {
                        byPlayer[pid] = (string.IsNullOrWhiteSpace(p.NickName) ? pid : p.NickName, 0);
                    }
                }
            }

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Online: {onlineIds.Count}   Players with clips: {byPlayer.Count(kv => kv.Value.count > 0)}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            _scrollPlayers = GUILayout.BeginScrollView(_scrollPlayers, GUILayout.Height(scrollH));

            var sorted = byPlayer
                .OrderByDescending(kv => onlineIds.Contains(kv.Key))
                .ThenBy(kv => kv.Value.name)
                .ToList();

            if (sorted.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  No player data available.", _gsLabel);
                GUI.color = Color.white;
            }

            foreach (var kv in sorted)
            {
                var pid = kv.Key;
                var (pname, count) = kv.Value;
                var isOnline = onlineIds.Contains(pid);
                var isLocal = string.Equals(pid, localId, System.StringComparison.OrdinalIgnoreCase);
                DrawPlayerRow(pid, pname, count, isOnline, isLocal);
                GUILayout.Space(2f);
            }

            GUILayout.EndScrollView();
        }

        private void DrawPlayerRow(string pid, string name, int clipCount, bool isOnline, bool isLocal)
        {
            GUILayout.BeginVertical(_gsPanelBox);
            GUILayout.BeginHorizontal();

            GUI.color = isOnline ? (isLocal ? CYellow : CGreen) : CTextDim;
            GUILayout.Label(isOnline ? "●" : "○", _gsLabel, GUILayout.Width(14f));
            GUI.color = Color.white;

            GUILayout.Label(name + (isLocal ? "  (you)" : string.Empty),
                isLocal ? new GUIStyle(_gsLabel) { fontStyle = FontStyle.Bold } : _gsLabel);
            GUILayout.FlexibleSpace();

            GUI.color = clipCount > 0 ? CAccent : CTextDim;
            GUILayout.Label($"{clipCount} clip{(clipCount != 1 ? "s" : string.Empty)}", _gsSmall, GUILayout.Width(56f));
            GUI.color = Color.white;

            if (!isOnline)
            {
                GUI.color = CTextDim;
                GUILayout.Label("[offline]", _gsSmall, GUILayout.Width(52f));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            GUI.color = CTextDim;
            GUILayout.Label($"  id: {FitHudText(pid, 40)}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.EndVertical();
        }

        // ─── Tab 3: Cache ─────────────────────────────────────────────────────────
        private void DrawCacheTab(float scrollH)
        {
            // ── Header: cache stats + toolbar ────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"Total: {cachedAudio.Count} clips   In-progress: {incomingAudioTransmissions.Count} transmissions   Custom: {_customAudioClips.Count}", _gsSmall);
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUI.color = CAccentDim;
            if (GUILayout.Button("↺ Reload Custom", _gsBtn, GUILayout.Height(22f), GUILayout.ExpandWidth(false)))
            {
                ReloadCustomAudio();
            }
            GUI.color = Color.white;
            GUILayout.Space(4f);
            if (GUILayout.Button("Clear Cache", _gsBtnDanger, GUILayout.Height(22f), GUILayout.ExpandWidth(false)))
            {
                cachedAudio.Clear();
                _cacheExpandedPlayers.Clear();
                DLog($"Debug HUD: cache manually cleared {DebugContext()}");
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            // Build per-player groups (ordered: most clips first)
            var byPlayer = new Dictionary<string, (string name, List<int> indices, float lastAt)>(System.StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cachedAudio.Count; i++)
            {
                var e = cachedAudio[i];
                if (e == null) continue;
                var pid = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : $"actor_{e.SourceActor}";
                var name = !string.IsNullOrWhiteSpace(e.SourceName) ? e.SourceName : pid;
                if (byPlayer.TryGetValue(pid, out var cur))
                {
                    cur.indices.Add(i);
                    byPlayer[pid] = (cur.name, cur.indices, Mathf.Max(cur.lastAt, e.ReceivedAt));
                }
                else
                {
                    byPlayer[pid] = (name, new List<int> { i }, e.ReceivedAt);
                }
            }

            var sorted = byPlayer
                .OrderByDescending(kv => kv.Value.indices.Count)
                .ToList();

            _scrollCache = GUILayout.BeginScrollView(_scrollCache, GUILayout.Height(scrollH));

            // ── Custom Audio section ─────────────────────────────────────────────
            var customClips = _customAudioClips;
            {
                var isExpanded = _cacheExpandedPlayers.Contains("__custom__");
                var chevron = isExpanded ? "▼" : "▶";

                GUILayout.BeginVertical(_gsPanelBox);
                GUILayout.BeginHorizontal();

                GUI.color = new Color(0.55f, 0.85f, 0.40f);
                if (GUILayout.Button(chevron, _gsBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (isExpanded) _cacheExpandedPlayers.Remove("__custom__");
                    else _cacheExpandedPlayers.Add("__custom__");
                    isExpanded = !isExpanded;
                }
                GUI.color = new Color(0.55f, 0.85f, 0.40f);
                GUILayout.Label("Custom Audio", _gsLabel);
                GUI.color = Color.white;
                GUILayout.FlexibleSpace();
                GUI.color = CTextDim;
                GUILayout.Label("custom-audio/", _gsSmall);
                GUI.color = new Color(0.55f, 0.85f, 0.40f);
                GUILayout.Label($"{customClips.Count} file{(customClips.Count != 1 ? "s" : string.Empty)}", _gsSmall, GUILayout.Width(50f));
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                if (isExpanded)
                {
                    GUILayout.Space(2f);
                    var lineColor = new Color(0.22f, 0.36f, 0.24f);
                    var lr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                    GUI.color = lineColor;
                    GUI.DrawTexture(lr, _txWhite);
                    GUI.color = Color.white;
                    GUILayout.Space(2f);

                    if (customClips.Count == 0)
                    {
                        GUI.color = CTextDim;
                        GUILayout.Label("  Drop .mp3 or .wav files into BepInEx/plugins/ToxesFoxes-Mimics/custom-audio/", _gsSmall);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        for (var ci = 0; ci < customClips.Count; ci++)
                        {
                            var ce = customClips[ci];
                            if (ce?.Clip == null) continue;

                            GUILayout.BeginHorizontal();
                            GUILayout.Space(4f);

                            GUI.color = new Color(0.55f, 0.85f, 0.40f);
                            if (GUILayout.Button("▶", _gsBtnYellow, GUILayout.Width(24f), GUILayout.Height(18f)))
                            {
                                PlayCustomAudioEntry(ce);
                            }
                            GUI.color = Color.white;

                            GUILayout.Space(4f);
                            GUI.color = CTextDim;
                            GUILayout.Label($"#{ci + 1:D2}", _gsSmall, GUILayout.Width(30f));
                            GUI.color = CText;
                            GUILayout.Label($"{ce.Clip.length:F2}s", _gsSmall, GUILayout.Width(44f));
                            GUI.color = CTextDim;
                            GUILayout.Label(FitHudText(ce.FileName, 30), _gsSmall);
                            GUI.color = Color.white;
                            GUILayout.EndHorizontal();
                        }
                    }
                    GUILayout.Space(2f);
                }

                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            if (sorted.Count == 0 && customClips.Count == 0)
            {
                GUI.color = CTextDim;
                GUILayout.Label("  Cache is empty.", _gsLabel);
                GUI.color = Color.white;
            }

            foreach (var kv in sorted)
            {
                var pid = kv.Key;
                var (pname, indices, lastAt) = kv.Value;
                var age = Time.time - lastAt;
                var ageStr = age < 60f ? $"{age:F0}s ago" : $"{age / 60f:F1}m ago";
                var isExpanded = _cacheExpandedPlayers.Contains(pid);
                var chevron = isExpanded ? "▼" : "▶";

                // ── Header row ──────────────────────────────────────────────────
                GUILayout.BeginVertical(_gsPanelBox);
                GUILayout.BeginHorizontal();

                // Expand/collapse button — sits outside the panel background edge
                GUI.color = CAccentDim;
                if (GUILayout.Button(chevron, _gsBtn, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (isExpanded) _cacheExpandedPlayers.Remove(pid);
                    else _cacheExpandedPlayers.Add(pid);
                    isExpanded = !isExpanded;
                }
                GUI.color = Color.white;

                GUI.color = CAccent;
                GUILayout.Label(pname, _gsLabel);
                GUI.color = Color.white;
                GUILayout.FlexibleSpace();
                GUI.color = CTextDim;
                GUILayout.Label(ageStr, _gsSmall, GUILayout.Width(58f));
                GUI.color = CYellow;
                GUILayout.Label($"{indices.Count} clip{(indices.Count != 1 ? "s" : string.Empty)}", _gsSmall, GUILayout.Width(50f));
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                // ID row
                GUILayout.BeginHorizontal();
                GUILayout.Space(26f);
                GUI.color = CTextDim;
                GUILayout.Label($"id: {FitHudText(pid, 38)}", _gsSmall);
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                // ── Clip list (expanded) ─────────────────────────────────────────
                if (isExpanded)
                {
                    GUILayout.Space(2f);
                    var lineColor = new Color(0.22f, 0.28f, 0.36f);
                    var lr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                    GUI.color = lineColor;
                    GUI.DrawTexture(lr, _txWhite);
                    GUI.color = Color.white;
                    GUILayout.Space(2f);

                    for (var ci = 0; ci < indices.Count; ci++)
                    {
                        var idx = indices[ci];
                        if (idx < 0 || idx >= cachedAudio.Count) continue;
                        var entry = cachedAudio[idx];
                        if (entry == null) continue;

                        var dur = entry.SampleRate > 0 && entry.AudioData != null
                            ? entry.AudioData.Length / (float)(entry.SampleRate * 2)
                            : 0f;
                        var clipAge = Time.time - entry.ReceivedAt;
                        var clipAgeStr = clipAge < 60f ? $"{clipAge:F0}s" : $"{clipAge / 60f:F1}m";

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(4f);

                        // Play button
                        GUI.color = CYellow;
                        if (GUILayout.Button("▶", _gsBtnYellow, GUILayout.Width(24f), GUILayout.Height(18f)))
                        {
                            PlayCacheEntryOnNearest(entry);
                        }
                        GUI.color = Color.white;

                        GUILayout.Space(4f);
                        // Index
                        GUI.color = CTextDim;
                        GUILayout.Label($"#{ci + 1:D2}", _gsSmall, GUILayout.Width(30f));
                        // Duration
                        GUI.color = CText;
                        GUILayout.Label($"{dur:F2}s", _gsSmall, GUILayout.Width(44f));
                        // Age
                        GUI.color = CTextDim;
                        GUILayout.Label($"{clipAgeStr} ago", _gsSmall, GUILayout.Width(52f));
                        // Bytes
                        var kb = entry.AudioData != null ? entry.AudioData.Length / 1024f : 0f;
                        GUILayout.Label(kb >= 1f ? $"{kb:F1}kb" : $"{entry.AudioData?.Length ?? 0}b", _gsSmall, GUILayout.Width(40f));
                        GUI.color = Color.white;
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Space(2f);
                }

                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            GUILayout.EndScrollView();
        }

        private void PlayCacheEntryOnNearest(CachedAudioEntry entry)
        {
            if (entry == null || entry.AudioData == null || entry.AudioData.Length == 0)
            {
                DLog($"PlayCacheEntryOnNearest: entry empty {DebugContext()}");
                return;
            }

            RefreshHudTargetsSnapshot();

            var nearest = nearestPlaybackCandidatesHud
                .Where(c => c != null)
                .OrderBy(c => c.Distance)
                .FirstOrDefault();

            if (nearest == null)
            {
                DLog($"PlayCacheEntryOnNearest: no enemy targets found {DebugContext()}");
                return;
            }

            DLog($"PlayCacheEntryOnNearest: playing on {nearest.EnemyName} dist={nearest.Distance:F1}m {DebugContext()}");
            PlayReceivedAudioOnTarget(entry, nearest.Enemy, nearest.Target);
        }

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

        // ─── Tab 5: Settings ──────────────────────────────────────────────────────
        private void DrawSettingsTab(float scrollH)
        {
            // Lazy-init text buffers from current config values
            if (_settingVolumeBuf == null) _settingVolumeBuf = (Plugin.configVoiceVolume?.Value ?? 20).ToString();
            if (_settingRadiusBuf == null) _settingRadiusBuf = (Plugin.configPlaybackNearRadius?.Value ?? 12).ToString();
            if (_settingMinDelayBuf == null) _settingMinDelayBuf = (Plugin.configMinDelay?.Value ?? 5).ToString();
            if (_settingMaxDelayBuf == null) _settingMaxDelayBuf = (Plugin.configMaxDelay?.Value ?? 15).ToString();
            if (_settingMaxFilesBuf == null) _settingMaxFilesBuf = (Plugin.configPersistMaxFilesPerPlayer?.Value ?? 100).ToString();
            if (_settingSamplingRateBuf == null) _settingSamplingRateBuf = (Plugin.configSamplingRate?.Value ?? 48000).ToString();
            if (_settingNormalizeBuf == null) _settingNormalizeBuf = (Plugin.configNormalizeTarget?.Value ?? 85).ToString();

            _scrollSettings = GUILayout.BeginScrollView(_scrollSettings, GUILayout.Height(scrollH));

            // ── General ────────────────────────────────────────────────────────
            DrawSettingsSection("General");

            DrawSettingSliderInt("Voice Volume", ref _settingVolumeBuf,
                Plugin.configVoiceVolume, 0, 100, "%");

            DrawSettingSliderInt("Playback Radius", ref _settingRadiusBuf,
                Plugin.configPlaybackNearRadius, 5, 100, "m");

            DrawSettingSliderInt("Min Delay", ref _settingMinDelayBuf,
                Plugin.configMinDelay, 5, 300, "s");

            DrawSettingSliderInt("Max Delay", ref _settingMaxDelayBuf,
                Plugin.configMaxDelay, 10, 600, "s");

            DrawSettingToggle("Hear Yourself", Plugin.configHearYourself);
            DrawSettingToggle("Playback Voice Filter", Plugin.configPlaybackVoiceFilterEnabled);

            DrawSettingSliderInt("Normalize Target", ref _settingNormalizeBuf,
                Plugin.configNormalizeTarget, 0, 100, "% (0=off)");

            GUILayout.Space(4f);

            // ── Persistence ────────────────────────────────────────────────────
            DrawSettingsSection("Persistence");

            DrawSettingToggle("Persist Audio Cache", Plugin.configPersistAudioCache);

            DrawSettingSliderInt("Max Files Per Player", ref _settingMaxFilesBuf,
                Plugin.configPersistMaxFilesPerPlayer, 1, 5000, "");

            GUILayout.Space(4f);

            // ── Experimental ───────────────────────────────────────────────────
            DrawSettingsSection("Experimental");

            DrawSettingSliderInt("Sampling Rate", ref _settingSamplingRateBuf,
                Plugin.configSamplingRate, 16000, 48000, "Hz");

            GUILayout.Space(4f);

            // ── Debug ──────────────────────────────────────────────────────────
            DrawSettingsSection("Debug");

            DrawSettingToggle("Verbose Logging", Plugin.configDebugVerbose);

            GUILayout.Space(8f);

            // Unsaved-changes indicator
            if (_settingsDirty)
            {
                GUI.color = CYellow;
                GUILayout.Label("⚠  Some integer/slider values were not applied. Press Enter or click Apply next to them.", _gsSmall);
                GUI.color = Color.white;
                GUILayout.Space(4f);
            }

            GUILayout.EndScrollView();
        }

        private void DrawSettingsSection(string label)
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUI.color = CAccent;
            GUILayout.Label(label, _gsLabel, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            DrawHRule();
        }

        private void DrawSettingToggle(string label, ConfigEntry<bool> entry)
        {
            if (entry == null) return;
            GUILayout.BeginHorizontal(_gsPanelBox);
            GUI.color = CText;
            GUILayout.Label(label, _gsLabel, GUILayout.Width(200f));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            var before = entry.Value;
            var after = GUILayout.Toggle(before, before ? "  ON" : "  OFF", _gsLabel, GUILayout.ExpandWidth(false));
            if (after != before)
            {
                entry.Value = after;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        private void DrawSettingSliderInt(string label, ref string textBuf, ConfigEntry<int> entry, int min, int max, string unit)
        {
            if (entry == null) return;
            var current = entry.Value;

            GUILayout.BeginVertical(_gsPanelBox);

            // Row: label + text field + apply button
            GUILayout.BeginHorizontal();
            GUI.color = CText;
            GUILayout.Label(label, _gsLabel, GUILayout.Width(200f));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUI.SetNextControlName("sf_" + label);
            textBuf = GUILayout.TextField(textBuf, 6, _gsLabel, GUILayout.Width(60f));
            if (!string.IsNullOrEmpty(unit))
            {
                GUI.color = CTextDim;
                GUILayout.Label(unit, _gsSmall, GUILayout.ExpandWidth(false));
                GUI.color = Color.white;
            }
            GUILayout.Space(4f);
            if (GUILayout.Button("Apply", _gsBtn, GUILayout.Width(46f), GUILayout.Height(18f)))
            {
                TryApplyIntField(textBuf, entry, min, max);
                _settingsDirty = false;
            }
            GUILayout.EndHorizontal();

            // Slider row
            GUILayout.Space(2f);
            var sliderVal = (float)current;
            var newSlider = GUILayout.HorizontalSlider(sliderVal, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(12f));
            var newInt = Mathf.RoundToInt(newSlider);
            if (newInt != current)
            {
                entry.Value = Mathf.Clamp(newInt, min, max);
                textBuf = entry.Value.ToString();
            }

            // Range hint
            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label(min.ToString(), _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUI.color = CText;
            GUILayout.Label($"Current: {current}{(string.IsNullOrEmpty(unit) ? "" : " " + unit)}", _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUI.color = CTextDim;
            GUILayout.Label(max.ToString(), _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.EndVertical();
            GUILayout.Space(3f);
        }

        private static void TryApplyIntField(string text, ConfigEntry<int> entry, int min, int max)
        {
            if (int.TryParse(text, out var v))
            {
                entry.Value = Mathf.Clamp(v, min, max);
            }
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
                    var dotColor = p.IsOnline ? (p.IsLocalPlayer ? CYellow : CGreen) : CTextDim;
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
            source.volume = Mathf.Clamp01(Plugin.configVoiceVolume.Value / 20f);
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

        // ─── Style Init ───────────────────────────────────────────────────────────
        private void EnsureGuiStyles()
        {
            if (_guiStylesInit)
            {
                return;
            }

            _guiStylesInit = true;

            _txWhite = MakeTex(1, 1, Color.white);
            _txBgDark = MakeTex(1, 1, CBgDark);
            _txBgPanel = MakeTex(1, 1, CBgPanel);
            _txBgHover = MakeTex(1, 1, CBgHover);
            _txAccent = MakeTex(1, 1, CAccent);
            _txAccentDim = MakeTex(1, 1, CAccentDim);
            _txGreen = MakeTex(1, 1, CGreen);
            _txRed = MakeTex(1, 1, CRed);
            _txYellow = MakeTex(1, 1, CYellow);

            _gsWindow = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 10, 10),
                normal = { background = _txBgDark, textColor = CText },
                onNormal = { background = _txBgDark, textColor = CText }
            };

            _gsH1 = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = CText },
                padding = new RectOffset(0, 0, 2, 2)
            };

            _gsLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = CText },
                padding = new RectOffset(2, 2, 1, 1)
            };

            _gsLabelDim = new GUIStyle(_gsLabel)
            {
                normal = { textColor = CTextDim }
            };

            _gsSmall = new GUIStyle(_gsLabel)
            {
                fontSize = 10,
                normal = { textColor = CTextDim }
            };

            _gsBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                padding = new RectOffset(8, 8, 4, 4),
                normal = { background = _txBgPanel, textColor = CText },
                hover = { background = _txBgHover, textColor = Color.white },
                active = { background = _txAccent, textColor = Color.white }
            };

            _gsBtnPrimary = new GUIStyle(_gsBtn)
            {
                normal = { background = _txAccentDim, textColor = Color.white },
                hover = { background = _txAccent, textColor = Color.white }
            };

            _gsBtnDanger = new GUIStyle(_gsBtn)
            {
                normal = { background = MakeTex(1, 1, new Color(0.40f, 0.08f, 0.08f)), textColor = CRed },
                hover = { background = _txRed, textColor = Color.white }
            };

            _gsBtnYellow = new GUIStyle(_gsBtn)
            {
                normal = { background = MakeTex(1, 1, new Color(0.30f, 0.24f, 0.04f)), textColor = CYellow },
                hover = { background = _txYellow, textColor = new Color(0.08f, 0.06f, 0f) }
            };

            _gsTabBtn = new GUIStyle(_gsBtn)
            {
                normal = { background = _txBgPanel, textColor = CTextDim },
                hover = { background = _txBgHover, textColor = CText }
            };

            _gsTabBtnActive = new GUIStyle(_gsTabBtn)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = _txAccentDim, textColor = Color.white }
            };

            var listBg = MakeTex(1, 1, new Color(0f, 0f, 0f, 0f));
            _gsListItem = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 4, 4),
                normal = { background = listBg, textColor = CText },
                hover = { background = _txBgHover, textColor = Color.white },
                active = { background = _txAccentDim, textColor = Color.white }
            };

            _gsListItemSel = new GUIStyle(_gsListItem)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = _txAccentDim, textColor = Color.white },
                hover = { background = _txAccent, textColor = Color.white }
            };

            _gsModalTitle = new GUIStyle(_gsH1)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = CAccent }
            };

            _gsPanelBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 5, 5),
                normal = { background = _txBgPanel, textColor = CText }
            };
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (var i = 0; i < pix.Length; i++)
            {
                pix[i] = col;
            }

            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
    }
}
