using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace TFS_Mimics
{
    [BepInPlugin("TFS_Mimics", "TFS_Mimics", "1.0.2")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource PluginLogger;
        private static Harmony harmony;

        public static ConfigEntry<bool> configDebugVerbose;
        public static ConfigEntry<int> configVoiceVolume;
        public static ConfigEntry<int> configPlaybackNearRadius;
        public static ConfigEntry<int> configMinDelay;
        public static ConfigEntry<int> configMaxDelay;
        public static ConfigEntry<bool> configHearYourself;
        public static ConfigEntry<bool> configFilterEnabled;
        public static ConfigEntry<bool> configPlaybackVoiceFilterEnabled;
        public static ConfigEntry<int> configSamplingRate;
        public static ConfigEntry<bool> configPersistAudioCache;
        public static ConfigEntry<int> configPersistMaxFilesPerPlayer;
        public static ConfigEntry<int> configNormalizeTarget;

        public static readonly Dictionary<string, ConfigEntry<bool>> enemyConfigEntries = new Dictionary<string, ConfigEntry<bool>>();

        private void Awake()
        {
            PluginLogger = base.Logger;
            PluginLogger.LogInfo("Plugin TFS_Mimics loaded.");

            configVoiceVolume = Config.Bind("General", "Volume", 20, new ConfigDescription("Volume of mimic voices (percent).", new AcceptableValueRange<int>(0, 100), Array.Empty<object>()));
            configPlaybackNearRadius = Config.Bind("General", "Playback Near Radius", 12, new ConfigDescription("Preferred radius around local player for selecting a mimic playback enemy.", new AcceptableValueRange<int>(5, 100), Array.Empty<object>()));
            configMinDelay = Config.Bind("General", "MinDelay", 5, new ConfigDescription("Minimum delay before record/play.", new AcceptableValueRange<int>(5, 300), Array.Empty<object>()));
            configMaxDelay = Config.Bind("General", "MaxDelay", 15, new ConfigDescription("Maximum delay before record/play.", new AcceptableValueRange<int>(10, 600), Array.Empty<object>()));
            configHearYourself = Config.Bind("General", "Hear Yourself?", false, "If false, only other clients hear mimic playback.");
            configPlaybackVoiceFilterEnabled = Config.Bind("General", "Playback Voice Filters Enabled", true, "If false, playback never applies pitch/alien voice filters.");
            configPersistAudioCache = Config.Bind("General", "Persist Audio Cache", false, "If true, received mimic audio clips are saved to disk and loaded on world entry.");
            configPersistMaxFilesPerPlayer = Config.Bind("General", "Persist Max Files Per Player", 100, new ConfigDescription("Maximum number of persisted recordings to keep per player folder.", new AcceptableValueRange<int>(1, 5000), Array.Empty<object>()));
            configNormalizeTarget = Config.Bind("General", "Normalize Target", 85, new ConfigDescription("Peak normalization target for voice and custom audio (0 = off, 100 = 0 dBFS).", new AcceptableValueRange<int>(0, 100), Array.Empty<object>()));
            configDebugVerbose = Config.Bind("Debug", "Verbose Logging", false, "Enable very detailed debug logs for the whole mimic pipeline.");
            configSamplingRate = Config.Bind("Experimental", "Sampling Rate", 48000, new ConfigDescription("Microphone/sample rate.", new AcceptableValueRange<int>(16000, 48000), Array.Empty<object>()));
            configFilterEnabled = Config.Bind("Filter", "Filter Enabled?", false, "Enable per-enemy mimic filter.");

            harmony = new Harmony("TFS_Mimics");
            harmony.PatchAll();

            patches.EnemyDirectorStartPatch.Initialize(Config);

            EnsureDataFolders();
        }

        private void EnsureDataFolders()
        {
            var root = Path.Combine(BepInEx.Paths.BepInExRootPath, "plugins", "ToxesFoxes-Mimics");

            // audio-cache — created silently, the mod writes there automatically
            var cacheDir = Path.Combine(root, "audio-cache");
            Directory.CreateDirectory(cacheDir);

            // custom-audio — created with a README placeholder so users know what to do
            var customDir = Path.Combine(root, "custom-audio");
            Directory.CreateDirectory(customDir);

            var readmePath = Path.Combine(customDir, "HOW TO USE.txt");
            if (!File.Exists(readmePath))
            {
                File.WriteAllText(readmePath,
                    "=== Mimics — Custom Audio ===\r\n\r\n" +
                    "Drop .mp3 or .wav files into this folder.\r\n" +
                    "They will be loaded automatically when you join a level.\r\n\r\n" +
                    "Custom clips are played through nearby enemies just like recorded\r\n" +
                    "player voices, but without any online-player restriction.\r\n\r\n" +
                    "Tips:\r\n" +
                    "  - Mono or stereo files both work.\r\n" +
                    "  - Volume is normalized automatically (configurable in Settings tab).\r\n" +
                    "  - Use the 'Reload Custom' button in the Debug HUD (Cache tab) to\r\n" +
                    "    reload files without restarting the game.\r\n" +
                    "  - This file (HOW TO USE.txt) is ignored by the mod.\r\n"
                );
            }

            PluginLogger.LogInfo($"[Mimics] Data folders ready: {root}");
        }
    }
}