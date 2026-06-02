using System;

namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Each entry: (key, apply delegate(samples, sampleRate))
        // To add a new filter — add one line here. No other changes needed.
        internal static readonly (string Key, Func<float[], int, float[]> Apply)[] Registry =
        {
            ("pitch_down",  (s, sr) => ApplyPitchShift(s, 0.5f)),
            ("pitch_up",    (s, sr) => ApplyPitchShift(s, 1.2f)),
            ("alien",       ApplyAlienFilter),
            ("teeth_bot",   ApplyTeethBotFilter),
            ("slow_mouth",  ApplySlowMouthFilter),
            ("tranq",       ApplyTranqFilter),
        };

        internal static int Count => Registry.Length;

        // Apply filter by index. Returns samples unchanged if index is out of range.
        internal static float[] Apply(float[] samples, int filterIndex, int sampleRate)
        {
            if (filterIndex < 0 || filterIndex >= Registry.Length) return samples;
            return Registry[filterIndex].Apply(samples, sampleRate);
        }
    }
}
