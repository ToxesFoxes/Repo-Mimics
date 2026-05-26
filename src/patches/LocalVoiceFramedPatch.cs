using HarmonyLib;
using Photon.Voice;

namespace TFS_Mimics.patches
{
    [HarmonyPatch(typeof(LocalVoiceFramed<short>), "PushDataAsync")]
    internal class LocalVoiceFramedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(short[] buf)
        {
            MimicsFinder.EnsureInitialized();

            var local = MimicsFinder.LocalMimics;
            if (local == null || local.photonView == null || !local.photonView.IsMine)
            {
                return;
            }

            local.ProcessVoiceData(buf);
        }
    }
}