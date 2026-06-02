using System;
using UnityEngine;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Chattering-teeth effect: pitch up ×1.25 (matches ValuableTeethBot.OverridePitch(1.25f))
        // + tremolo at ~10 Hz to simulate the rapid chattering amplitude variation.
        internal static float[] ApplyTeethBotFilter(float[] samples, int sampleRate)
        {
            // Pitch up matching the in-game OverridePitch value
            samples = ApplyPitchShift(samples, 1.25f);

            // Tremolo — rapid amplitude chatter at ~10 Hz, depth 0.25
            const float tremoloRateHz = 10f;
            const float tremoloDepth = 0.25f;
            var output = new float[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)sampleRate;
                var lfo = (Mathf.Sin(MathF.PI * 2f * tremoloRateHz * t) + 1f) * 0.5f; // 0..1
                var gain = 1f - tremoloDepth * lfo;                                    // 0.75..1.0
                output[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }
            return output;
        }
    }
}
