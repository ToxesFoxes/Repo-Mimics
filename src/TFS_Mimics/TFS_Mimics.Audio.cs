using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        private static void WriteWavHeader(BinaryWriter writer, int sampleCount, int sampleRate)
        {
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + sampleCount * 2);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(sampleCount * 2);
        }

        private static byte[] ConvertFloatArrayToByteArray(float[] audioData)
        {
            var bytes = new byte[audioData.Length * 2];
            for (var i = 0; i < audioData.Length; i++)
            {
                var value = (short)(audioData[i] * 32767f);
                BitConverter.GetBytes(value).CopyTo(bytes, i * 2);
            }

            return bytes;
        }

        private byte[] CombineChunks(List<byte[]> chunks)
        {
            var total = chunks.Sum(chunk => chunk.Length);
            var output = new byte[total];
            var offset = 0;

            foreach (var chunk in chunks)
            {
                Array.Copy(chunk, 0, output, offset, chunk.Length);
                offset += chunk.Length;
            }

            DLog($"CombineChunks: chunkCount={chunks.Count} totalBytes={total} {DebugContext()}");

            return output;
        }

        private List<byte[]> ChunkAudioData(byte[] audioData, int chunkSize)
        {
            var list = new List<byte[]>();
            for (var i = 0; i < audioData.Length; i += chunkSize)
            {
                var len = Mathf.Min(chunkSize, audioData.Length - i);
                var chunk = new byte[len];
                Array.Copy(audioData, i, chunk, 0, len);
                list.Add(chunk);
            }

            DLog($"ChunkAudioData: inputBytes={audioData.Length} chunkSize={chunkSize} chunkCount={list.Count} {DebugContext()}");

            return list;
        }

        private float[] ConvertByteArrayToFloatArray(byte[] bytes, bool applyVoiceFilter, int senderSampleRate)
        {
            var mode = applyVoiceFilter ? UnityEngine.Random.Range(0, AudioFilters.Count) : -1;
            return ConvertByteArrayToFloatArray(bytes, mode, senderSampleRate);
        }

        // voiceFilterMode: -1 = none, index into AudioFilters.Registry (see AudioFilters/TFS_Mimics.Audio.FilterRegistry.cs)
        private float[] ConvertByteArrayToFloatArray(byte[] bytes, int voiceFilterMode, int senderSampleRate)
        {
            var fadeSamples = (int)(senderSampleRate * 0.02f);
            var silencePadding = (int)(senderSampleRate * 0.5f);
            var sampleCount = bytes.Length / 2;
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            }

            samples = AudioFilters.ApplyLowPassFilter(samples, 4500f, sampleRate);
            samples = AudioFilters.Apply(samples, voiceFilterMode, sampleRate);
            AudioFilters.NormalizeSamples(samples);

            var output = new float[samples.Length + silencePadding * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var gain = 1f;
                if (i < fadeSamples)
                {
                    gain = i / (float)fadeSamples;
                }
                else if (i >= samples.Length - fadeSamples)
                {
                    gain = (samples.Length - i) / (float)fadeSamples;
                }

                output[i + silencePadding] = samples[i] * gain;
            }

            return output;
        }

        // Writes a WAV file to audio-cache/debug/ when verbose logging is active.
        // Useful for verifying recording and playback audio quality outside the game.
        private void TryWriteDebugWav(string prefix, byte[] pcmBytes, int wavSampleRate)
        {
            if (Plugin.configDebugVerbose == null || !Plugin.configDebugVerbose.Value)
            {
                return;
            }

            try
            {
                var dir = Path.Combine(GetAudioCacheDirectoryPath(), "debug");
                Directory.CreateDirectory(dir);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                var fileName = $"{prefix}_{timestamp}.wav";
                var path = Path.Combine(dir, fileName);
                var sampleCount = pcmBytes.Length / 2;

                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(fs))
                {
                    WriteWavHeader(writer, sampleCount, wavSampleRate);
                    writer.Write(pcmBytes);
                }

                DLog($"Debug WAV written: file={fileName} samples={sampleCount} sampleRate={wavSampleRate} {DebugContext()}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Debug WAV write failed: {ex.Message}");
            }
        }

    }
}