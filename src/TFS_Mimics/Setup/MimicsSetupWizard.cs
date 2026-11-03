using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace TFS_Mimics
{
    /// <summary>
    /// First-run microphone setup shown on the main menu: measures background noise and normal speech
    /// on the game's selected mic and picks a "Voice Detection Sensitivity" that fits.
    /// Re-opened by turning on "Show Setup Wizard" (REPOConfig), and automatically after a wizard version bump.
    /// </summary>
    internal sealed class MimicsSetupWizard : MonoBehaviour
    {
        // Bump when the wizard changes enough that everyone should see it again.
        private const int WizardVersion = 1;
        private const int FrameSamples = 1920;          // same frame size the in-game voice stream uses (40 ms @ 48 kHz)
        private const float SilenceSeconds = 3f;
        private const float SpeechSeconds = 5f;

        internal static bool IsOpen { get; private set; }

        private enum Step { Intro, MicCheck, Silence, Speech, Result }

        private Step _step;
        private Rect _rect = new Rect(0f, 0f, 520f, 400f);
        private bool _positioned;

        // Microphone capture
        private string _device;
        private AudioClip _micClip;
        private int _micFrequency;
        private int _readPos;
        private readonly float[] _frame = new float[FrameSamples];
        private float _level, _peak;

        // Calibration
        private float _stepEndsAt;
        private readonly List<float> _silence = new List<float>();
        private readonly List<float> _speech = new List<float>();
        private float _noise;
        private int _sensitivity;
        private float _sensitivitySlider;

        // ─── Bootstrap / flag ─────────────────────────────────────────────────────
        internal static void Create()
        {
            var go = new GameObject("MimicsSetupWizard");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MimicsSetupWizard>();

            if (ReadCompletedVersion() < WizardVersion && Plugin.configShowSetupWizard != null)
                Plugin.configShowSetupWizard.Value = true;
        }

        private static string StatePath => Path.Combine(Paths.ConfigPath, "TFS_Mimics.setup.txt");

        private static int ReadCompletedVersion()
        {
            try
            {
                return File.Exists(StatePath) && int.TryParse(File.ReadAllText(StatePath).Trim(), out var v) ? v : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void MarkCompleted()
        {
            try { File.WriteAllText(StatePath, WizardVersion.ToString()); }
            catch (Exception ex) { Plugin.PluginLogger.LogWarning($"[Mimics] Setup wizard state not saved: {ex.Message}"); }
            if (Plugin.configShowSetupWizard != null) Plugin.configShowSetupWizard.Value = false;
        }

        private static bool OnMainMenu()
        {
            try { return RunManager.instance != null && SemiFunc.IsMainMenu() && MenuPageMain.instance != null; }
            catch { return false; }
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────
        private void Update()
        {
            var shouldShow = Plugin.configShowSetupWizard != null && Plugin.configShowSetupWizard.Value && OnMainMenu();
            if (shouldShow && !IsOpen) Open();
            else if (!shouldShow && IsOpen) Close();
            if (!IsOpen) return;

            // The game draws its own menu cursor and hides the OS one; swap them while the wizard is up.
            if (MenuCursor.instance != null) MenuCursor.instance.OverrideHide(0.2f);

            ReadMicFrames();

            if ((_step == Step.Silence || _step == Step.Speech) && Time.unscaledTime >= _stepEndsAt)
            {
                if (_step == Step.Silence) _step = BeginStep(Step.Speech, SpeechSeconds);
                else FinishCalibration();
            }
        }

        // After the game's own cursor handling (CursorManager hides the OS cursor every unlock).
        private void LateUpdate()
        {
            if (!IsOpen) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Open()
        {
            IsOpen = true;
            _step = Step.Intro;
            _positioned = false;
            StartMic();
        }

        private void Close()
        {
            IsOpen = false;
            StopMic();
            Cursor.visible = false; // back to the game's menu cursor
            if (MenuCursor.instance != null) MenuCursor.instance.OverrideHideReset();
        }

        private void OnDestroy() => StopMic();

        // ─── Microphone ───────────────────────────────────────────────────────────
        private void StartMic()
        {
            StopMic();
            try
            {
                // The settings menu has its own mic preview; only one capture per device at a time.
                MenuSliderMicrophone.MicrophonePreviewStopAll();

                var selected = SessionManager.instance != null ? SessionManager.instance.micDeviceCurrent : string.Empty;
                _device = string.IsNullOrEmpty(selected) ? null : selected;
                if (Microphone.devices.Length == 0)
                {
                    _device = null;
                    return;
                }

                Microphone.GetDeviceCaps(_device, out var minFreq, out var maxFreq);
                _micFrequency = maxFreq == 0 ? 48000 : Mathf.Clamp(48000, minFreq, maxFreq);
                _micClip = Microphone.Start(_device, true, 1, _micFrequency);
                _readPos = 0;
            }
            catch (Exception ex)
            {
                Plugin.PluginLogger.LogWarning($"[Mimics] Setup wizard: mic start failed: {ex.Message}");
                _micClip = null;
            }
        }

        private void StopMic()
        {
            if (_micClip == null) return;
            try { Microphone.End(_device); } catch { /* device gone */ }
            Destroy(_micClip);
            _micClip = null;
        }

        private void ReadMicFrames()
        {
            if (_micClip == null || !Microphone.IsRecording(_device)) return;

            var pos = Microphone.GetPosition(_device);
            var total = _micClip.samples;
            var available = (pos - _readPos + total) % total;
            while (available >= FrameSamples)
            {
                ReadWrapped(_readPos, total);
                _readPos = (_readPos + FrameSamples) % total;
                available -= FrameSamples;

                double sum = 0;
                for (var i = 0; i < FrameSamples; i++) sum += _frame[i] * _frame[i];
                var rms = (float)Math.Sqrt(sum / FrameSamples);
                OnFrame(rms);
            }
        }

        private void ReadWrapped(int start, int total)
        {
            if (start + FrameSamples <= total)
            {
                _micClip.GetData(_frame, start);
                return;
            }

            var first = total - start;
            var a = new float[first];
            var b = new float[FrameSamples - first];
            _micClip.GetData(a, start);
            _micClip.GetData(b, 0);
            Array.Copy(a, 0, _frame, 0, first);
            Array.Copy(b, 0, _frame, first, b.Length);
        }

        private void OnFrame(float rms)
        {
            _level = rms;
            _peak = Mathf.Max(rms, _peak * 0.92f);
            if (_step == Step.Silence) _silence.Add(rms);
            else if (_step == Step.Speech) _speech.Add(rms);
        }

        // ─── Calibration ──────────────────────────────────────────────────────────
        private Step BeginStep(Step step, float seconds)
        {
            _stepEndsAt = Time.unscaledTime + seconds;
            if (step == Step.Silence) _silence.Clear();
            if (step == Step.Speech) _speech.Clear();
            return step;
        }

        private static float Percentile(List<float> values, float p)
        {
            if (values.Count == 0) return 0f;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        private void FinishCalibration()
        {
            _noise = Percentile(_silence, 0.9f);
            var noiseGate = Mathf.Max(_noise * 2f, 0.0008f);
            // Only frames that are clearly speech; quiet gaps between words shouldn't drag the estimate down.
            var voiced = _speech.Where(v => v > noiseGate).ToList();
            var speechLow = voiced.Count >= 5 ? Percentile(voiced, 0.25f) : Percentile(_speech, 0.75f);

            // Threshold halfway (in dB) between the noise ceiling and quieter speech.
            var low = Mathf.Max(_noise * 1.5f, 0.0008f);
            var high = Mathf.Max(speechLow, low * 1.2f);
            var threshold = Mathf.Sqrt(low * high);
            _sensitivity = TFS_Mimics.SensitivityForThreshold(threshold);
            _sensitivitySlider = _sensitivity;
            _step = Step.Result;

            Plugin.PluginLogger.LogInfo($"[Mimics] Setup wizard: device={_device ?? "default"} rate={_micFrequency} noise={_noise:F4} speechLow={speechLow:F4} " +
                $"voicedP50={Percentile(voiced, 0.5f):F4} voicedP90={Percentile(voiced, 0.9f):F4} speechMax={(_speech.Count > 0 ? _speech.Max() : 0f):F4} " +
                $"voicedFrames={voiced.Count}/{_speech.Count} threshold={threshold:F4} sensitivity={_sensitivity}");
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────
        private static readonly Color CAccent = new Color(0.29f, 0.67f, 1.00f);
        private static readonly Color CGreen = new Color(0.27f, 0.87f, 0.42f);
        private static readonly Color CYellow = new Color(1.00f, 0.84f, 0.20f);
        private static readonly Color CRed = new Color(1.00f, 0.33f, 0.33f);
        private static readonly Color CText = new Color(0.90f, 0.92f, 0.94f);
        private static readonly Color CTextDim = new Color(0.55f, 0.59f, 0.63f);
        private static readonly Color CTrack = new Color(0.15f, 0.19f, 0.24f);

        private GUIStyle _window, _title, _body, _small, _btn, _btnPrimary;
        private Texture2D _white;

        private void EnsureStyles()
        {
            if (_window != null) return;
            _white = Tex(Color.white);
            _window = new GUIStyle(GUI.skin.window) { padding = new RectOffset(18, 18, 14, 16), normal = { background = Tex(new Color(0.07f, 0.09f, 0.11f, 0.98f)) }, onNormal = { background = Tex(new Color(0.07f, 0.09f, 0.11f, 0.98f)) } };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = CAccent } };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = CText } };
            _small = new GUIStyle(_body) { fontSize = 11, normal = { textColor = CTextDim } };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 12, padding = new RectOffset(12, 12, 6, 6), normal = { background = Tex(new Color(0.11f, 0.14f, 0.17f)), textColor = CText }, hover = { background = Tex(new Color(0.18f, 0.22f, 0.27f)), textColor = Color.white } };
            _btnPrimary = new GUIStyle(_btn) { normal = { background = Tex(new Color(0.16f, 0.38f, 0.62f)), textColor = Color.white }, hover = { background = Tex(CAccent), textColor = Color.white } };
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            if (!_positioned)
            {
                _rect.x = (Screen.width - _rect.width) * 0.5f;
                _rect.y = (Screen.height - _rect.height) * 0.5f;
                _positioned = true;
            }

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _white);
            GUI.color = Color.white;
            _rect = GUI.Window(9950, _rect, DrawWindow, string.Empty, _window);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("MIMICS  ·  microphone setup", _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{(int)_step + 1} / 5", _small, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);

            switch (_step)
            {
                case Step.Intro: DrawIntro(); break;
                case Step.MicCheck: DrawMicCheck(); break;
                case Step.Silence: DrawCountdown("Stay quiet", "Measuring the background noise of your room. Don't talk for a moment."); break;
                case Step.Speech: DrawCountdown("Now talk normally", "Say anything at your usual in-game volume — count to ten, describe your day."); break;
                case Step.Result: DrawResult(); break;
            }

            GUI.DragWindow(new Rect(0f, 0f, _rect.width, 30f));
        }

        private void DrawIntro()
        {
            GUILayout.Label("Mimics records short phrases of your voice and lets monsters play them back to your crew.", _body);
            GUILayout.Space(6f);
            GUILayout.Label("This quick setup tunes how loud you need to speak for a phrase to be recorded, so quiet players get picked up and background noise doesn't.", _body);
            GUILayout.Space(6f);
            GUILayout.Label("It takes about 10 seconds. You can reopen it any time with \"Show Setup Wizard\" in the mod settings.", _small);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Skip", _btn)) MarkCompleted();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Start", _btnPrimary, GUILayout.Width(120f))) _step = Step.MicCheck;
            GUILayout.EndHorizontal();
        }

        private void DrawMicCheck()
        {
            if (_micClip == null)
            {
                GUI.color = CRed;
                GUILayout.Label("No microphone found. Plug one in or pick it in the game's audio settings, then reopen this setup.", _body);
                GUI.color = Color.white;
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Retry", _btn)) StartMic();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", _btn)) MarkCompleted();
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.Label("Microphone", _small);
            GUILayout.Label(_device ?? "System default", _body);
            GUILayout.Space(8f);
            GUILayout.Label("Say something — the bar should jump. If it doesn't, change the microphone in the game's audio settings first.", _body);
            GUILayout.Space(8f);
            DrawMeter(-1f, -1f);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", _btn)) _step = Step.Intro;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("It works", _btnPrimary, GUILayout.Width(120f))) _step = BeginStep(Step.Silence, SilenceSeconds);
            GUILayout.EndHorizontal();
        }

        private void DrawCountdown(string heading, string text)
        {
            GUILayout.Label(heading, _title);
            GUILayout.Space(4f);
            GUILayout.Label(text, _body);
            GUILayout.Space(10f);
            DrawMeter(-1f, -1f);
            GUILayout.Space(10f);

            var duration = _step == Step.Silence ? SilenceSeconds : SpeechSeconds;
            var left = Mathf.Max(0f, _stepEndsAt - Time.unscaledTime);
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(6f));
            GUI.color = CTrack;
            GUI.DrawTexture(r, _white);
            GUI.color = CAccent;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * (1f - left / duration), r.height), _white);
            GUI.color = Color.white;
            GUILayout.Label($"{left:F1}s", _small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Restart", _btn, GUILayout.ExpandWidth(false))) _step = BeginStep(Step.Silence, SilenceSeconds);
        }

        private void DrawResult()
        {
            var threshold = TFS_Mimics.ThresholdForSensitivity(Mathf.RoundToInt(_sensitivitySlider));
            var triggered = _level > Mathf.Max(threshold, _noise * 3f);

            GUILayout.Label("All set. Try talking — the dot lights up when a phrase would be recorded.", _body);
            GUILayout.Space(8f);
            DrawMeter(threshold, _noise);
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUI.color = triggered ? CGreen : CTextDim;
            GUILayout.Label(triggered ? "●  recording" : "○  quiet", _body, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"noise {_noise:F4} · threshold {threshold:F4}", _small, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sensitivity", _body, GUILayout.Width(90f));
            _sensitivitySlider = GUILayout.HorizontalSlider(_sensitivitySlider, 0f, 100f);
            GUILayout.Label($"{Mathf.RoundToInt(_sensitivitySlider)}", _body, GUILayout.Width(32f));
            GUILayout.EndHorizontal();
            GUILayout.Label($"Recommended: {_sensitivity}. Higher catches quieter speech but also more background noise.", _small);
            GUILayout.Space(6f);

            if (Plugin.configHearYourself != null)
            {
                Plugin.configHearYourself.Value = GUILayout.Toggle(Plugin.configHearYourself.Value,
                    "  Hear your own voice from monsters", _body);
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Measure again", _btn)) _step = BeginStep(Step.Silence, SilenceSeconds);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save", _btnPrimary, GUILayout.Width(120f)))
            {
                if (Plugin.configVadSensitivity != null) Plugin.configVadSensitivity.Value = Mathf.RoundToInt(_sensitivitySlider);
                MarkCompleted();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawMeter(float threshold, float noise)
        {
            var r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(16f));
            GUI.color = CTrack;
            GUI.DrawTexture(r, _white);
            var above = threshold > 0f && _level > threshold;
            GUI.color = above ? CGreen : CAccent;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * MeterT(_level), r.height), _white);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(r.x + r.width * MeterT(_peak) - 1f, r.y, 2f, r.height), _white);
            if (noise > 0f)
            {
                GUI.color = CTextDim;
                GUI.DrawTexture(new Rect(r.x + r.width * MeterT(noise) - 1f, r.y - 2f, 2f, r.height + 4f), _white);
            }
            if (threshold > 0f)
            {
                GUI.color = CYellow;
                GUI.DrawTexture(new Rect(r.x + r.width * MeterT(threshold) - 1f, r.y - 3f, 2f, r.height + 6f), _white);
            }
            GUI.color = Color.white;
        }

        private static float MeterT(float rms)
        {
            if (rms <= 0.000001f) return 0f;
            return Mathf.Clamp01((20f * Mathf.Log10(rms) + 60f) / 60f);
        }
    }
}
