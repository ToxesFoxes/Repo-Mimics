using Photon.Pun;
using UnityEngine;

namespace TFS_Mimics.patches
{
    public class MimicsFinder : MonoBehaviour
    {
        private static MimicsFinder instance;

        public static TFS_Mimics LocalMimics { get; set; }

        public static void EnsureInitialized()
        {
            if (instance != null)
            {
                return;
            }

            instance = new GameObject("MimicsFinder").AddComponent<MimicsFinder>();
            DontDestroyOnLoad(instance.gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                LocalMimics = null;
                instance = null;
            }
        }
    }
}