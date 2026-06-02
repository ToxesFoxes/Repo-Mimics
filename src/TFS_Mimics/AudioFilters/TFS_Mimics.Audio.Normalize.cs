using UnityEngine;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        /// <summary>
        /// Peak-normalises a float sample array in-place.
        /// Scales so the loudest sample reaches the configured target (0-100 → 0.0-1.0).
        /// Does nothing if target is 0 or the clip is silent.
        /// </summary>
        internal static void NormalizeSamples(float[] samples)
        {
            if (samples == null || samples.Length == 0) return;

            // 0 = normalization disabled
            var targetInt = Plugin.configNormalizeTarget != null ? Plugin.configNormalizeTarget.Value : 85;
            if (targetInt <= 0) return;

            var target = Mathf.Clamp(targetInt / 100f, 0.001f, 1f);

            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var abs = Mathf.Abs(samples[i]);
                if (abs > peak) peak = abs;
            }

            if (peak < 0.0001f) return;   // silent

            var scale = target / peak;
            if (scale >= 1f && peak >= target) return;  // don't over-amplify

            for (var i = 0; i < samples.Length; i++)
                samples[i] *= scale;
        }
    }
}
