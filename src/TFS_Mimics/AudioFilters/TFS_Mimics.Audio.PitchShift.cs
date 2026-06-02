using UnityEngine;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Cubic (Hermite) 4-point resampling pitch shift.
        // Different approach from linear interpolation: uses surrounding sample context
        // for smoother results on voiced speech.
        internal static float[] ApplyPitchShift(float[] samples, float pitchFactor)
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
    }
}
