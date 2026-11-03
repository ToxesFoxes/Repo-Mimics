using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Window State ────────────────────────────────────────────────────────
        private bool _debugWindowOpen;
        private bool _debugWindowCollapsed;
        private bool _showGizmos;
        private Rect _debugWindowRect = new Rect(20f, 20f, 640f, 520f);
        private float _debugWindowExpandedHeight = 520f;
        private int _debugTab;
        private Vector2 _scrollMobs, _scrollPlayers, _scrollRecordings, _scrollVoiceLog, _scrollVoiceLogOut, _scrollOverlays;
        private const int VoiceLogMaxEntries = 200;
        private readonly List<VoiceLogEntry> _voiceLog = new List<VoiceLogEntry>();

        private bool _isResizing;
        private Vector2 _resizeMouseStart;
        private Rect _resizeRectStart;

        private const float MinWindowWidth = 560f;
        private const float MinWindowHeight = 360f;
        private const float SidebarWidth = 112f;

        // ─── Enemy Overlay Manager ────────────────────────────────────────────────
        private readonly Dictionary<int, MimicsEnemyOverlay> _enemyOverlays = new Dictionary<int, MimicsEnemyOverlay>();
        private readonly Dictionary<int, MimicsAudioMarker> _audioMarkers = new Dictionary<int, MimicsAudioMarker>();
        private float _overlayNextRefresh;

        private const int TabRecordings = 0;
        private const int TabMobs = 1;
        private const int TabPlayers = 2;
        private const int TabNetwork = 3;
        private const int TabOverlays = 4;
        private const int TabSettings = 5;
        private static readonly string[] TabNames = { "Recordings", "Mobs", "Players", "Network", "Overlays", "Settings" };

        // ─── Settings Tab State ───────────────────────────────────────────────────
        private string _settingVolumeBuf;
        private string _settingRadiusBuf;
        private string _settingMinDelayBuf;
        private string _settingMaxDelayBuf;
        private string _settingMaxFilesBuf;
        private string _settingSamplingRateBuf;
        private string _settingNormalizeBuf;
        private string _settingVadSensitivityBuf;
        private bool _settingsDirty;
        private Vector2 _scrollSettings;
        private Vector2 _scrollReadiness;
        // Per-player volume slider text buffers (keyed by persistent player ID)
        private readonly Dictionary<string, string> _playerVolBuf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ─── Tab Data Caches ──────────────────────────────────────────────────────
        private float _tabCacheNextRebuild;
        private const float TabCacheInterval = 0.25f;

        // Players tab
        private readonly List<(string pid, string name, int count)> _cachedPlayersSorted = new List<(string pid, string name, int count)>();
        private readonly HashSet<string> _cachedPlayersOnlineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _cachedPlayersLocalId = string.Empty;
        private int _cachedPlayersWithClips;
        private int _playersPage;

        // VoiceLog (Network tab)
        private readonly List<VoiceLogEntry> _cachedVoiceLogIn = new List<VoiceLogEntry>();
        private readonly List<VoiceLogEntry> _cachedVoiceLogOut = new List<VoiceLogEntry>();
        private int _cachedVoiceLogInProgress;

        // Readiness (Network tab)
        private int _cachedReadinessEligibleCount;
        private readonly Dictionary<string, bool> _cachedReadinessEligible = new Dictionary<string, bool>();

        // ─── Force-Play Modal State ──────────────────────────────────────────────
        private bool _fpmOpen;
        private int _fpmPage;       // 0 = Player, 1 = Clip, 2 = Enemy
        private int _fpmPlayerIdx;  // -1 = Random
        private int _fpmClipIdx;    // -1 = Random within player
        private int _fpmEnemyIdx;   // -1 = Nearest
        private int _fpmFilterIdx = -1; // -1 = Random/Default
        private bool _fpmHearYourself;
        private Vector2 _fpmScrollPlayer, _fpmScrollClip, _fpmScrollEnemy, _fpmScrollFilter;
        private int _fpmPagePlayer, _fpmPageClip, _fpmPageEnemy, _fpmPageFilter;
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
            public bool IsFailed;
            public float ReceivedAt;
            public float UpdatedAt;
            public int ChunksDone;
            public int ChunksTotal;
        }

        // ─── GUI Styles & Textures ───────────────────────────────────────────────
        private bool _guiStylesInit;
        private GUIStyle _gsWindow, _gsH1, _gsLabel, _gsLabelDim, _gsSmall, _gsTitle;
        private GUIStyle _gsBtn, _gsBtnPrimary, _gsBtnDanger, _gsBtnYellow, _gsIconBtn;
        private GUIStyle _gsTabBtn, _gsTabBtnActive, _gsNavItem, _gsNavItemActive, _gsChip;
        private GUIStyle _gsListItem, _gsListItemSel;
        private GUIStyle _gsPanelBox, _gsModalTitle, _gsRow, _gsRowAlt;

        private Texture2D _txWhite, _txBgDark, _txBgPanel, _txBgHover;
        private Texture2D _txAccent, _txAccentDim, _txGreen, _txRed, _txYellow;

        private static readonly Color CAccent = new Color(0.29f, 0.67f, 1.00f);
        private static readonly Color CAccentDim = new Color(0.16f, 0.38f, 0.62f);
        private static readonly Color CGreen = new Color(0.27f, 0.87f, 0.42f);
        private static readonly Color CRed = new Color(1.00f, 0.33f, 0.33f);
        private static readonly Color CYellow = new Color(1.00f, 0.84f, 0.20f);
        private static readonly Color CCustom = new Color(0.55f, 0.85f, 0.40f);
        private static readonly Color CText = new Color(0.90f, 0.92f, 0.94f);
        private static readonly Color CTextDim = new Color(0.50f, 0.54f, 0.58f);
        private static readonly Color CBgDark = new Color(0.07f, 0.09f, 0.11f, 0.97f);
        private static readonly Color CBgPanel = new Color(0.11f, 0.14f, 0.17f, 1.00f);
        private static readonly Color CBgHover = new Color(0.18f, 0.22f, 0.27f, 1.00f);
        private static readonly Color CBgTrack = new Color(0.15f, 0.19f, 0.24f);
        private static readonly Color CLine = new Color(0.17f, 0.21f, 0.26f);

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

        // The menu works in levels, the shop and the truck lobby; enemy-related data only exists in levels.
        private static bool IsDebugGuiAllowedHere()
        {
            return SemiFunc.RunIsLevel() || SemiFunc.RunIsShop() || SemiFunc.RunIsLobby();
        }

        // ─── Update Hook ─────────────────────────────────────────────────────────
        private bool _debugWindowFocused = true;
        private ConfigEntry<string> _keybindListeningEntry;

        private void DebugGuiUpdate()
        {
            EnsureWindowStateLoaded();

            var menuKey = Plugin.GetDebugMenuKey();
            if (_keybindListeningEntry == null && Input.GetKeyDown(menuKey))
            {
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift && _debugWindowOpen)
                {
                    // Shift+Key — переключить фокус (вернуть курсор игре), окно остаётся видимым
                    _debugWindowFocused = !_debugWindowFocused;
                }
                else
                {
                    // Key — переключить окно
                    _debugWindowOpen = !_debugWindowOpen;
                    _debugWindowFocused = _debugWindowOpen;
                    if (_debugWindowOpen)
                    {
                        hudNextRefreshAt = 0f;
                    }
                    else
                    {
                        _fpmOpen = false;
                        StopRecordingPreview();
                    }
                }
            }

            // Окно закрыто или ушли с вкладки Settings — отменить ожидание клавиши
            if (!_debugWindowOpen || !_debugWindowFocused || _debugTab != TabSettings)
            {
                _keybindListeningEntry = null;
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

            SaveWindowStateIfChanged();

            if (!_debugWindowOpen)
            {
                return;
            }

            var inLevel = SemiFunc.RunIsLevel();
            if (inLevel && Time.time >= hudNextRefreshAt)
            {
                hudNextRefreshAt = Time.time + 0.25f;
                RefreshHudTargetsSnapshot();
            }

            if (_debugTab == TabPlayers)
            {
                RebuildAvatarMapIfNeeded();
            }

            if (Time.time >= _tabCacheNextRebuild)
            {
                _tabCacheNextRebuild = Time.time + TabCacheInterval;
                RebuildActiveTabCache();
            }

            if (inLevel && _showGizmos)
            {
                RefreshEnemyOverlays();
            }
        }

        // ─── OnGUI ───────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (photonView == null || !photonView.IsMine || !IsDebugGuiAllowedHere())
            {
                return;
            }

            EnsureGuiStyles();

            if (!_debugWindowOpen)
            {
                return;
            }

            if (_showGizmos && SemiFunc.RunIsLevel())
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

            ClampWindowToScreen();
            if (_debugWindowCollapsed)
            {
                // Collapsed: title bar + status strip only.
                _debugWindowRect.height = GetBodyTopOffset() - 2f;
            }
            _debugWindowRect = GUI.Window(9900, _debugWindowRect, DrawMainWindow, string.Empty, _gsWindow);

            if (_debugWindowCollapsed)
            {
                return;
            }

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

        // ─── Resize + Main Window ─────────────────────────────────────────────────
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
                    _debugWindowRect.width = Mathf.Max(MinWindowWidth, _resizeRectStart.width + delta.x);
                    _debugWindowRect.height = Mathf.Max(MinWindowHeight, _resizeRectStart.height + delta.y);
                    _debugWindowExpandedHeight = _debugWindowRect.height;
                    ev.Use();
                }

                if (ev.type == EventType.MouseUp)
                {
                    _isResizing = false;
                    ev.Use();
                }
            }
        }

        private void ClampWindowToScreen()
        {
            _debugWindowRect.width = Mathf.Min(_debugWindowRect.width, Mathf.Max(MinWindowWidth, Screen.width));
            _debugWindowRect.x = Mathf.Clamp(_debugWindowRect.x, 0f, Mathf.Max(0f, Screen.width - _debugWindowRect.width));
            _debugWindowRect.y = Mathf.Clamp(_debugWindowRect.y, 0f, Mathf.Max(0f, Screen.height - 40f));
        }

        private void SetWindowCollapsed(bool collapsed)
        {
            if (collapsed == _debugWindowCollapsed)
            {
                return;
            }

            _debugWindowCollapsed = collapsed;
            if (collapsed)
            {
                _debugWindowExpandedHeight = _debugWindowRect.height;
                StopRecordingPreview();
            }
            else
            {
                _debugWindowRect.height = Mathf.Max(MinWindowHeight, _debugWindowExpandedHeight);
            }
        }

        private void DrawMainWindow(int id)
        {
            DrawWindowTitleBar();
            GUILayout.Space(4f);
            DrawStatusStrip();

            if (_debugWindowCollapsed)
            {
                GUI.DragWindow(new Rect(0f, 0f, _debugWindowRect.width - 60f, 28f));
                return;
            }

            GUILayout.Space(6f);

            var bodyH = Mathf.Max(160f, _debugWindowRect.height - GetBodyTopOffset() - 12f);
            GUILayout.BeginHorizontal(GUILayout.Height(bodyH));

            DrawSidebar(bodyH);

            // Vertical divider
            var divider = GUILayoutUtility.GetRect(1f, bodyH, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            GUI.color = CLine;
            GUI.DrawTexture(divider, _txWhite);
            GUI.color = Color.white;
            GUILayout.Space(8f);

            GUILayout.BeginVertical();
            var scrollH = Mathf.Max(100f, bodyH - 34f);
            switch (_debugTab)
            {
                case TabRecordings: DrawRecordingsTab(scrollH); break;
                case TabMobs: DrawNearestMobsTab(scrollH); break;
                case TabPlayers: DrawPlayersTab(scrollH); break;
                case TabNetwork: DrawNetworkTab(scrollH); break;
                case TabOverlays: DrawOverlaysTab(scrollH); break;
                case TabSettings: DrawSettingsTab(scrollH); break;
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, _debugWindowRect.width - 60f, 28f));
        }

        // Height consumed by the title bar and status strip (status grows with concurrent playbacks).
        private float GetBodyTopOffset()
        {
            var extraPlaybackRows = Mathf.Clamp(CountActivePlaybacks() - 1, 0, MaxExtraPlaybackRows + 1);
            return 116f + extraPlaybackRows * 16f;
        }

        private void DrawSidebar(float height)
        {
            GUILayout.BeginVertical(GUILayout.Width(SidebarWidth), GUILayout.Height(height));
            for (var i = 0; i < TabNames.Length; i++)
            {
                var active = _debugTab == i;
                var rect = GUILayoutUtility.GetRect(new GUIContent(TabNames[i]), active ? _gsNavItemActive : _gsNavItem,
                    GUILayout.Width(SidebarWidth), GUILayout.Height(26f));
                if (GUI.Button(rect, TabNames[i] + GetTabBadge(i), active ? _gsNavItemActive : _gsNavItem))
                {
                    if (_debugTab != i)
                    {
                        _debugTab = i;
                        _tabCacheNextRebuild = 0f;
                    }
                }

                if (active)
                {
                    GUI.color = CAccent;
                    GUI.DrawTexture(new Rect(rect.x, rect.y + 4f, 2f, rect.height - 8f), _txWhite);
                    GUI.color = Color.white;
                }

                GUILayout.Space(2f);
            }

            GUILayout.FlexibleSpace();

            GUI.enabled = SemiFunc.RunIsLevel();
            if (GUILayout.Button("▶  Force play", _gsBtnYellow, GUILayout.Width(SidebarWidth), GUILayout.Height(26f)))
            {
                OpenForcePlayModal();
            }
            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private string GetTabBadge(int tab)
        {
            switch (tab)
            {
                case TabRecordings: return cachedAudio.Count + _customAudioClips.Count > 0 ? $"  {cachedAudio.Count + _customAudioClips.Count}" : string.Empty;
                case TabMobs: return nearestPlaybackCandidatesHud.Count > 0 && SemiFunc.RunIsLevel() ? $"  {nearestPlaybackCandidatesHud.Count}" : string.Empty;
                case TabNetwork: return incomingAudioTransmissions.Count > 0 ? $"  ●{incomingAudioTransmissions.Count}" : string.Empty;
                default: return string.Empty;
            }
        }

        // ─── Window state persistence ─────────────────────────────────────────────
        // BepInEx/config/TFS_Mimics.window.txt: x;y;w;h;tab;collapsed;expandedHeight
        private bool _windowStateLoaded;
        private string _windowStateSaved;
        private float _windowStateNextSaveAt;

        private static string GetWindowStatePath()
        {
            return Path.Combine(Paths.ConfigPath, "TFS_Mimics.window.txt");
        }

        private void EnsureWindowStateLoaded()
        {
            if (_windowStateLoaded)
            {
                return;
            }

            _windowStateLoaded = true;
            try
            {
                var path = GetWindowStatePath();
                if (!File.Exists(path))
                {
                    return;
                }

                var parts = File.ReadAllText(path).Trim().Split(';');
                if (parts.Length < 7)
                {
                    return;
                }

                var inv = CultureInfo.InvariantCulture;
                _debugWindowRect = new Rect(
                    float.Parse(parts[0], inv), float.Parse(parts[1], inv),
                    Mathf.Max(MinWindowWidth, float.Parse(parts[2], inv)),
                    Mathf.Max(MinWindowHeight, float.Parse(parts[3], inv)));
                _debugTab = Mathf.Clamp(int.Parse(parts[4], inv), 0, TabNames.Length - 1);
                _debugWindowCollapsed = parts[5] == "1";
                _debugWindowExpandedHeight = Mathf.Max(MinWindowHeight, float.Parse(parts[6], inv));
                _windowStateSaved = SerializeWindowState();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to load debug window state: {ex.Message}");
            }
        }

        private string SerializeWindowState()
        {
            var inv = CultureInfo.InvariantCulture;
            var height = _debugWindowCollapsed ? _debugWindowExpandedHeight : _debugWindowRect.height;
            return string.Join(";",
                Mathf.Round(_debugWindowRect.x).ToString(inv),
                Mathf.Round(_debugWindowRect.y).ToString(inv),
                Mathf.Round(_debugWindowRect.width).ToString(inv),
                Mathf.Round(height).ToString(inv),
                _debugTab.ToString(inv),
                _debugWindowCollapsed ? "1" : "0",
                Mathf.Round(_debugWindowExpandedHeight).ToString(inv));
        }

        private void SaveWindowStateIfChanged()
        {
            if (!_windowStateLoaded || _isResizing || Time.unscaledTime < _windowStateNextSaveAt)
            {
                return;
            }

            _windowStateNextSaveAt = Time.unscaledTime + 1f;
            var state = SerializeWindowState();
            if (state == _windowStateSaved)
            {
                return;
            }

            try
            {
                File.WriteAllText(GetWindowStatePath(), state);
                _windowStateSaved = state;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to save debug window state: {ex.Message}");
                _windowStateNextSaveAt = Time.unscaledTime + 30f;
            }
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
            var txClear = MakeTex(1, 1, new Color(0f, 0f, 0f, 0f));
            var txRowAlt = MakeTex(1, 1, new Color(1f, 1f, 1f, 0.025f));
            var txNavActive = MakeTex(1, 1, new Color(0.16f, 0.38f, 0.62f, 0.45f));

            _gsWindow = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 8, 10),
                normal = { background = _txBgDark, textColor = CText },
                onNormal = { background = _txBgDark, textColor = CText }
            };

            _gsTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = CAccent },
                padding = new RectOffset(0, 0, 2, 2)
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

            _gsIconBtn = new GUIStyle(_gsBtn)
            {
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
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

            _gsNavItem = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 6, 4, 4),
                normal = { background = txClear, textColor = CTextDim },
                hover = { background = _txBgHover, textColor = CText },
                active = { background = _txAccentDim, textColor = Color.white }
            };

            _gsNavItemActive = new GUIStyle(_gsNavItem)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = txNavActive, textColor = Color.white },
                hover = { background = txNavActive, textColor = Color.white }
            };

            _gsChip = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2),
                margin = new RectOffset(2, 2, 3, 3),
                normal = { background = _txBgPanel, textColor = CTextDim }
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

            _gsRow = new GUIStyle(GUIStyle.none)
            {
                padding = new RectOffset(4, 4, 3, 3),
                normal = { background = txClear }
            };

            _gsRowAlt = new GUIStyle(_gsRow)
            {
                normal = { background = txRowAlt }
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
