using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics.patches
{
    // Stage 3: enemies' own voice lines. The host decides right before the line goes out, so every client
    // either hears the native line or the stolen voice (sent through the usual director playback).

    internal static class EnemyVoiceLines
    {
        // Returns true when the line must be dropped.
        internal static bool ShouldDrop(PhotonView view, string methodName)
        {
            switch (methodName)
            {
                case "PlayVORPC": return Replace(view.GetComponent<EnemyOogly>());
                case "PlayIdleBreakerRPC": return Replace(view.GetComponent<EnemyBirthdayBoy>());
                case "IdleBreakerRPC": return Replace(view.GetComponent<EnemyTricycle>()); // Gnome uses the same name
                case "TalkBreakerRPC": return Replace(view.GetComponent<EnemyBang>());
                case "IdleBreakerSetRPC": return Echo(view.GetComponent<EnemyDuck>()); // Elsa/Head Grabber share it
                case "TorsoHeadBreakerRPC": return Echo(view.GetComponent<EnemyBombThrower>());
                default: return false;
            }
        }

        internal static bool Replace(Component controller) =>
            controller != null && TFS_Mimics.DirectorOnEnemyVoiceLine(controller, replace: true);

        // Animation-driven lines can't be cut without freezing the enemy — the stolen voice follows them instead.
        internal static bool Echo(Component controller)
        {
            if (controller != null) TFS_Mimics.DirectorOnEnemyVoiceLine(controller, replace: false);
            return false;
        }
    }

    // Multiplayer: the master client sends these RPCs to everyone; drop the send to drop the line.
    [HarmonyPatch(typeof(PhotonView), nameof(PhotonView.RPC), typeof(string), typeof(RpcTarget), typeof(object[]))]
    internal class EnemyVoiceLineRpcPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(PhotonView __instance, string methodName)
        {
            if (!PhotonNetwork.IsMasterClient || __instance == null) return true;
            return !EnemyVoiceLines.ShouldDrop(__instance, methodName);
        }
    }

    // Bomb Thrower's thrown head stops laughing while it talks with a stolen voice (runs on every client).
    [HarmonyPatch(typeof(EnemyBombThrowerHead), "SoundLoopLogic")]
    internal class BombHeadLaughPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(EnemyBombThrowerHead __instance)
        {
            if (!MimicMouths.IsSpeakingOn(__instance.gameObject)) return true;
            __instance.soundLaugh.PlayLoop(false, 5f, 5f);
            return false;
        }
    }

    // Singleplayer: the same methods are called directly instead of through an RPC.
    [HarmonyPatch]
    internal class EnemyVoiceLineSoloPatch
    {
        [HarmonyPatch(typeof(EnemyOogly), "PlayVORPC")]
        [HarmonyPrefix]
        private static bool Oogly(EnemyOogly __instance) => SemiFunc.IsMultiplayer() || !EnemyVoiceLines.Replace(__instance);

        [HarmonyPatch(typeof(EnemyBirthdayBoy), "PlayIdleBreakerRPC")]
        [HarmonyPrefix]
        private static bool BirthdayBoy(EnemyBirthdayBoy __instance) => SemiFunc.IsMultiplayer() || !EnemyVoiceLines.Replace(__instance);

        [HarmonyPatch(typeof(EnemyTricycle), "IdleBreakerRPC")]
        [HarmonyPrefix]
        private static bool Tricycle(EnemyTricycle __instance) => SemiFunc.IsMultiplayer() || !EnemyVoiceLines.Replace(__instance);

        [HarmonyPatch(typeof(EnemyBang), "TalkBreakerRPC")]
        [HarmonyPrefix]
        private static bool Bang(EnemyBang __instance) => SemiFunc.IsMultiplayer() || !EnemyVoiceLines.Replace(__instance);

        [HarmonyPatch(typeof(EnemyDuck), "IdleBreakerSetRPC")]
        [HarmonyPrefix]
        private static void Duck(EnemyDuck __instance)
        {
            if (!SemiFunc.IsMultiplayer()) EnemyVoiceLines.Echo(__instance);
        }

        [HarmonyPatch(typeof(EnemyBombThrower), "TorsoHeadBreakerRPC")]
        [HarmonyPrefix]
        private static void BombThrower(EnemyBombThrower __instance)
        {
            if (!SemiFunc.IsMultiplayer()) EnemyVoiceLines.Echo(__instance);
        }
    }
}
