using System;
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
            if (_settingVoiceLevelBuf == null) _settingVoiceLevelBuf = (Plugin.configPlayerVoiceLevel?.Value ?? 15).ToString();
            if (_settingSamplingRateBuf == null) _settingSamplingRateBuf = (Plugin.configSamplingRate?.Value ?? 48000).ToString();
            if (_settingNormalizeBuf == null) _settingNormalizeBuf = (Plugin.configNormalizeTarget?.Value ?? 85).ToString();
            if (_settingVadSensitivityBuf == null) _settingVadSensitivityBuf = (Plugin.configVadSensitivity?.Value ?? 85).ToString();

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

            DrawSettingSliderInt("Voice Detection Sensitivity", ref _settingVadSensitivityBuf,
                Plugin.configVadSensitivity, 0, 100, "");

            GUILayout.Space(4f);

            // ── Director (host) ────────────────────────────────────────────────
            DrawSettingsSection("Director (host only)");

            DrawSettingToggle("Smart Director", Plugin.configSmartDirector);
            DrawSettingToggle("Event: Player Death", Plugin.configEventDeath);
            DrawSettingToggle("Event: Player Hurt", Plugin.configEventHurt);
            DrawSettingToggle("Event: Enemy Spotted Player", Plugin.configEventSpotted);
            DrawSettingToggle("Event: Valuable Broken", Plugin.configEventBreak);
            DrawSettingToggle("Event: Head Grabber Talks", Plugin.configEventHeadTalk);
            DrawSettingToggle("Event: Valuable Picked Up", Plugin.configEventPickup);
            DrawSettingToggle("Event: Sneaking Enemy Lure", Plugin.configEventSneakLure);
            DrawSettingToggle("Event: Look-Under Lure", Plugin.configEventLookUnderLure);
            DrawSettingToggle("Event: Enemy Voice Lines", Plugin.configEventVoiceLines);
            DrawSettingToggle("Event: Enemy Special Moments", Plugin.configEventSpecials);
            DrawSettingToggle("Event: Hunter Hears Mimics", Plugin.configEventHunterHears);
            DrawSettingToggle("Event: Enemy Answers Talk", Plugin.configEventTalkAnswer);
            DrawSettingToggle("Event: Enemy Hits Enemy", Plugin.configEventEnemyFight);
            DrawSettingToggle("Event: Enemy Hits Valuable", Plugin.configEventEnemyHitItem);
            DrawSettingToggle("Event: Enemies Talk To Each Other", Plugin.configEventMobDialog);
            DrawSettingToggle("Persona Rhythm", Plugin.configPersonaRhythm);
            DrawSettingToggle("Event: Player Flees/Approaches/Hides", Plugin.configEventPlayerMoves);
            DrawSettingToggle("Event: Enemy Hurts Itself", Plugin.configEventEnemySelfHurt);
            DrawSettingToggle("Event: Player Hurts Enemy", Plugin.configEventEnemyHurtByPlayer);

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
            DrawSettingToggle("Sound Like Player Voices", Plugin.configGameVoiceRouting);
            DrawSettingSliderInt("Player Voice Level", ref _settingVoiceLevelBuf, Plugin.configPlayerVoiceLevel, 5, 100, "");
            DrawStorageRateChooser();

            GUILayout.Space(4f);

            // ── Debug ──────────────────────────────────────────────────────────
            DrawSettingsSection("Debug");

            DrawSettingKeybind("Menu Keybind", Plugin.configDebugMenuKey);
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

        private static readonly int[] StorageRates = { 16000, 24000, 32000, 48000 };

        private void DrawStorageRateChooser()
        {
            if (Plugin.configStorageSampleRate == null) return;
            GUILayout.BeginHorizontal(_gsPanelBox);
            GUI.color = CText;
            GUILayout.Label("Storage Sample Rate", _gsLabel, GUILayout.Width(200f));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            foreach (var rate in StorageRates)
            {
                var on = Plugin.configStorageSampleRate.Value == rate;
                if (GUILayout.Button($"{rate / 1000}k", on ? _gsBtnPrimary : _gsBtn, GUILayout.Width(40f), GUILayout.Height(20f)))
                    Plugin.configStorageSampleRate.Value = rate;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
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

        private void DrawSettingKeybind(string label, ConfigEntry<string> entry)
        {
            if (entry == null) return;
            GUILayout.BeginHorizontal(_gsPanelBox);
            GUI.color = CText;
            GUILayout.Label(label, _gsLabel, GUILayout.Width(200f));
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();

            var listening = _keybindListeningEntry == entry;
            if (listening)
            {
                var e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && !IsModifierKey(e.keyCode))
                {
                    if (e.keyCode != KeyCode.Escape)
                        entry.Value = e.keyCode.ToString();
                    _keybindListeningEntry = null;
                    listening = false;
                    e.Use();
                }
            }

            GUI.color = listening ? CYellow : Color.white;
            if (GUILayout.Button(listening ? "Press a key..." : entry.Value, _gsBtn, GUILayout.MinHeight(18f), GUILayout.Width(110f)))
            {
                _keybindListeningEntry = listening ? null : entry;
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (listening)
            {
                GUI.color = CTextDim;
                GUILayout.Label("Press any key to bind, Esc to cancel.", _gsSmall);
                GUI.color = Color.white;
            }
            GUILayout.Space(2f);
        }

        // Shift is reserved for the Shift+Key focus toggle, so bare modifiers can't be bound.
        private static bool IsModifierKey(KeyCode key)
        {
            return key == KeyCode.LeftShift || key == KeyCode.RightShift
                || key == KeyCode.LeftControl || key == KeyCode.RightControl
                || key == KeyCode.LeftAlt || key == KeyCode.RightAlt
                || key == KeyCode.LeftCommand || key == KeyCode.RightCommand;
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
