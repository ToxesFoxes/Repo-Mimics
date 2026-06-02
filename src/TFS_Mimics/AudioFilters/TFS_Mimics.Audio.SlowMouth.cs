namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Slow-mouth effect: pitch down ×0.75 (matches EnemySlowMouth.OverridePitch(0.75f)).
        // Low-pass at 3000 Hz adds muffled resonance that emphasises the "deep throat" quality.
        internal static float[] ApplySlowMouthFilter(float[] samples, int sampleRate)
        {
            samples = ApplyLowPassFilter(samples, 3000f, sampleRate);
            return ApplyPitchShift(samples, 0.75f);
        }
    }
}
