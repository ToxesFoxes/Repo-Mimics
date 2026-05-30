using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace TFS_Mimics
{
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
        private static readonly Color CAccent  = new Color(0.35f, 0.65f, 1.00f);
        private static readonly Color CBgDark  = new Color(0.06f, 0.08f, 0.12f, 0.88f);
        private static readonly Color CBgPanel = new Color(0.10f, 0.13f, 0.18f, 0.72f);
        private static readonly Color CGreen   = new Color(0.30f, 0.85f, 0.40f);
        private static readonly Color CYellow  = new Color(1.00f, 0.80f, 0.20f);
        private static readonly Color CRed     = new Color(0.90f, 0.25f, 0.25f);
        private static readonly Color CText    = new Color(0.90f, 0.92f, 0.95f);
        private static readonly Color CDim     = new Color(0.55f, 0.60f, 0.65f);

        // ─── Layout constants ────────────────────────────────────────────────────
        private const float PanelW     = 160f;
        private const float PanelH     = 82f;
        private const float WorldScale = 0.012f;   // world-unit size: ~1.92 × 0.98 m
        private const float HeadOffset = 2.4f;     // metres above EnemyParent origin
        private const float DataHz     = 0.2f;     // data refresh interval

        // ─── Runtime state ───────────────────────────────────────────────────────
        private EnemyParent _enemyParent;
        private Transform   _playerTransform;

        private Canvas          _canvas;
        private RectTransform   _canvasRect;
        private Image           _hpFill;
        private Text            _nameText;
        private Text            _hpText;
        private Text            _stateText;
        private Text            _distText;
        private Image           _playingBg;
        private Text            _playingText;

        private float  _nextRefresh;
        private float  _smoothHp = 1f;

        public bool IsPlayingAudio { get; set; }

        // ─── Public init ─────────────────────────────────────────────────────────
        public void Init(EnemyParent enemyParent, Transform playerTransform)
        {
            _enemyParent     = enemyParent;
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

        private void LateUpdate()
        {
            if (_canvas == null || _enemyParent == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            // Place panel above enemy
            _canvasRect.position = _enemyParent.transform.position + Vector3.up * HeadOffset;

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

            _canvas             = go.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.WorldSpace;
            _canvas.sortingOrder = 100;

            _canvasRect           = go.GetComponent<RectTransform>();
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
            _hpFill         = hpFillGo.AddComponent<Image>();
            _hpFill.sprite  = GetSprite();
            _hpFill.type    = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillAmount = 1f;
            _hpFill.color   = CGreen;
            var hfr         = hpFillGo.GetComponent<RectTransform>();
            hfr.anchorMin   = Vector2.zero;
            hfr.anchorMax   = Vector2.one;
            hfr.offsetMin   = hfr.offsetMax = Vector2.zero;

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
            _playingText.text      = "\u25b6  MIMICS PLAYING";
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
            if (_hpFill != null)
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
            if (_hpText != null)
                _hpText.text = hpMax > 0 ? $"{hpCur} / {hpMax} HP" : "HP: —";

            // State
            var state = TryGetState();
            if (_stateText != null) _stateText.text = state ?? "—";

            // Distance
            if (_distText != null && _playerTransform != null)
            {
                var d = Vector3.Distance(_enemyParent.transform.position, _playerTransform.position);
                _distText.text = $"{d:F1} m";
            }

            // Playing indicator
            if (_playingBg != null && _playingText != null)
            {
                var playColor = IsPlayingAudio ? new Color(CGreen.r, CGreen.g, CGreen.b, 0.18f) : new Color(0f, 0f, 0f, 0f);
                var textColor = IsPlayingAudio ? CGreen : new Color(0f, 0f, 0f, 0f);
                _playingBg.color   = playColor;
                _playingText.color = textColor;
            }
        }

        // ─── Reflection: HP ──────────────────────────────────────────────────────
        private static FieldInfo _fEnemyOnParent;
        private static FieldInfo _fHealthOnEnemy;
        private static FieldInfo _fHealthCurrent;
        private static FieldInfo _fHealthMax;
        private static FieldInfo _fCurrentState;

        private static void EnsureReflection(EnemyParent parent)
        {
            // Cache parent → Enemy field
            if (_fEnemyOnParent == null)
            {
                var pt = parent.GetType();
                _fEnemyOnParent =
                    pt.GetField("Enemy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    pt.GetField("enemy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_fEnemyOnParent == null) return;

            // Need a live Enemy instance to discover its type
            if (_fCurrentState != null && _fHealthCurrent != null) return;

            var enemy = _fEnemyOnParent.GetValue(parent);
            if (enemy == null) return;

            var et = enemy.GetType();

            if (_fCurrentState == null)
                _fCurrentState =
                    et.GetField("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    et.GetField("currentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_fHealthOnEnemy == null)
                _fHealthOnEnemy =
                    et.GetField("Health", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    et.GetField("health", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_fHealthOnEnemy == null) return;

            var health = _fHealthOnEnemy.GetValue(enemy);
            if (health == null) return;

            var ht = health.GetType();
            if (_fHealthCurrent == null)
                _fHealthCurrent = ht.GetField("healthCurrent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_fHealthMax == null)
                _fHealthMax = ht.GetField("health",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private void TryGetHealth(out int current, out int max)
        {
            current = max = -1;
            if (_enemyParent == null) return;

            EnsureReflection(_enemyParent);
            if (_fEnemyOnParent == null || _fHealthOnEnemy == null) return;

            var enemy = _fEnemyOnParent.GetValue(_enemyParent);
            if (enemy == null) return;

            var health = _fHealthOnEnemy.GetValue(enemy);
            if (health == null) return;

            if (_fHealthCurrent != null)
                current = (int)_fHealthCurrent.GetValue(health);
            if (_fHealthMax != null)
                max = (int)_fHealthMax.GetValue(health);
        }

        private string TryGetState()
        {
            if (_enemyParent == null) return null;

            EnsureReflection(_enemyParent);
            if (_fEnemyOnParent == null || _fCurrentState == null) return null;

            var enemy = _fEnemyOnParent.GetValue(_enemyParent);
            return enemy == null ? null : _fCurrentState.GetValue(enemy)?.ToString();
        }

        // ─── UI helpers ──────────────────────────────────────────────────────────
        private Image MkImg(Transform parent, string name, Color color,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img    = go.AddComponent<Image>();
            img.sprite = GetSprite();
            img.color  = color;
            SetAnch(go.GetComponent<RectTransform>(), xMin, xMax, yMin, yMax);
            return img;
        }

        private Text MkText(Transform parent, string name, int fontSize, FontStyle style, Color color)
        {
            var go   = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text          = go.AddComponent<Text>();
            text.font         = GetFont();
            text.fontSize     = fontSize;
            text.fontStyle    = style;
            text.color        = color;
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
