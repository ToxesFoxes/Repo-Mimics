using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Window State ────────────────────────────────────────────────────────
        private bool _debugWindowOpen;
        private bool _showGizmos;
        private Rect _debugWindowRect = new Rect(20f, 20f, 520f, 500f);
        private int _debugTab;
        private Vector2 _scrollMobs, _scrollPlayers, _scrollCache, _scrollVoiceLog, _scrollVoiceLogOut, _scrollOverlays;
        private const int VoiceLogMaxEntries = 200;
        private readonly List<VoiceLogEntry> _voiceLog = new List<VoiceLogEntry>();
        private readonly HashSet<string> _cacheExpandedPlayers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private bool _isResizing;
        private Vector2 _resizeMouseStart;
        private Rect _resizeRectStart;

        // ─── Enemy Overlay Manager ────────────────────────────────────────────────
        private readonly Dictionary<int, MimicsEnemyOverlay> _enemyOverlays = new Dictionary<int, MimicsEnemyOverlay>();
        private readonly Dictionary<int, MimicsAudioMarker> _audioMarkers = new Dictionary<int, MimicsAudioMarker>();
        private float _overlayNextRefresh;

        private static readonly string[] TabNames = { "Mobs", "Players", "Cache", "Voice Log", "Overlays", "Settings" };

        // ─── Settings Tab State ───────────────────────────────────────────────────
        private string _settingVolumeBuf;
        private string _settingRadiusBuf;
        private string _settingMinDelayBuf;
        private string _settingMaxDelayBuf;
        private string _settingMaxFilesBuf;
        private string _settingSamplingRateBuf;
        private string _settingNormalizeBuf;
        private string _settingHostIntervalBuf;
        private bool _settingsDirty;
        private Vector2 _scrollSettings;
        private Vector2 _scrollReadiness;
        // Per-player volume slider text buffers (keyed by persistent player ID)
        private readonly Dictionary<string, string> _playerVolBuf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            public bool IsFailed;
            public float ReceivedAt;
            public float UpdatedAt;
            public int ChunksDone;
            public int ChunksTotal;
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
                    // Shift+F8 — переключить фокус (вернуть курсор игре), окно остаётся видимым
                    _debugWindowFocused = !_debugWindowFocused;
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

        private void DrawMainWindow(int id)
        {
            DrawWindowTitleBar("MIMICS  DEBUG", "[F8]");
            GUILayout.Space(4f);
            DrawPlaybackHeader();
            GUILayout.Space(4f);
            DrawTabRow();
            GUILayout.Space(4f);

            var scrollH = Mathf.Max(100f, _debugWindowRect.height - 168f);
            switch (_debugTab)
            {
                case 0: DrawNearestMobsTab(scrollH); break;
                case 1: DrawPlayersTab(scrollH); break;
                case 2: DrawCacheTab(scrollH); break;
                case 3: DrawVoiceLogTab(scrollH); break;
                case 4: DrawOverlaysTab(scrollH); break;
                case 5: DrawSettingsTab(scrollH); break;
            }

            GUI.DragWindow(new Rect(0f, 0f, _debugWindowRect.width - 28f, 26f));
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
