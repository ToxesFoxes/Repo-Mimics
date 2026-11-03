using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Tab: Network (steam networking state, transmissions, sound readiness) ─
        private void DrawNetworkTab(float scrollH)
        {
            GUILayout.BeginHorizontal();
            var client = patches.SteamNetworkingCompat.IsClientConnected();
            var server = patches.SteamNetworkingCompat.IsServerActive();
            DrawChip(client ? "client connected" : "client offline", client ? CGreen : CRed);
            if (PhotonNetwork.IsMasterClient)
                DrawChip(server ? "server running" : "server stopped", server ? CGreen : CRed);
            DrawChip(patches.SteamNetworkingCompat.Active ? "0.5.0 compat shim active" : "native lib hooks", CTextDim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            var readinessH = soundReadinessMap.Count == 0 ? 52f : Mathf.Min(130f, soundReadinessMap.Count * 19f + 6f) + 34f;
            DrawVoiceLogTab(Mathf.Max(120f, scrollH - readinessH - 30f));

            GUILayout.Space(4f);
            DrawReadinessSection();
        }
    }
}
