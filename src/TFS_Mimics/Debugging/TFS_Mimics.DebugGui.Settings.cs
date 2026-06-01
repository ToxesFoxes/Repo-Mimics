using BepInEx.Configuration;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Tab 6: Settings ──────────────────────────────────────────────────────
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
            if (_settingHostIntervalBuf == null) _settingHostIntervalBuf = $"{(Plugin.configHostAuthorityInterval?.Value ?? 4f):F0}";

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

            DrawSettingSliderFloat("Host Authority Interval", ref _settingHostIntervalBuf,
                Plugin.configHostAuthorityInterval, 1f, 30f, "s");

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

        private void DrawSettingSliderFloat(string label, ref string textBuf, ConfigEntry<float> entry, float min, float max, string unit)
        {
            if (entry == null) return;
            var current = entry.Value;

            GUILayout.BeginVertical(_gsPanelBox);

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
                if (float.TryParse(textBuf, out var fv))
                    entry.Value = Mathf.Clamp(fv, min, max);
                _settingsDirty = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            var newSlider = GUILayout.HorizontalSlider(current, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(12f));
            var rounded = Mathf.Round(newSlider);
            if (!Mathf.Approximately(rounded, current))
            {
                entry.Value = Mathf.Clamp(rounded, min, max);
                textBuf = $"{entry.Value:F0}";
            }

            GUILayout.BeginHorizontal();
            GUI.color = CTextDim;
            GUILayout.Label($"{min:F0}", _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUI.color = CText;
            GUILayout.Label($"Current: {current:F1}{(string.IsNullOrEmpty(unit) ? "" : " " + unit)}", _gsSmall, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUI.color = CTextDim;
            GUILayout.Label($"{max:F0}", _gsSmall, GUILayout.ExpandWidth(false));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.EndVertical();
            GUILayout.Space(3f);
        }
    }
}
