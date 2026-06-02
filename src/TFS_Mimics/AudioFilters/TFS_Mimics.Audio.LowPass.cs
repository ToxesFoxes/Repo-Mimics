using System;
using UnityEngine;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Butterworth 2nd-order low-pass filter (single forward pass, no phase distortion hack).
        // Coefficients derived from bilinear transform at the given cutoff.
        internal static float[] ApplyLowPassFilter(float[] samples, float cutoffFreq, int sampleRate)
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
