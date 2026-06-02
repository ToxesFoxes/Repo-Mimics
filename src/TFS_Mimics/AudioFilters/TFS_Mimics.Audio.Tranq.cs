namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Tranq effect: pitch down ×0.65 (matches ItemGunTranq.SlowDownVoiceRPC → OverridePitch(0.65f))
        // + heavy low-pass at 1800 Hz for a thick, sedated timbre.
        internal static float[] ApplyTranqFilter(float[] samples, int sampleRate)
        {
            samples = ApplyLowPassFilter(samples, 1800f, sampleRate);
            return ApplyPitchShift(samples, 0.65f);
        }
    }
}
