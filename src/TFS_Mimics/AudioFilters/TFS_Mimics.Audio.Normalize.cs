using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        /// <summary>
        /// Brings speech to a target average loudness (RMS of the voiced part), like voice-chat AGC does,
        /// limited so peaks stay under 0.95 and quiet noise isn't boosted more than 12×. In-place.
        /// </summary>
        internal static void NormalizeSpeechLevel(float[] samples, float targetRms)
        {
            if (samples == null || samples.Length == 0 || targetRms <= 0f) return;

            // RMS over 20 ms frames that carry speech, so pauses inside the phrase don't drag the level down.
            const int frame = 320;
            double voicedSum = 0;
            var voicedCount = 0;
            var peak = 0f;
            var frameRms = new List<float>();
            for (var start = 0; start + frame <= samples.Length; start += frame)
            {
                double sum = 0;
                for (var i = start; i < start + frame; i++)
                {
                    var a = Mathf.Abs(samples[i]);
                    if (a > peak) peak = a;
                    sum += samples[i] * samples[i];
                }
                frameRms.Add((float)System.Math.Sqrt(sum / frame));
            }
            if (frameRms.Count == 0 || peak <= 0f) return;

            var loudest = frameRms.Max();
            for (var f = 0; f < frameRms.Count; f++)
            {
                if (frameRms[f] < loudest * 0.25f) continue;
                voicedSum += frameRms[f] * frameRms[f];
                voicedCount++;
            }
            var rms = voicedCount > 0 ? (float)System.Math.Sqrt(voicedSum / voicedCount) : loudest;
            if (rms <= 0f) return;

            var gain = Mathf.Min(targetRms / rms, 0.95f / peak, 12f);
            for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
        }

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
