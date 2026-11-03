using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RepoSteamNetworking.API;

namespace TFS_Mimics
{
    [BepInPlugin("TFS_Mimics", "TFS_Mimics", "1.1.3")]
    [BepInDependency("com.rune580.reposteamnetworking")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource PluginLogger;
        private static Harmony harmony;

        public static ConfigEntry<bool> configDebugVerbose;
        public static ConfigEntry<string> configDebugMenuKey;
        public static ConfigEntry<int> configVoiceVolume;
        public static ConfigEntry<int> configPlaybackNearRadius;
        public static ConfigEntry<int> configMinDelay;
        public static ConfigEntry<int> configMaxDelay;
        public static ConfigEntry<bool> configHearYourself;
        public static ConfigEntry<bool> configFilterEnabled;
        public static ConfigEntry<bool> configPlaybackVoiceFilterEnabled;
        public static ConfigEntry<bool> configSmartDirector;
        public static ConfigEntry<bool> configEventDeath;
        public static ConfigEntry<bool> configEventHurt;
        public static ConfigEntry<bool> configEventSpotted;
        public static ConfigEntry<bool> configEventBreak;
        public static ConfigEntry<bool> configEventHeadTalk;
        public static ConfigEntry<bool> configEventPickup;
        public static ConfigEntry<int> configSamplingRate;
        public static ConfigEntry<bool> configPersistAudioCache;
        public static ConfigEntry<int> configPersistMaxFilesPerPlayer;
        public static ConfigEntry<int> configNormalizeTarget;
        public static ConfigEntry<int> configVadSensitivity;
        public static ConfigEntry<bool> configShowSetupWizard;
        public static ConfigEntry<int> configMaxClipsInMemory;
        public static readonly Dictionary<string, ConfigEntry<bool>> enemyConfigEntries = new Dictionary<string, ConfigEntry<bool>>();

        public static UnityEngine.KeyCode GetDebugMenuKey()
        {
            var val = configDebugMenuKey.Value;

            // Handle legacy or user-friendly names
            if (val == "Enter") return UnityEngine.KeyCode.Return;
            if (val.Length == 1 && char.IsDigit(val[0]))
            {
                if (Enum.TryParse<UnityEngine.KeyCode>("Alpha" + val, out var alphaKey)) return alphaKey;
            }

            if (Enum.TryParse<UnityEngine.KeyCode>(val, out var key)) return key;
            return UnityEngine.KeyCode.F8;
        }

        private void Awake()
        {

            PluginLogger = base.Logger;
            PluginLogger.LogInfo("Plugin TFS_Mimics loaded.");

            #region General
            configVoiceVolume = /*                 */ Config.Bind("General", "Volume", /*                          */ 30, /*    */ new ConfigDescription("Volume of mimic voices (percent).", new AcceptableValueRange<int>(0, 100), Array.Empty<object>()));
            configHearYourself = /*                */ Config.Bind("General", "Hear Yourself?", /*                  */ true, /*  */ "If false, only other clients hear mimic playback.");
            configPersistAudioCache = /*           */ Config.Bind("General", "Persist Audio Cache", /*             */ false, /* */ "If true, received mimic audio clips are saved to disk and loaded on world entry.");
            configMaxClipsInMemory = /*            */ Config.Bind("General", "Max Clips In Memory Per Player", /*  */ 40, /*    */ new ConfigDescription("How many recordings per player are kept loaded during a session (oldest unpinned are dropped from memory, not from disk). Keeps long sessions light.", new AcceptableValueRange<int>(5, 500), Array.Empty<object>()));
            configPersistMaxFilesPerPlayer = /*    */ Config.Bind("General", "Persist Max Files Per Player", /*    */ 100, /*   */ new ConfigDescription("Maximum number of persisted recordings to keep per player folder.", new AcceptableValueRange<int>(1, 5000), Array.Empty<object>()));
            configNormalizeTarget = /*             */ Config.Bind("General", "Normalize Target", /*                */ 85, /*    */ new ConfigDescription("Peak normalization target for voice and custom audio (0 = off, 100 = 0 dBFS).", new AcceptableValueRange<int>(0, 100), Array.Empty<object>()));
            configVadSensitivity = /*              */ Config.Bind("General", "Voice Detection Sensitivity", /*     */ 85, /*    */ new ConfigDescription("How easily your voice starts a recording (higher = quieter speech is captured, but more background noise too).", new AcceptableValueRange<int>(0, 100), Array.Empty<object>()));
            configShowSetupWizard = /*             */ Config.Bind("General", "Show Setup Wizard", /*               */ false, /* */ "Open the microphone setup wizard on the main menu. Turns itself off once the setup is saved or skipped.");
            #endregion

            #region Host Only
            configPlaybackNearRadius = /*          */ Config.Bind("Host Only", "Playback Near Radius", /*            */ 15, /*    */ new ConfigDescription("Preferred radius around local player for selecting a mimic playback enemy.", new AcceptableValueRange<int>(5, 100), Array.Empty<object>()));
            configMinDelay = /*                    */ Config.Bind("Host Only", "MinDelay", /*                        */ 10, /*    */ new ConfigDescription("Minimum delay before record/play.", new AcceptableValueRange<int>(1, 300), Array.Empty<object>()));
            configMaxDelay = /*                    */ Config.Bind("Host Only", "MaxDelay", /*                        */ 20, /*    */ new ConfigDescription("Maximum delay before record/play.", new AcceptableValueRange<int>(5, 600), Array.Empty<object>()));
            configPlaybackVoiceFilterEnabled = /*  */ Config.Bind("Host Only", "Playback Voice Filters Enabled", /*  */ true, /*  */ "If false, playback never applies pitch/alien voice filters.");
            configSmartDirector = /*               */ Config.Bind("Host Only", "Smart Director", /*                  */ true, /*  */ "Pick voices, enemies and listeners by context (who can see whom, who is far or dead, what was played recently) instead of at random.");
            configEventDeath = /*                  */ Config.Bind("Host Only", "Event: Player Death", /*             */ true, /*  */ "Some time after a player dies, an enemy near the body calls out with their voice.");
            configEventHurt = /*                   */ Config.Bind("Host Only", "Event: Player Hurt", /*              */ true, /*  */ "Shortly after a player gets hurt, nearby teammates hear their voice from an enemy.");
            configEventSpotted = /*                */ Config.Bind("Host Only", "Event: Enemy Spotted Player", /*     */ true, /*  */ "An enemy that spotted a player tries to lure them with another teammate's voice.");
            configEventBreak = /*                  */ Config.Bind("Host Only", "Event: Valuable Broken", /*          */ true, /*  */ "After a valuable is damaged, an enemy nearby reacts with the voice of whoever held it.");
            configEventHeadTalk = /*               */ Config.Bind("Host Only", "Event: Head Grabber Talks", /*       */ true, /*  */ "While a Head Grabber carries a dead player's head, the head calls out with its owner's voice (and screams when the grabber attacks).");
            configEventPickup = /*                 */ Config.Bind("Host Only", "Event: Valuable Picked Up", /*       */ true, /*  */ "Sometimes, after a player grabs a valuable worth $200+, a hidden enemy nearby talks to them with a teammate's voice.");
            #endregion

            configDebugVerbose = /*                */ Config.Bind("Debug", "Verbose Logging", /*                 */ false, /* */ "Enable very detailed debug logs for the whole mimic pipeline.");
            configDebugMenuKey = /*                */ Config.Bind("Debug", "Menu Keybind", /*                    */ "F8", /*  */ new ConfigDescription("Keybind to open the Mimics debug menu in-game (UnityEngine.KeyCode name, e.g. F8, Insert, Keypad5). Can be rebound by key press in the debug menu Settings tab.", null, Array.Empty<object>()));
            configSamplingRate = /*                */ Config.Bind("Experimental", "Sampling Rate", /*            */ 48000, /* */ new ConfigDescription("Microphone/sample rate.", new AcceptableValueRange<int>(16000, 48000), Array.Empty<object>()));

            configFilterEnabled = /*               */ Config.Bind("Filter", "Filter Enabled?", false, "Enable per-enemy mimic filter.");

            harmony = new Harmony("TFS_Mimics");
            patches.SteamNetworkingCompat.Apply(harmony);

            RepoSteamNetwork.RegisterPacket<MimicsAudioPacket>();
            RepoSteamNetwork.AddCallback<MimicsAudioPacket>(TFS_Mimics.OnMimicsAudioPacketReceived);

            RepoSteamNetwork.RegisterPacket<SoundReadyPacket>();
            RepoSteamNetwork.AddCallback<SoundReadyPacket>(TFS_Mimics.OnSoundReadyPacketReceived);

            RepoSteamNetwork.RegisterPacket<SoundForgetPacket>();
            RepoSteamNetwork.AddCallback<SoundForgetPacket>(TFS_Mimics.OnSoundForgetPacketReceived);

            RepoSteamNetwork.RegisterPacket<CustomSoundManifestPacket>();
            RepoSteamNetwork.AddCallback<CustomSoundManifestPacket>(TFS_Mimics.OnCustomSoundManifestReceived);

            RepoSteamNetwork.RegisterPacket<SyncPlayCommandPacket>();
            RepoSteamNetwork.AddCallback<SyncPlayCommandPacket>(TFS_Mimics.OnSyncPlayCommandReceived);

            harmony.PatchAll();

            patches.EnemyDirectorStartPatch.Initialize(Config);

            EnsureDataFolders();

            MimicsSetupWizard.Create();
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