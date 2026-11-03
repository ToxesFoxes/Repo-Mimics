using HarmonyLib;
using UnityEngine;

namespace TFS_Mimics.patches
{
    // Game-event hooks for the host director. Each postfix only forwards; the director checks host/config itself.

    [HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
    internal class PlayerDeathDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerAvatar __instance)
        {
            // The RPC bails out early for invalid senders; only react to a death that actually happened.
            if (__instance != null && __instance.deadSet)
                TFS_Mimics.DirectorOnPlayerDeath(__instance);
        }
    }

    [HarmonyPatch(typeof(EnemyVision), "VisionTrigger")]
    internal class EnemyVisionDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnemyVision __instance, PlayerAvatar player, bool culled)
        {
            TFS_Mimics.DirectorOnEnemySpotted(__instance.Enemy, player, culled);
        }
    }

    // Multiplayer: the master adds the grabber via this RPC on every client.
    [HarmonyPatch(typeof(PhysGrabObject), "GrabPlayerAddRPC")]
    internal class ValuableGrabDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PhysGrabObject __instance, int photonViewID)
        {
            var view = Photon.Pun.PhotonView.Find(photonViewID);
            var grabber = view != null ? view.GetComponent<PhysGrabber>() : null;
            if (grabber != null)
                TFS_Mimics.DirectorOnValuableGrabbed(__instance, grabber.playerAvatar);
        }
    }

    // Singleplayer: GrabStarted adds the grabber directly, no RPC.
    [HarmonyPatch(typeof(PhysGrabObject), "GrabStarted")]
    internal class ValuableGrabSoloDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PhysGrabObject __instance, PhysGrabber player)
        {
            if (GameManager.instance != null && GameManager.instance.gameMode == 0 && player != null)
                TFS_Mimics.DirectorOnValuableGrabbed(__instance, player.playerAvatar);
        }
    }

    [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "BreakRPC")]
    internal class ValuableBreakDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PhysGrabObjectImpactDetector __instance, float valueLost, Vector3 _contactPoint, bool _loseValue)
        {
            if (_loseValue && __instance != null && __instance.isValuable)
                TFS_Mimics.DirectorOnValuableBroken(__instance.physGrabObject, valueLost, _contactPoint);
        }
    }
}
