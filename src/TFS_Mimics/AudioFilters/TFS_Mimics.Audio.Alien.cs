using System;
using UnityEngine;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Formant-shifting distortion: chorus + bit-crush noise layer.
        // Produces an alien/uncanny timbre without relying on ring-modulation.
        internal static float[] ApplyAlienFilter(float[] samples, int sampleRate)
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
    }
}
