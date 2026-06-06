using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using RepoSteamNetworking.Networking.Serialization;

namespace TFS_Mimics
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Existing packet — audio chunks
    // ─────────────────────────────────────────────────────────────────────────────
    public class MimicsAudioPacket : NetworkPacket<MimicsAudioPacket>
    {
        public byte[] ChunkData { get; set; }
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
        public int SampleRate { get; set; }
        public string TransmissionId { get; set; }
        public int SenderActorNumber { get; set; }

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(SenderActorNumber);
            socketMessage.Write(TransmissionId);
            socketMessage.Write(ChunkIndex);
            socketMessage.Write(TotalChunks);
            socketMessage.Write(SampleRate);
            socketMessage.Write(ChunkData);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            SenderActorNumber = socketMessage.Read<int>();
            TransmissionId = socketMessage.Read<string>();
            ChunkIndex = socketMessage.Read<int>();
            TotalChunks = socketMessage.Read<int>();
            SampleRate = socketMessage.Read<int>();
            ChunkData = socketMessage.Read<byte[]>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Client → Host: "I now have this voice sound"
    // ─────────────────────────────────────────────────────────────────────────────
    public class SoundReadyPacket : NetworkPacket<SoundReadyPacket>
    {
        public string SoundGuid { get; set; }
        public int SenderActorNumber { get; set; }

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(SenderActorNumber);
            socketMessage.Write(SoundGuid);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            SenderActorNumber = socketMessage.Read<int>();
            SoundGuid = socketMessage.Read<string>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Client → Host: full manifest of custom audio available on this client
    // ─────────────────────────────────────────────────────────────────────────────
    public class CustomSoundManifestPacket : NetworkPacket<CustomSoundManifestPacket>
    {
        public string[] SoundGuids { get; set; }
        public string[] ContentHashes { get; set; }
        public int SenderActorNumber { get; set; }

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(SenderActorNumber);
            socketMessage.Write(SoundGuids?.Length ?? 0);
            foreach (var g in SoundGuids ?? System.Array.Empty<string>())
                socketMessage.Write(g ?? string.Empty);
            socketMessage.Write(ContentHashes?.Length ?? 0);
            foreach (var h in ContentHashes ?? System.Array.Empty<string>())
                socketMessage.Write(h ?? string.Empty);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            SenderActorNumber = socketMessage.Read<int>();
            var guidCount = socketMessage.Read<int>();
            SoundGuids = new string[guidCount];
            for (var i = 0; i < guidCount; i++) SoundGuids[i] = socketMessage.Read<string>();
            var hashCount = socketMessage.Read<int>();
            ContentHashes = new string[hashCount];
            for (var i = 0; i < hashCount; i++) ContentHashes[i] = socketMessage.Read<string>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Host → All: "play this sound on these enemies"
    // ─────────────────────────────────────────────────────────────────────────────
    public class SyncPlayCommandPacket : NetworkPacket<SyncPlayCommandPacket>
    {
        public string SoundGuid { get; set; }
        public int[] EnemyViewIds { get; set; }
        public int HostActorNumber { get; set; }
        /// <summary>
        /// Voice filter mode decided by host so all clients apply the same effect.
        /// -1 = no filter, 0 = pitch down (×0.5), 1 = pitch up (×1.2), 2 = alien.
        /// </summary>
        public int VoiceFilterMode { get; set; }

        protected override void WriteData(SocketMessage socketMessage)
        {
            socketMessage.Write(HostActorNumber);
            socketMessage.Write(SoundGuid);
            socketMessage.Write(EnemyViewIds?.Length ?? 0);
            foreach (var id in EnemyViewIds ?? System.Array.Empty<int>())
                socketMessage.Write(id);
            socketMessage.Write(VoiceFilterMode);
        }

        protected override void ReadData(SocketMessage socketMessage)
        {
            HostActorNumber = socketMessage.Read<int>();
            SoundGuid = socketMessage.Read<string>();
            var count = socketMessage.Read<int>();
            EnemyViewIds = new int[count];
            for (var i = 0; i < count; i++) EnemyViewIds[i] = socketMessage.Read<int>();
            VoiceFilterMode = socketMessage.Read<int>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Packet handlers + notification helpers
    // ─────────────────────────────────────────────────────────────────────────────
    public partial class TFS_Mimics
    {
        // ── Handlers ─────────────────────────────────────────────────────────────
        internal static void OnMimicsAudioPacketReceived(MimicsAudioPacket packet)
        {
            var localInstance = Instance;
            if (localInstance == null)
                return;

            var sender = PhotonNetwork.PlayerList?.FirstOrDefault(p => p.ActorNumber == packet.SenderActorNumber);
            if (sender == null)
                return;

            localInstance.ReceiveAudioChunkInternal(
                packet.ChunkData,
                packet.ChunkIndex,
                packet.TotalChunks,
                false,
                packet.SampleRate,
                packet.TransmissionId,
                sender);
        }

        internal static void OnSoundReadyPacketReceived(SoundReadyPacket packet)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (Instance == null) return;
            Instance.RegisterVoiceSoundReady(packet.SoundGuid, packet.SenderActorNumber);
        }

        internal static void OnCustomSoundManifestReceived(CustomSoundManifestPacket packet)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (Instance == null) return;
            Instance.ProcessCustomSoundManifest(packet);
        }

        internal static void OnSyncPlayCommandReceived(SyncPlayCommandPacket packet)
        {
            Instance?.HandleSyncPlayCommand(packet);
        }

        // ── Helpers called from audio pipeline ────────────────────────────────────
        internal void NotifyHostSoundReady(string soundGuid)
        {
            if (string.IsNullOrEmpty(soundGuid)) return;
            var localActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            if (localActor < 0) return;

            if (PhotonNetwork.IsMasterClient)
            {
                RegisterVoiceSoundReady(soundGuid, localActor);
            }
            else
            {
                var packet = new SoundReadyPacket { SoundGuid = soundGuid, SenderActorNumber = localActor };
                RepoSteamNetwork.SendPacket(packet, NetworkDestination.HostOnly);
                DLog($"NotifyHostSoundReady: sent guid={soundGuid} actor={localActor}");
            }
        }

        internal void RegisterCustomSoundsWithHost()
        {
            if (_customAudioClips.Count == 0) return;
            var localActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            if (localActor < 0) return;

            if (PhotonNetwork.IsMasterClient)
            {
                foreach (var entry in _customAudioClips)
                {
                    if (string.IsNullOrEmpty(entry.SoundGuid)) continue;
                    if (!soundReadinessMap.TryGetValue(entry.SoundGuid, out var set))
                    {
                        set = new HashSet<int>();
                        soundReadinessMap[entry.SoundGuid] = set;
                    }
                    set.Add(localActor);
                }
                DLog($"RegisterCustomSoundsWithHost: host registered {_customAudioClips.Count} custom sounds directly");
            }
            else
            {
                var guids = _customAudioClips.Where(e => !string.IsNullOrEmpty(e.SoundGuid)).Select(e => e.SoundGuid).ToArray();
                var hashes = _customAudioClips.Where(e => !string.IsNullOrEmpty(e.SoundGuid)).Select(e => e.ContentHash ?? string.Empty).ToArray();
                if (guids.Length == 0) return;

                var packet = new CustomSoundManifestPacket
                {
                    SoundGuids = guids,
                    ContentHashes = hashes,
                    SenderActorNumber = localActor
                };
                RepoSteamNetwork.SendPacket(packet, NetworkDestination.HostOnly);
                DLog($"RegisterCustomSoundsWithHost: sent {guids.Length} custom sounds to host");
            }
        }

        // ── Host-side processing ──────────────────────────────────────────────────
        private void RegisterVoiceSoundReady(string soundGuid, int actorNumber)
        {
            if (string.IsNullOrEmpty(soundGuid) || actorNumber < 0) return;
            if (!soundReadinessMap.TryGetValue(soundGuid, out var set))
            {
                set = new HashSet<int>();
                soundReadinessMap[soundGuid] = set;
            }
            set.Add(actorNumber);
            DLog($"RegisterVoiceSoundReady: guid={soundGuid} actor={actorNumber} totalHave={set.Count}");
        }

        private void ProcessCustomSoundManifest(CustomSoundManifestPacket packet)
        {
            var guids = packet.SoundGuids;
            var hashes = packet.ContentHashes;
            var senderActor = packet.SenderActorNumber;
            var hostActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

            if (guids == null) return;

            for (var i = 0; i < guids.Length; i++)
            {
                var guid = guids[i];
                if (string.IsNullOrEmpty(guid)) continue;
                var hash = (hashes != null && i < hashes.Length) ? hashes[i] : null;

                if (!soundReadinessMap.TryGetValue(guid, out var set))
                {
                    set = new HashSet<int>();
                    soundReadinessMap[guid] = set;
                }
                set.Add(senderActor);

                // Cross-reference: if host has the same audio under a different GUID, merge
                if (!string.IsNullOrEmpty(hash) && hostActor >= 0)
                {
                    var hostEntry = _customAudioClips.FirstOrDefault(e => e.ContentHash == hash);
                    if (hostEntry != null)
                        set.Add(hostActor);
                }
            }
            DLog($"ProcessCustomSoundManifest: actor={senderActor} registered {guids.Length} custom sounds");
        }
    }
}
