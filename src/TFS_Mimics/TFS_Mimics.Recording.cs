using System;
using System.Collections;
using Photon.Pun;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
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

        // Cached reflection handles for checking PlayerVoiceChat.recorder.TransmitEnabled.
        private System.Reflection.FieldInfo _recorderField;
        private System.Reflection.PropertyInfo _recorderTransmitProp;
        private bool _recorderReflectionDone;

        // Returns true when the player has muted their microphone.
        // Uses lazy-initialised reflection into PlayerVoiceChat.recorder so the
        // hot path (ProcessVoiceData) avoids type-scanning after the first call.
        private bool IsMicrophoneMuted()
        {
            if (playerVoiceChat == null)
            {
                return false;
            }

            if (!_recorderReflectionDone)
            {
                _recorderReflectionDone = true;
                _recorderField = typeof(PlayerVoiceChat).GetField(
                    "recorder",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                if (_recorderField != null)
                {
                    var recorderObj = _recorderField.GetValue(playerVoiceChat);
                    if (recorderObj != null)
                    {
                        _recorderTransmitProp = recorderObj.GetType().GetProperty(
                            "TransmitEnabled",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public);
                    }
                }

                DLog($"IsMicrophoneMuted init: recorderField={_recorderField != null} transmitProp={_recorderTransmitProp != null} {DebugContext()}");
            }

            if (_recorderField == null || _recorderTransmitProp == null)
            {
                return false;
            }

            try
            {
                var recorder = _recorderField.GetValue(playerVoiceChat);
                if (recorder == null)
                {
                    return false;
                }

                return !(bool)_recorderTransmitProp.GetValue(recorder);
            }
            catch
            {
                return false;
            }
        }

        private void AbortCapture()
        {
            if (!capturingSpeech)
            {
                return;
            }

            DLog($"AbortCapture: discarding bufferedSamples={bufferPosition} {DebugContext()}");
            capturingSpeech = false;
            bufferPosition = 0;
            fileSaved = false;
            vadHoldUntil = 0f;
            pendingSilenceSamples = 0;
            postSpeechSamplesRemaining = 0;
        }

        public void ProcessVoiceData(short[] voiceData)
        {
            if (!isRecording || !photonView.IsMine)
            {
                return;
            }

            // If the player mutes mid-capture — discard the in-progress buffer and
            // reset state so recording starts clean when they unmute.
            if (IsMicrophoneMuted())
            {
                AbortCapture();
                return;
            }

            PushToPreSpeechBuffer(voiceData);

            // RMS energy-based voice activity detection — no reflection into PlayerVoiceChat internals.
            var energy = ComputeRmsEnergy(voiceData);
            if (energy > VadEnergyThreshold)
            {
                vadHoldUntil = Time.time + VadHoldSeconds;
            }

            var isTalking = Time.time < vadHoldUntil;

            if (isTalking && !capturingSpeech)
            {
                capturingSpeech = true;
                bufferPosition = 0;
                PrependPreSpeechToCapture();
                pendingSilenceSamples = 0;
                postSpeechSamplesRemaining = 0;
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
                pendingSilenceSamples = 0;
                copyCount = AppendVoiceDataToCapture(voiceData);
            }
            else
            {
                // Finalize immediately once VAD hold has expired — no post-speech merge window.
                if (bufferPosition > sampleRate / 4 && !fileSaved)
                {
                    DLog($"Speech ended (VAD hold expired): finalize bufferedSamples={bufferPosition} {DebugContext()}");
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
            // TryWriteDebugWav("rec", audioBytes, sampleRate);
            if (_isSendingAudio)
            {
                if (_sendQueue.Count >= MaxSendQueueDepth)
                {
                    var dropped = _sendQueue.Dequeue();
                    Log.LogWarning($"SendQueue full (depth={MaxSendQueueDepth}): dropped oldest entry ({dropped.Length} bytes) {DebugContext()}");
                }
                _sendQueue.Enqueue(audioBytes);
                Log.LogInfo($"SendQueue: enqueued {audioBytes.Length} bytes (queued={_sendQueue.Count}) {DebugContext()}");
            }
            else
            {
                StartCoroutine(SendAudioInChunks(audioBytes));
            }
            StartRecording();
        }

        // Voice activity detection using per-frame RMS energy.
        // This avoids reflecting into PlayerVoiceChat private state entirely.
        private const float VadEnergyThreshold = 0.012f;
        private const float VadHoldSeconds = 0.5f;

        private static float ComputeRmsEnergy(short[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return 0f;
            }

            double sumSq = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var normalized = samples[i] / 32768.0;
                sumSq += normalized * normalized;
            }

            return (float)Math.Sqrt(sumSq / samples.Length);
        }

        // Steam Networking chunk size — 200 KB keeps us well under the ~512 KB Steam message limit.
        private const int SteamChunkSize = 204800;

        private IEnumerator SendAudioInChunks(byte[] audioData)
        {
            _isSendingAudio = true;
            var chunks = ChunkAudioData(audioData, SteamChunkSize);
            var transmissionId = Guid.NewGuid().ToString("N");
            var localPlayer = PhotonNetwork.LocalPlayer;
            var localPlayerId = GetPlayerPersistentId(localPlayer);
            var localPlayerName = GetPlayerDisplayName(localPlayer);
            var destination = Plugin.configHearYourself.Value ? NetworkDestination.Everyone : NetworkDestination.EveryoneExcludingSender;
            Log.LogInfo($"Start sending audio [{localPlayerName}]({localPlayerId}) tx={transmissionId} bytes={audioData.Length} chunks={chunks.Count} destination={destination} {DebugContext()}");
            DLog($"SendAudioInChunks begin: totalBytes={audioData.Length} chunks={chunks.Count} chunkSize={SteamChunkSize} {DebugContext()}");

            for (var i = 0; i < chunks.Count; i++)
            {
                var packet = new MimicsAudioPacket
                {
                    ChunkData = chunks[i],
                    ChunkIndex = i,
                    TotalChunks = chunks.Count,
                    SampleRate = sampleRate,
                    TransmissionId = transmissionId,
                    SenderActorNumber = localPlayer.ActorNumber
                };

                RepoSteamNetwork.SendPacket(packet, destination);
                DLog($"Steam packet sent: tx={transmissionId} chunk={i + 1}/{chunks.Count} bytes={chunks[i].Length} destination={destination} {DebugContext()}");
                PushVoiceLog(false, transmissionId, localPlayerId, localPlayerName, audioData.Length, false, i + 1, chunks.Count);
            }

            Log.LogInfo($"Finished sending audio [{localPlayerName}]({localPlayerId}) tx={transmissionId} chunksSent={chunks.Count} {DebugContext()}");
            PushVoiceLog(false, transmissionId, localPlayerId, localPlayerName, audioData.Length, true, chunks.Count, chunks.Count);
            _isSendingAudio = false;

            if (_sendQueue.Count > 0)
            {
                var next = _sendQueue.Dequeue();
                Log.LogInfo($"SendQueue: starting next transmission ({next.Length} bytes, remaining={_sendQueue.Count}) {DebugContext()}");
                StartCoroutine(SendAudioInChunks(next));
            }

            yield break;
        }
    }
}