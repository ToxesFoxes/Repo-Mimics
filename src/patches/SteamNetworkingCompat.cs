using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using RepoSteamNetworking.Networking.Unity;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace TFS_Mimics.patches
{
    /// <summary>
    /// R.E.P.O. 0.5.0 turned SteamManager into an obsolete static forwarder to SteamLobbyManager.
    /// RepoSteamNetworking 0.1.2 still patches SteamManager.Awake/OnLobbyCreated/OnLobbyEntered/LeaveLobby,
    /// so its Harmony.CreateAndPatchAll throws, its own packets never get registered and its
    /// server/client singletons are never created. This shim re-applies the same hooks on SteamLobbyManager.
    /// It does nothing when the library managed to patch the game itself (i.e. an updated version is installed).
    /// </summary>
    internal static class SteamNetworkingCompat
    {
        private const string RsnGuid = "com.rune580.reposteamnetworking";

        internal static bool Active { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                var lobbyEntered = AccessTools.Method(typeof(SteamLobbyManager), "OnLobbyEntered");
                if (lobbyEntered == null)
                {
                    Plugin.PluginLogger.LogWarning("[Mimics] SteamLobbyManager.OnLobbyEntered not found, SteamNetworking compat skipped.");
                    return;
                }

                var info = Harmony.GetPatchInfo(lobbyEntered);
                if (info != null && info.Owners.Contains(RsnGuid))
                {
                    Plugin.PluginLogger.LogInfo("[Mimics] RepoSteamNetworking already hooks SteamLobbyManager, compat not needed.");
                    return;
                }

                RegisterLibraryPackets();

                harmony.Patch(AccessTools.Method(typeof(SteamLobbyManager), "Awake"),
                    postfix: new HarmonyMethod(typeof(SteamNetworkingCompat), nameof(AwakePostfix)));
                harmony.Patch(AccessTools.Method(typeof(SteamLobbyManager), "OnLobbyCreated"),
                    postfix: new HarmonyMethod(typeof(SteamNetworkingCompat), nameof(OnLobbyCreatedPostfix)));
                harmony.Patch(lobbyEntered,
                    postfix: new HarmonyMethod(typeof(SteamNetworkingCompat), nameof(OnLobbyEnteredPostfix)));
                harmony.Patch(AccessTools.Method(typeof(SteamLobbyManager), "LeaveLobby"),
                    prefix: new HarmonyMethod(typeof(SteamNetworkingCompat), nameof(LeaveLobbyPrefix)));

                Active = true;
                Plugin.PluginLogger.LogInfo("[Mimics] SteamNetworking compat applied (SteamManager -> SteamLobbyManager).");

                // SteamLobbyManager may already be awake if plugins load late.
                if (SteamLobbyManager.instance)
                    CreateSingletons(SteamLobbyManager.instance.gameObject);
            }
            catch (Exception e)
            {
                Plugin.PluginLogger.LogError($"[Mimics] SteamNetworking compat failed: {e}");
            }
        }

        // The library's plugin Awake threw before its private RegisterPackets() ran.
        private static void RegisterLibraryPackets()
        {
            if (!Chainloader.PluginInfos.TryGetValue(RsnGuid, out var pluginInfo) || pluginInfo.Instance == null)
            {
                Plugin.PluginLogger.LogWarning("[Mimics] RepoSteamNetworking plugin instance not found, its packets are not registered.");
                return;
            }

            var register = AccessTools.Method(pluginInfo.Instance.GetType(), "RegisterPackets");
            if (register == null)
            {
                Plugin.PluginLogger.LogWarning("[Mimics] RepoSteamNetworking.RegisterPackets not found.");
                return;
            }

            register.Invoke(pluginInfo.Instance, null);
        }

        private static void CreateSingletons(GameObject parent)
        {
            InvokeCreateSingleton(typeof(RepoSteamNetworkManager), parent);
            InvokeCreateSingleton(typeof(RepoNetworkingServer), parent);
            InvokeCreateSingleton(typeof(RepoNetworkingClient), parent);
        }

        private static void InvokeCreateSingleton(Type type, GameObject parent)
        {
            var method = AccessTools.Method(type, "CreateSingleton", new[] { typeof(GameObject) });
            if (method == null)
            {
                Plugin.PluginLogger.LogWarning($"[Mimics] {type.Name}.CreateSingleton not found.");
                return;
            }

            try
            {
                method.Invoke(null, new object[] { parent });
            }
            catch (TargetInvocationException e)
            {
                Plugin.PluginLogger.LogError($"[Mimics] {type.Name}.CreateSingleton failed: {e.InnerException}");
            }
        }

        private static void AwakePostfix(SteamLobbyManager __instance)
        {
            // Duplicate managers destroy themselves in Awake; only hook the surviving singleton.
            if (SteamLobbyManager.instance != __instance)
                return;

            CreateSingletons(__instance.gameObject);
        }

        private static void OnLobbyCreatedPostfix(Result _result, Lobby _lobby)
        {
            if (_result != Result.OK)
                return;

            RepoNetworkingServer.Instance?.StartSocketServer(_lobby);
        }

        private static void OnLobbyEnteredPostfix(Lobby _lobby)
        {
            RepoNetworkingClient.Instance?.ConnectToServer(_lobby);
        }

        private static void LeaveLobbyPrefix(SteamLobbyManager __instance)
        {
            if (SteamClient.IsValid && __instance.currentLobby.IsOwnedBy(SteamClient.SteamId))
                RepoNetworkingServer.Instance?.StopSocketServer();

            RepoNetworkingClient.Instance?.DisconnectFromServer();
        }
    }
}
