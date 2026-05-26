using HarmonyLib;
using Photon.Pun;

namespace TFS_Mimics.patches
{
    [HarmonyPatch(typeof(PlayerAvatar), "Awake")]
    internal class PlayerAvatarPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerAvatar __instance)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                return;
            }

            var mimics = __instance.GetComponent<TFS_Mimics>();
            if (mimics == null)
            {
                mimics = __instance.gameObject.AddComponent<TFS_Mimics>();
            }

            var view = __instance.GetComponent<PhotonView>();
            if (view != null && view.IsMine)
            {
                MimicsFinder.LocalMimics = mimics;
            }
        }
    }
}