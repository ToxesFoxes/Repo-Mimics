namespace TFS_Mimics.patches
{
    // Static voice-data event bus — replaces the original's MimicsFinder MonoBehaviour singleton.
    // No scene GameObject is needed; the patch simply routes microphone frames here.
    internal static class VoiceDataBus
    {
        private static TFS_Mimics _localMimics;

        internal static TFS_Mimics LocalMimics
        {
            get => _localMimics;
            set => _localMimics = value;
        }

        internal static void Dispatch(short[] voiceData)
        {
            var local = _localMimics;
            if (local == null || local.photonView == null || !local.photonView.IsMine)
            {
                return;
            }

            local.ProcessVoiceData(voiceData);
        }
    }
}