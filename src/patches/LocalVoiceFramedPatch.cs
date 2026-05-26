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
            VoiceDataBus.Dispatch(buf);
        }
    }
}