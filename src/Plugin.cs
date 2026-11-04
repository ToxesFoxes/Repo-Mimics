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
    [BepInPlugin("TFS_Mimics", "TFS_Mimics", "1.2.0")]
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
        public static ConfigEntry<bool> configEventSneakLure;
        public static ConfigEntry<bool> configEventLookUnderLure;
        public static ConfigEntry<bool> configEventVoiceLines;
        public static ConfigEntry<bool> configEventSpecials;
        public static ConfigEntry<bool> configEventHunterHears;
        public static ConfigEntry<bool> configEventTalkAnswer;
        public static ConfigEntry<bool> configEventEnemyFight;
        public static ConfigEntry<bool> configEventEnemyHitItem;
        public static ConfigEntry<bool> configEventMobDialog;
        public static ConfigEntry<bool> configPersonaRhythm;
        public static ConfigEntry<bool> configEventPlayerMoves;
        public static ConfigEntry<bool> configEventEnemySelfHurt;
        public static ConfigEntry<bool> configEventEnemyHurtByPlayer;
        public static ConfigEntry<int> configSamplingRate;
        public static ConfigEntry<int> configStorageSampleRate;
        public static ConfigEntry<bool> configGameVoiceRouting;
        public static ConfigEntry<int> configPlayerVoiceLevel;
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
            configMaxClipsInMemory = /*            */ Config.Bind("General", "Max Clips In Memory Per Player", /*  */ 120, /*    */ new ConfigDescription("How many recordings per player are kept loaded during a session (oldest unpinned are dropped from memory, not from disk). Keeps long sessions light.", new AcceptableValueRange<int>(5, 500), Array.Empty<object>()));
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
            configEventSneakLure = /*              */ Config.Bind("Host Only", "Event: Sneaking Enemy Lure", /*      */ true, /*  */ "When an enemy starts sneaking up unseen (Mentalist, Reaper, Trudge, Headman...), it calls out in a teammate's voice from behind.");
            configEventLookUnderLure = /*          */ Config.Bind("Host Only", "Event: Look-Under Lure", /*          */ true, /*  */ "When an enemy searches under furniture for a hiding player, it tries to talk them out with a teammate's voice.");
            configEventVoiceLines = /*             */ Config.Bind("Host Only", "Event: Enemy Voice Lines", /*        */ true, /*  */ "Talkative enemies (Oogly, Birthday Boy, Tricycle, Bang) sometimes say a teammate's words instead of their own line; Duck and Bomb Thrower answer theirs with one.");
            configEventSpecials = /*               */ Config.Bind("Host Only", "Event: Enemy Special Moments", /*    */ true, /*  */ "Signature enemy moments get a voice (Bomb Thrower's thrown head talks, Spinny's roulette gets a reaction); grabbed players (Spewer, Hidden, Snatcher, Tick) call for help in their own voice; Heart Hugger, Birthday Boy, Thin Man, Valuable Thrower and Ceiling Eye lure with a teammate's.");
            configEventHunterHears = /*            */ Config.Bind("Host Only", "Event: Hunter Hears Mimics", /*      */ true, /*  */ "The blind Hunter goes to check (and may shoot at) mimic voices played within 20m of it.");
            configEventTalkAnswer = /*             */ Config.Bind("Host Only", "Event: Enemy Answers Talk", /*       */ true, /*  */ "When a player talks while looking at an enemy within 10m, the enemy answers once they stop: with a teammate's voice or an echo of the speaker's own.");
            configEventEnemyFight = /*             */ Config.Bind("Host Only", "Event: Enemy Hits Enemy", /*         */ true, /*  */ "When one enemy hurts another, the one that got hit cries out in a player's voice.");
            configEventEnemyHitItem = /*           */ Config.Bind("Host Only", "Event: Enemy Hits Valuable", /*      */ true, /*  */ "When an enemy smashes a valuable, it speaks with the voice of whoever last carried it.");
            configEventMobDialog = /*              */ Config.Bind("Host Only", "Event: Enemies Talk To Each Other", /**/ true, /*  */ "Two calm enemies near each other replay a real exchange between players (linked by recording context), so players overhear them talking.");
            configPersonaRhythm = /*               */ Config.Bind("Host Only", "Persona Rhythm", /*                  */ true, /*  */ "An enemy that just spoke with a player's voice sometimes says another of their phrases after that player's usual pause.");
            configEventPlayerMoves = /*            */ Config.Bind("Host Only", "Event: Player Flees/Approaches/Hides", /**/ true, /*  */ "Reacts to what players do about an enemy: running from it (a teammate calls after them), walking up to it (it greets them), hiding from it (it whispers that it's safe).");
            configEventEnemySelfHurt = /*          */ Config.Bind("Host Only", "Event: Enemy Hurts Itself", /*       */ true, /*  */ "When an enemy gets hurt by a fall, a trap or crashing into something, it complains in a player's voice.");
            configEventEnemyHurtByPlayer = /*      */ Config.Bind("Host Only", "Event: Player Hurts Enemy", /*       */ true, /*  */ "When a player hits an enemy, it cries out: either the attacker's own scream or a teammate pleading to stop.");
            #endregion

            configDebugVerbose = /*                */ Config.Bind("Debug", "Verbose Logging", /*                 */ false, /* */ "Enable very detailed debug logs for the whole mimic pipeline.");
            configDebugMenuKey = /*                */ Config.Bind("Debug", "Menu Keybind", /*                    */ "F8", /*  */ new ConfigDescription("Keybind to open the Mimics debug menu in-game (UnityEngine.KeyCode name, e.g. F8, Insert, Keypad5). Can be rebound by key press in the debug menu Settings tab.", null, Array.Empty<object>()));
            configSamplingRate = /*                */ Config.Bind("Experimental", "Sampling Rate", /*            */ 48000, /* */ new ConfigDescription("Microphone/sample rate.", new AcceptableValueRange<int>(16000, 48000), Array.Empty<object>()));
            configStorageSampleRate = /*           */ Config.Bind("Experimental", "Storage Sample Rate", /*      */ 16000, /* */ new ConfigDescription("Rate recordings are stored and sent at. Playback filters out everything above 4.5 kHz anyway, so 16000 sounds the same as 48000 at a third of the memory, traffic and disk.", new AcceptableValueList<int>(16000, 24000, 32000, 48000), Array.Empty<object>()));
            configGameVoiceRouting = /*            */ Config.Bind("Experimental", "Sound Like Player Voices", /*  */ true, /*  */ "Play mimic voices through the game's player-voice mixer (room reverb, voice volume) and muffle them behind walls like real voices. Off = direct, always-clear output.");
            configPlayerVoiceLevel = /*            */ Config.Bind("Experimental", "Player Voice Level", /*        */ 15, /*    */ new ConfigDescription("Speech loudness of mimic voices when they sound like player voices (average level, like voice-chat gain). Raise if mimics are quieter than real teammates, lower if louder.", new AcceptableValueRange<int>(5, 100), Array.Empty<object>()));

            configFilterEnabled = /*               */ Config.Bind("Filter", "Filter Enabled?", false, "Enable per-enemy mimic filter.");

            harmony = new Harmony("TFS_Mimics");
            patches.SteamNetworkingCompat.Apply(harmony);

            RepoSteamNetwork.RegisterPacket<MimicsAudioPacket>();
            RepoSteamNetwork.AddCallback<MimicsAudioPacket>(TFS_Mimics.OnMimicsAudioPacketReceived);

            RepoSteamNetwork.RegisterPacket<SoundContextPacket>();
            RepoSteamNetwork.AddCallback<SoundContextPacket>(TFS_Mimics.OnSoundContextPacketReceived);

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