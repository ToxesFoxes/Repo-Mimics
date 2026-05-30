using UnityEngine;
using UnityEngine.UI;

namespace TFS_Mimics
{
    /// <summary>
    /// World-space billboard that attaches to the same GameObject as an AudioSource
    /// and shows a live audio-state indicator floating at that exact 3D position.
    /// Follows the target automatically because it's a component on it.
    /// Mirrors Imperium's EnemyStatus approach.
    /// </summary>
    internal sealed class MimicsAudioMarker : MonoBehaviour
    {
        // ─── Shared resources ────────────────────────────────────────────────────
        private static Sprite s_sprite;
        private static Font   s_font;

        private static Sprite GetSprite()
        {
            if (s_sprite != null) return s_sprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            s_sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
            return s_sprite;
        }

        private static Font GetFont()
        {
            if (s_font == null)
                s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return s_font;
        }

        // ─── Colors ──────────────────────────────────────────────────────────────
        private static readonly Color CAccent  = new Color(0.35f, 0.65f, 1.00f);
        private static readonly Color CBg      = new Color(0.05f, 0.07f, 0.11f, 0.82f);
        private static readonly Color CGreen   = new Color(0.30f, 0.85f, 0.40f);
        private static readonly Color CDim     = new Color(0.50f, 0.55f, 0.60f, 0.75f);
        private static readonly Color CText    = new Color(0.88f, 0.91f, 0.95f);
        private static readonly Color CYellow  = new Color(1.00f, 0.80f, 0.20f);

        // ─── Layout ──────────────────────────────────────────────────────────────
        private const float PanelW      = 100f;
        private const float PanelH      = 38f;
        private const float WorldScale  = 0.010f;   // ~1 × 0.38 m world-unit size
        private const float YOffset     = 0.6f;     // metres above the component pivot

        // ─── Runtime ─────────────────────────────────────────────────────────────
        private AudioSource     _source;
        private Canvas          _canvas;
        private RectTransform   _canvasRect;
        private Image           _bg;
        private Image           _border;
        private Text            _iconText;
        private Text            _stateText;
        private float           _pulse;

        public void Init(AudioSource src)
        {
            _source = src;
            BuildUI();
            SetAlwaysOnTop();
        }

        public void SetVisible(bool v)
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(v);
        }

        private void OnDestroy()
        {
            // Destroy the canvas child GO explicitly — Destroy(component) alone
            // does NOT remove child GameObjects, leaving zombie panels on the AudioSource GO.
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────────
        private void Update()
        {
            if (_source == null || _canvas == null) return;

            var playing = _source.isPlaying;
            _pulse = playing
                ? 0.55f + 0.45f * Mathf.Sin(Time.time * 7f)
                : 1f;

            // Border color: accent pulse when playing, dim otherwise
            if (_border != null)
            {
                var bc = playing ? CAccent : CDim;
                bc.a = playing ? _pulse : 0.4f;
                _border.color = bc;
            }

            if (_bg != null)
            {
                var bgc = playing
                    ? new Color(0.03f, 0.08f, 0.16f, 0.88f)
                    : CBg;
                _bg.color = bgc;
            }

            if (_iconText != null)
            {
                _iconText.text  = playing ? "\u266b" : "\u266a";
                var ic = playing ? CGreen : CDim;
                ic.a = _pulse;
                _iconText.color = ic;
            }

            if (_stateText != null)
            {
                if (playing)
                {
                    _stateText.color = CText;
                    var remain = Mathf.Max(0f, _source.clip != null
                        ? _source.clip.length - _source.time
                        : 0f);
                    _stateText.text = remain > 0.05f ? $"PLAYING\n{remain:F1}s left" : "PLAYING";
                }
                else
                {
                    _stateText.color = CDim;
                    _stateText.text = "AUDIO\nSOURCE";
                }
            }
        }

        private void LateUpdate()
        {
            if (_canvas == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            // Place canvas above the component pivot (follows the GO automatically)
            _canvasRect.position = transform.position + Vector3.up * YOffset;

            // Billboard: match camera rotation so panel always faces the viewer
            _canvasRect.rotation = cam.transform.rotation;
        }

        // ─── UI ──────────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            var go             = new GameObject("_MimicsAudioMarkerCanvas");
            go.transform.SetParent(transform, false);

            _canvas            = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 101;

            _canvasRect           = go.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(PanelW, PanelH);
            _canvasRect.localScale = Vector3.one * WorldScale;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            // Background
            _bg = MkImg(go.transform, "bg", CBg, 0f, 1f, 0f, 1f);

            // Border (top accent line)
            _border = MkImg(go.transform, "border", CAccent, 0f, 1f, 0.90f, 1f);

            // Music note icon (left 28%)
            _iconText = MkText(go.transform, "icon", 18, FontStyle.Bold, CGreen);
            SetAnch(_iconText.rectTransform, 0.02f, 0.28f, 0.0f, 1.0f);
            _iconText.alignment = TextAnchor.MiddleCenter;
            _iconText.text = "\u266a";

            // State label (right 68%)
            _stateText = MkText(go.transform, "state", 8, FontStyle.Bold, CDim);
            SetAnch(_stateText.rectTransform, 0.30f, 0.98f, 0.0f, 1.0f);
            _stateText.alignment = TextAnchor.MiddleLeft;
            _stateText.text = "AUDIO\nSOURCE";
            _stateText.lineSpacing = 1.1f;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────
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
            var go           = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t            = go.AddComponent<Text>();
            t.font           = GetFont();
            t.fontSize       = fontSize;
            t.fontStyle      = style;
            t.color          = color;
            t.raycastTarget  = false;
            t.supportRichText = false;
            return t;
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
