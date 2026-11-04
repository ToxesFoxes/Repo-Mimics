namespace TFS_Mimics
{
    internal static partial class AudioFilters
    {
        // Downsampling for storage: anti-alias low-pass just under the new Nyquist, then linear interpolation.
        // Upsampling isn't needed (Unity resamples a lower-rate AudioClip on playback), so it returns the input.
        internal static float[] Resample(float[] samples, int fromRate, int toRate)
        {
            if (samples == null || samples.Length == 0 || fromRate <= 0 || toRate <= 0 || toRate >= fromRate)
            {
                return samples;
            }

            var filtered = ApplyLowPassFilter(samples, toRate * 0.45f, fromRate);
            var ratio = fromRate / (double)toRate;
            var length = (int)(filtered.Length / ratio);
            var output = new float[length];
            for (var i = 0; i < length; i++)
            {
                var pos = i * ratio;
                var index = (int)pos;
                var frac = (float)(pos - index);
                var a = filtered[index];
                var b = index + 1 < filtered.Length ? filtered[index + 1] : a;
                output[i] = a + (b - a) * frac;
            }

            return output;
        }
    }
}
