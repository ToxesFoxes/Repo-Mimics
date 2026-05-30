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
            var fadeSamples = (int)(senderSampleRate * 0.02f);
            var silencePadding = (int)(senderSampleRate * 0.5f);
            var sampleCount = bytes.Length / 2;
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            }

            samples = ApplyLowPassFilter(samples, 4500f);

            if (applyVoiceFilter)
            {
                var mode = UnityEngine.Random.Range(0, 3);
                if (mode == 0)
                {
                    samples = ApplyPitchShift(samples, 0.5f);
                }
                else if (mode == 1)
                {
                    samples = ApplyPitchShift(samples, 1.2f);
                }
                else
                {
                    samples = ApplyAlienFilter(samples);
                }
            }

            NormalizeSamples(samples);

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

        // Cubic (Hermite) 4-point resampling pitch shift.
        // Different approach from linear interpolation: uses surrounding sample context
        // for smoother results on voiced speech.
        private static float[] ApplyPitchShift(float[] samples, float pitchFactor)
        {
            var newLength = (int)(samples.Length / pitchFactor);
            var output = new float[newLength];

            for (var i = 0; i < newLength; i++)
            {
                var srcPos = i * pitchFactor;
                var idx = (int)srcPos;
                var t = srcPos - idx;

                var s0 = idx > 0 ? samples[idx - 1] : samples[0];
                var s1 = idx < samples.Length ? samples[idx] : 0f;
                var s2 = idx + 1 < samples.Length ? samples[idx + 1] : 0f;
                var s3 = idx + 2 < samples.Length ? samples[idx + 2] : 0f;

                // Catmull-Rom spline
                var a = -0.5f * s0 + 1.5f * s1 - 1.5f * s2 + 0.5f * s3;
                var b = s0 - 2.5f * s1 + 2f * s2 - 0.5f * s3;
                var c = -0.5f * s0 + 0.5f * s2;
                var d = s1;
                output[i] = Mathf.Clamp(((a * t + b) * t + c) * t + d, -1f, 1f);
            }

            return output;
        }

        // Formant-shifting distortion: chorus + bit-crush noise layer.
        // Produces an alien/uncanny timbre without relying on ring-modulation.
        private float[] ApplyAlienFilter(float[] samples)
        {
            var output = new float[samples.Length];

            // Chorus parameters
            const float chorusRateHz = 1.3f;
            const float chorusDepthMs = 8f;
            const float chorusMix = 0.45f;
            var maxDelaySamples = (int)(sampleRate * chorusDepthMs / 1000f) + 2;
            var delayBuf = new float[maxDelaySamples];
            var writeHead = 0;

            // Bit-crush depth (reduces to ~10-bit)
            const float crushSteps = 1024f;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)sampleRate;

                // Modulated delay read position (chorus)
                var modDepth = (int)(sampleRate * chorusDepthMs / 1000f);
                var lfo = (Mathf.Sin(MathF.PI * 2f * chorusRateHz * t) + 1f) * 0.5f;
                var delaySamples = (int)(modDepth * lfo) + 1;
                var readHead = (writeHead - delaySamples + maxDelaySamples) % maxDelaySamples;

                var dry = samples[i];
                delayBuf[writeHead] = dry;
                writeHead = (writeHead + 1) % maxDelaySamples;

                var chorus = delayBuf[readHead];

                // Bit-crush applied to the chorus layer only
                var crushed = Mathf.Round(chorus * crushSteps) / crushSteps;

                output[i] = Mathf.Clamp(dry * (1f - chorusMix) + crushed * chorusMix, -1f, 1f);
            }

            return output;
        }

        private const float NormalizeTarget = 0.85f;   // peak target, 15% headroom

        /// <summary>
        /// Peak-normalises a float sample array in-place.
        /// Scales so the loudest sample reaches NormalizeTarget.
        /// Does nothing if the clip is silent or already at/above target.
        /// </summary>
        internal static void NormalizeSamples(float[] samples)
        {
            if (samples == null || samples.Length == 0) return;

            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var abs = Mathf.Abs(samples[i]);
                if (abs > peak) peak = abs;
            }

            if (peak < 0.0001f) return;   // silent

            var scale = NormalizeTarget / peak;
            if (scale >= 1f && peak >= NormalizeTarget) return;  // don't over-amplify

            for (var i = 0; i < samples.Length; i++)
                samples[i] *= scale;
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

        // Butterworth 2nd-order low-pass filter (single forward pass, no phase distortion hack).
        // Coefficients derived from bilinear transform at the given cutoff.
        private float[] ApplyLowPassFilter(float[] samples, float cutoffFreq)
        {
            var output = new float[samples.Length];
            if (samples.Length == 0)
            {
                return output;
            }

            // Standard bilinear-transform Butterworth 2nd-order LPF.
            // k = tan(π * fc / fs) is the pre-warped normalised cutoff (small positive value).
            var fs = sampleRate > 0 ? sampleRate : 48000f;
            var k = Mathf.Tan(MathF.PI * cutoffFreq / fs);
            var k2 = k * k;
            var sqrt2k = MathF.Sqrt(2f) * k;
            var norm = 1f / (k2 + sqrt2k + 1f);

            var b0 = k2 * norm;
            var b1 = 2f * k2 * norm;
            var b2 = k2 * norm;
            var a1 = 2f * (k2 - 1f) * norm;
            var a2 = (k2 - sqrt2k + 1f) * norm;

            var x1 = 0f; var x2 = 0f;
            var y1 = 0f; var y2 = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var x0 = samples[i];
                var y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x0;
                y2 = y1; y1 = y0;
                output[i] = Mathf.Clamp(y0, -1f, 1f);
            }

            return output;
        }
    }
}