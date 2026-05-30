using UnityEngine;
using UnityEngine.UI;

namespace TFS_Mimics
{
    /// <summary>
    /// Global display settings for all MimicsEnemyOverlay instances.
    /// Toggled from the DebugGui "Overlays" tab.
    /// </summary>
    internal static class OverlaySettings
    {
        public static bool ShowHp = true;
        public static bool ShowState = true;
        public static bool ShowDistance = true;
        public static bool ShowPlaying = true;
    }

    /// <summary>
    /// World-space billboard panel that floats above an enemy and shows name, HP bar,
    /// current AI state, distance and a "PLAYING" indicator.
    /// Instantiated and managed by the DebugGui overlay manager.
    /// </summary>
    internal sealed class MimicsEnemyOverlay : MonoBehaviour
    {
        // ─── Shared resources ────────────────────────────────────────────────────
        private static Font s_font;
        private static Sprite s_whiteSprite;

        private static Font GetFont()
        {
            if (s_font == null)
                s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return s_font;
        }

        private static Sprite GetSprite()
        {
            if (s_whiteSprite != null) return s_whiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            s_whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
            return s_whiteSprite;
        }

        // ─── Colors (mirror DebugGui palette) ────────────────────────────────────
        private static readonly Color CAccent = new Color(0.35f, 0.65f, 1.00f);
        private static readonly Color CBgDark = new Color(0.06f, 0.08f, 0.12f, 0.88f);
        private static readonly Color CBgPanel = new Color(0.10f, 0.13f, 0.18f, 0.72f);
        private static readonly Color CGreen = new Color(0.30f, 0.85f, 0.40f);
        private static readonly Color CYellow = new Color(1.00f, 0.80f, 0.20f);
        private static readonly Color CRed = new Color(0.90f, 0.25f, 0.25f);
        private static readonly Color CText = new Color(0.90f, 0.92f, 0.95f);
        private static readonly Color CDim = new Color(0.55f, 0.60f, 0.65f);

        // ─── Layout constants ────────────────────────────────────────────────────
        private const float PanelW = 160f;
        private const float PanelH = 82f;
        private const float WorldScale = 0.012f;   // world-unit size: ~1.92 × 0.98 m
        private const float HeadOffset = 0.8f;     // metres above CenterTransform
        private const float DataHz = 0.2f;     // data refresh interval

        // ─── Runtime state ───────────────────────────────────────────────────────
        private EnemyParent _enemyParent;
        private Transform _playerTransform;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Image _hpFill;
        private Text _nameText;
        private Text _hpText;
        private Text _stateText;
        private Text _distText;
        private Image _playingBg;
        private Text _playingText;

        private float _nextRefresh;
        private float _smoothHp = 1f;

        public bool IsPlayingAudio { get; set; }

        // ─── Public init ─────────────────────────────────────────────────────────
        public void Init(EnemyParent enemyParent, Transform playerTransform)
        {
            _enemyParent = enemyParent;
            _playerTransform = playerTransform;
            BuildUI();
        }

        // ─── Unity lifecycle ─────────────────────────────────────────────────────
        private void Update()
        {
            if (_enemyParent == null)
            {
                gameObject.SetActive(false);
                return;
            }

            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + DataHz;
            RefreshData();
        }

        private Transform GetAnchor()
        {
            if (_enemyParent == null || _enemyParent.Enemy == null) return null;
            var e = _enemyParent.Enemy;
            if (e.CenterTransform != null) return e.CenterTransform;
            if (e.HasVision && e.Vision != null && e.Vision.VisionTransform != null) return e.Vision.VisionTransform;
            return e.transform;
        }

        private void LateUpdate()
        {
            if (_canvas == null || _enemyParent == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            var anchor = GetAnchor();
            if (anchor == null) return;

            // Place panel above CenterTransform (like Imperium HP bar)
            _canvasRect.position = anchor.position + Vector3.up * HeadOffset;

            // Billboard: keep panel parallel to the camera's view plane
            _canvasRect.rotation = cam.transform.rotation;
        }

        public void SetVisible(bool visible)
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(visible);
        }

        // ─── UI construction ─────────────────────────────────────────────────────
        private void BuildUI()
        {
            var go = new GameObject("_MimicsOverlayCanvas");
            go.transform.SetParent(transform, false);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 100;

            _canvasRect = go.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(PanelW, PanelH);
            _canvasRect.localScale = Vector3.one * WorldScale;

            // CanvasScaler: raise dynamicPixelsPerUnit so text is sharp
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            // Background
            MkImg(go.transform, "bg", CBgDark, 0f, 1f, 0f, 1f);

            // Border accent line at top
            MkImg(go.transform, "border_top", CAccent, 0f, 1f, 0.965f, 1f);

            // ── Name row (top 22%) ────────────────────────────────────────────
            _nameText = MkText(go.transform, "name", 12, FontStyle.Bold, CAccent);
            SetAnch(_nameText.rectTransform, 0.05f, 0.85f, 0.78f, 1f);
            _nameText.alignment = TextAnchor.MiddleLeft;

            // ── HP bar background (53–72%) ────────────────────────────────────
            var hpBg = MkImg(go.transform, "hp_bg", new Color(0.08f, 0.10f, 0.15f), 0.05f, 0.95f, 0.53f, 0.73f);

            // HP bar fill (inside hpBg, uses Image.fillAmount)
            var hpFillGo = new GameObject("hp_fill");
            hpFillGo.transform.SetParent(hpBg.transform, false);
            _hpFill = hpFillGo.AddComponent<Image>();
            _hpFill.sprite = GetSprite();
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillAmount = 1f;
            _hpFill.color = CGreen;
            var hfr = hpFillGo.GetComponent<RectTransform>();
            hfr.anchorMin = Vector2.zero;
            hfr.anchorMax = Vector2.one;
            hfr.offsetMin = hfr.offsetMax = Vector2.zero;

            // HP text (centred over bar)
            _hpText = MkText(go.transform, "hp_text", 9, FontStyle.Normal, CText);
            SetAnch(_hpText.rectTransform, 0.05f, 0.95f, 0.53f, 0.73f);
            _hpText.alignment = TextAnchor.MiddleCenter;

            // ── State (left, 29–51%) ──────────────────────────────────────────
            _stateText = MkText(go.transform, "state", 9, FontStyle.Normal, CDim);
            SetAnch(_stateText.rectTransform, 0.05f, 0.58f, 0.29f, 0.51f);
            _stateText.alignment = TextAnchor.MiddleLeft;

            // ── Distance (right, 29–51%) ──────────────────────────────────────
            _distText = MkText(go.transform, "dist", 9, FontStyle.Normal, CDim);
            SetAnch(_distText.rectTransform, 0.58f, 0.95f, 0.29f, 0.51f);
            _distText.alignment = TextAnchor.MiddleRight;

            // ── Playing strip (bottom 26%) ────────────────────────────────────
            _playingBg = MkImg(go.transform, "playing_bg", new Color(0f, 0f, 0f, 0f), 0f, 1f, 0f, 0.26f);

            _playingText = MkText(go.transform, "playing_text", 10, FontStyle.Bold, new Color(0f, 0f, 0f, 0f));
            SetAnch(_playingText.rectTransform, 0.04f, 0.96f, 0f, 0.26f);
            _playingText.text = "\u25b6  MIMICS PLAYING";
            _playingText.alignment = TextAnchor.MiddleCenter;
        }

        // ─── Data refresh ─────────────────────────────────────────────────────────
        private void RefreshData()
        {
            if (_enemyParent == null) return;

            // Name
            var n = _enemyParent.enemyName;
            if (string.IsNullOrWhiteSpace(n)) n = _enemyParent.gameObject.name;
            if (_nameText != null) _nameText.text = n;

            // HP
            TryGetHealth(out var hpCur, out var hpMax);
            var showHp = OverlaySettings.ShowHp;
            if (_hpFill != null)
            {
                _hpFill.transform.parent.gameObject.SetActive(showHp);
                if (showHp)
                {
                    if (hpMax > 0)
                    {
                        var t = Mathf.Clamp01((float)hpCur / hpMax);
                        _smoothHp = Mathf.Lerp(_smoothHp, t, 0.35f);
                        _hpFill.fillAmount = _smoothHp;
                        // Green → Yellow → Red gradient
                        _hpFill.color = _smoothHp > 0.5f
                            ? Color.Lerp(CYellow, CGreen, (_smoothHp - 0.5f) * 2f)
                            : Color.Lerp(CRed, CYellow, _smoothHp * 2f);
                    }
                    else
                    {
                        _hpFill.fillAmount = 1f;
                        _hpFill.color = CDim;
                    }
                }
            }
            if (_hpText != null)
            {
                _hpText.gameObject.SetActive(showHp);
                if (showHp) _hpText.text = hpMax > 0 ? $"{hpCur} / {hpMax} HP" : "HP: —";
            }

            // State
            if (_stateText != null)
            {
                _stateText.gameObject.SetActive(OverlaySettings.ShowState);
                if (OverlaySettings.ShowState)
                    _stateText.text = _enemyParent.Enemy != null ? _enemyParent.Enemy.CurrentState.ToString() : "—";
            }

            // Distance (from actual enemy body position)
            if (_distText != null)
            {
                _distText.gameObject.SetActive(OverlaySettings.ShowDistance);
                if (OverlaySettings.ShowDistance && _playerTransform != null)
                {
                    var anchor = GetAnchor();
                    var pos = anchor != null ? anchor.position : _enemyParent.transform.position;
                    _distText.text = $"{Vector3.Distance(pos, _playerTransform.position):F1} m";
                }
            }

            // Playing indicator
            if (_playingBg != null && _playingText != null)
            {
                var show = OverlaySettings.ShowPlaying && IsPlayingAudio;
                _playingBg.color = show ? new Color(CGreen.r, CGreen.g, CGreen.b, 0.18f) : new Color(0f, 0f, 0f, 0f);
                _playingText.color = show ? CGreen : new Color(0f, 0f, 0f, 0f);
            }
        }

        // ─── Health / State ────────────────────────────────────────────────────
        private void TryGetHealth(out int current, out int max)
        {
            current = max = -1;
            if (_enemyParent == null || _enemyParent.Enemy == null) return;
            var health = _enemyParent.Enemy.Health;
            if (health == null) return;
            current = health.healthCurrent;
            max = health.health;
        }

        // ─── UI helpers ──────────────────────────────────────────────────────────
        private Image MkImg(Transform parent, string name, Color color,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = GetSprite();
            img.color = color;
            SetAnch(go.GetComponent<RectTransform>(), xMin, xMax, yMin, yMax);
            return img;
        }

        private Text MkText(Transform parent, string name, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = GetFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private static void SetAnch(RectTransform r, float xMin, float xMax, float yMin, float yMax)
        {
            r.anchorMin = new Vector2(xMin, yMin);
            r.anchorMax = new Vector2(xMax, yMax);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private void SetAlwaysOnTop()
        {
            foreach (var graphic in _canvas.GetComponentsInChildren<Graphic>(true))
            {
                var mat = new Material(graphic.defaultMaterial);
                mat.SetInt("unity_GUIZTestMode",
                    (int)UnityEngine.Rendering.CompareFunction.Always);
                graphic.material = mat;
            }
        }
    }
}
