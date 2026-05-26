using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace TFS_Mimics
{
    [BepInPlugin("TFS_Mimics", "TFS_Mimics", "1.1.6")]
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

        public static readonly Dictionary<string, ConfigEntry<bool>> enemyConfigEntries = new Dictionary<string, ConfigEntry<bool>>();

        private void Awake()
        {
            PluginLogger = base.Logger;
            PluginLogger.LogInfo("Plugin TFS_Mimics loaded.");

            configVoiceVolume = Config.Bind("General", "Volume", 10, new ConfigDescription("Volume of mimic voices (percent).", new AcceptableValueRange<int>(0, 20), Array.Empty<object>()));
            configPlaybackNearRadius = Config.Bind("General", "Playback Near Radius", 15, new ConfigDescription("Preferred radius around local player for selecting a mimic playback enemy.", new AcceptableValueRange<int>(5, 100), Array.Empty<object>()));
            configMinDelay = Config.Bind("General", "MinDelay", 15, new ConfigDescription("Minimum delay before record/play.", new AcceptableValueRange<int>(5, 300), Array.Empty<object>()));
            configMaxDelay = Config.Bind("General", "MaxDelay", 60, new ConfigDescription("Maximum delay before record/play.", new AcceptableValueRange<int>(10, 600), Array.Empty<object>()));
            configHearYourself = Config.Bind("General", "Hear Yourself?", false, "If false, only other clients hear mimic playback.");
            configPlaybackVoiceFilterEnabled = Config.Bind("General", "Playback Voice Filters Enabled", true, "If false, playback never applies pitch/alien voice filters.");
            configPersistAudioCache = Config.Bind("General", "Persist Audio Cache", true, "If true, received mimic audio clips are saved to disk and loaded on world entry.");
            configPersistMaxFilesPerPlayer = Config.Bind("General", "Persist Max Files Per Player", 100, new ConfigDescription("Maximum number of persisted recordings to keep per player folder.", new AcceptableValueRange<int>(1, 5000), Array.Empty<object>()));
            configDebugVerbose = Config.Bind("Debug", "Verbose Logging", false, "Enable very detailed debug logs for the whole mimic pipeline.");
            configSamplingRate = Config.Bind("Experimental", "Sampling Rate", 48000, new ConfigDescription("Microphone/sample rate.", new AcceptableValueRange<int>(16000, 48000), Array.Empty<object>()));
            configFilterEnabled = Config.Bind("Filter", "Filter Enabled?", false, "Enable per-enemy mimic filter.");

            harmony = new Harmony("TFS_Mimics");
            harmony.PatchAll();

            patches.EnemyDirectorStartPatch.Initialize(Config);
        }
    }
}