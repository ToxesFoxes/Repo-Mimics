using System;
using System.Collections;
using System.Reflection;
using Photon.Pun;
using Photon.Voice;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        private void StartRecording()
        {
            if (isRecording)
            {
                return;
            }

            audioBuffer = new float[sampleRate * RecordingBufferSeconds];
            preSpeechBuffer = new float[Mathf.Max(1, (int)(sampleRate * 0.25f))];
            bufferPosition = 0;
            preSpeechWritePos = 0;
            preSpeechCount = 0;
            postSpeechSamplesRemaining = 0;
            pendingSilenceSamples = 0;
            isRecording = true;
            capturingSpeech = false;
            fileSaved = false;
            DLog($"StartRecording: bufferSamples={audioBuffer.Length} (~{audioBuffer.Length / (float)sampleRate:F2}s) {DebugContext()}");
        }

        private void PushToPreSpeechBuffer(short[] voiceData)
        {
            if (preSpeechBuffer == null || preSpeechBuffer.Length == 0 || voiceData == null || voiceData.Length == 0)
            {
                return;
            }

            for (var i = 0; i < voiceData.Length; i++)
            {
                preSpeechBuffer[preSpeechWritePos] = voiceData[i] / 32768f;
                preSpeechWritePos = (preSpeechWritePos + 1) % preSpeechBuffer.Length;
                if (preSpeechCount < preSpeechBuffer.Length)
                {
                    preSpeechCount++;
                }
            }
        }

        private void PrependPreSpeechToCapture()
        {
            if (preSpeechBuffer == null || preSpeechCount <= 0 || audioBuffer == null)
            {
                return;
            }

            var available = audioBuffer.Length - bufferPosition;
            var copyCount = Mathf.Min(preSpeechCount, available);
            if (copyCount <= 0)
            {
                return;
            }

            var start = (preSpeechWritePos - preSpeechCount + preSpeechBuffer.Length) % preSpeechBuffer.Length;
            for (var i = 0; i < copyCount; i++)
            {
                var idx = (start + i) % preSpeechBuffer.Length;
                audioBuffer[bufferPosition + i] = preSpeechBuffer[idx];
            }

            bufferPosition += copyCount;
        }

        private int AppendVoiceDataToCapture(short[] voiceData)
        {
            if (voiceData == null || voiceData.Length == 0 || audioBuffer == null || bufferPosition >= audioBuffer.Length)
            {
                return 0;
            }

            var copyCount = Mathf.Min(voiceData.Length, audioBuffer.Length - bufferPosition);
            for (var i = 0; i < copyCount; i++)
            {
                audioBuffer[bufferPosition + i] = voiceData[i] / 32768f;
            }

            bufferPosition += copyCount;
            return copyCount;
        }

        private int AppendSilenceToCapture(int sampleCount)
        {
            if (sampleCount <= 0 || audioBuffer == null || bufferPosition >= audioBuffer.Length)
            {
                return 0;
            }

            var copyCount = Mathf.Min(sampleCount, audioBuffer.Length - bufferPosition);
            for (var i = 0; i < copyCount; i++)
            {
                audioBuffer[bufferPosition + i] = 0f;
            }

            bufferPosition += copyCount;
            return copyCount;
        }

        public void ProcessVoiceData(short[] voiceData)
        {
            if (!isRecording || !photonView.IsMine || playerVoiceChat == null)
            {
                return;
            }

            PushToPreSpeechBuffer(voiceData);

            var isTalkingField = typeof(PlayerVoiceChat).GetField("isTalking", BindingFlags.Instance | BindingFlags.NonPublic);
            var isTalking = isTalkingField != null && (bool)isTalkingField.GetValue(playerVoiceChat);

            if (isTalking && !capturingSpeech)
            {
                capturingSpeech = true;
                bufferPosition = 0;
                PrependPreSpeechToCapture();
                postSpeechSamplesRemaining = Mathf.Max(1, (int)(sampleRate * SilenceMergeSeconds));
                var localPlayer = PhotonNetwork.LocalPlayer;
                var localPlayerId = GetPlayerPersistentId(localPlayer);
                var localPlayerName = GetPlayerDisplayName(localPlayer);
                Log.LogInfo($"Started recording player [{localPlayerName}]({localPlayerId}) {DebugContext()}");
                DLog($"Speech detected: begin capture voiceDataSamples={voiceData.Length} {DebugContext()}");
            }

            if (!capturingSpeech)
            {
                return;
            }

            var copyCount = 0;
            if (isTalking)
            {
                if (pendingSilenceSamples > 0)
                {
                    var bridgeMaxSamples = Mathf.Max(1, (int)(sampleRate * MergeSilenceBridgeSeconds));
                    var bridgeSamples = Mathf.Min(pendingSilenceSamples, bridgeMaxSamples);
                    AppendSilenceToCapture(bridgeSamples);
                    pendingSilenceSamples = 0;
                }

                copyCount = AppendVoiceDataToCapture(voiceData);
                postSpeechSamplesRemaining = Mathf.Max(1, (int)(sampleRate * SilenceMergeSeconds));
            }
            else
            {
                pendingSilenceSamples = Mathf.Min(
                    pendingSilenceSamples + voiceData.Length,
                    Mathf.Max(1, (int)(sampleRate * SilenceMergeSeconds))
                );
                postSpeechSamplesRemaining -= voiceData.Length;
                if (postSpeechSamplesRemaining <= 0 && bufferPosition > sampleRate / 4 && !fileSaved)
                {
                    DLog($"Speech ended after smart silence-merge window: finalize bufferedSamples={bufferPosition} mergeSilenceSec={SilenceMergeSeconds:F1} bridgeSec={MergeSilenceBridgeSeconds:F1} {DebugContext()}");
                    FinalizeCaptureAndSend(bufferPosition);
                    return;
                }
            }

            if (bufferPosition % (sampleRate / 2) < copyCount)
            {
                DLog($"Capture progress: bufferPosition={bufferPosition}/{audioBuffer.Length} copiedNow={copyCount} {DebugContext()}");
            }

            if (bufferPosition < audioBuffer.Length || fileSaved)
            {
                return;
            }

            DLog($"Capture finished by full buffer: totalSamples={audioBuffer.Length} {DebugContext()}");
            FinalizeCaptureAndSend(audioBuffer.Length);
        }

        private void FinalizeCaptureAndSend(int usedSamples)
        {
            if (fileSaved || usedSamples <= 0)
            {
                return;
            }

            var finalSamples = new float[usedSamples];
            Array.Copy(audioBuffer, finalSamples, usedSamples);

            isRecording = false;
            capturingSpeech = false;
            fileSaved = true;

            var audioBytes = ConvertFloatArrayToByteArray(finalSamples);
            var durationSec = sampleRate > 0 ? usedSamples / (float)sampleRate : 0f;
            var localPlayer = PhotonNetwork.LocalPlayer;
            var localPlayerId = GetPlayerPersistentId(localPlayer);
            var localPlayerName = GetPlayerDisplayName(localPlayer);
            Log.LogInfo($"Finished recording player [{localPlayerName}]({localPlayerId}) duration={durationSec:F2}s bytes={audioBytes.Length} {DebugContext()}");
            DLog($"FinalizeCaptureAndSend: usedSamples={usedSamples} bytes={audioBytes.Length} {DebugContext()}");
            StartCoroutine(SendAudioInChunks(audioBytes));
            StartRecording();
        }

        private IEnumerator SendAudioInChunks(byte[] audioData)
        {
            var chunks = ChunkAudioData(audioData, 8192);
            var transmissionId = Guid.NewGuid().ToString("N");
            var localPlayer = PhotonNetwork.LocalPlayer;
            var localPlayerId = GetPlayerPersistentId(localPlayer);
            var localPlayerName = GetPlayerDisplayName(localPlayer);
            var target = Plugin.configHearYourself.Value ? RpcTarget.All : RpcTarget.Others;
            Log.LogInfo($"Start sending audio to players [{localPlayerName}]({localPlayerId}) tx={transmissionId} bytes={audioData.Length} chunks={chunks.Count} target={target} {DebugContext()}");
            DLog($"SendAudioInChunks begin: totalBytes={audioData.Length} chunks={chunks.Count} chunkSize=8192 {DebugContext()}");

            for (var i = 0; i < chunks.Count; i++)
            {
                if (!PhotonNetwork.IsConnectedAndReady)
                {
                    Log.LogWarning($"Photon disconnected during send at chunk={i}/{chunks.Count} {DebugContext()}");
                    yield break;
                }

                var applyVoiceFilter = false;

                photonView.RPC(
                    "ReceiveAudioChunkV2",
                    target,
                    chunks[i],
                    i,
                    chunks.Count,
                    applyVoiceFilter,
                    sampleRate,
                    transmissionId
                );

                DLog($"RPC sent: tx={transmissionId} chunk={i + 1}/{chunks.Count} bytes={chunks[i].Length} target={target} applyVoiceFilter={applyVoiceFilter} senderSampleRate={sampleRate} {DebugContext()}");

                yield return new WaitForSeconds(0.125f);
            }

            Log.LogInfo($"Finished sending audio to players [{localPlayerName}]({localPlayerId}) tx={transmissionId} chunksSent={chunks.Count} target={target} {DebugContext()}");
            DLog($"SendAudioInChunks complete: chunksSent={chunks.Count} {DebugContext()}");
        }
    }
}