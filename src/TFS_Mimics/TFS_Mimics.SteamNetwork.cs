using System.Linq;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking.Serialization;

namespace TFS_Mimics
{
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

    public partial class TFS_Mimics
    {
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
    }
}
