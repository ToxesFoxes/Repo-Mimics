using System.Collections.Generic;
using UnityEngine;

namespace TFS_Mimics
{
    /// <summary>
    /// Stage 2 lip-sync: remembers which of our AudioSources is currently "speaking" on which enemy,
    /// so enemies with a code-driven jaw (Bang, small Elsa, Oogly, Tricycle rider) move their mouth to the stolen voice.
    /// Runs on every client — each client plays the clip locally, so each animates its own jaws.
    /// </summary>
    internal static class MimicMouths
    {
        private static readonly Dictionary<int, AudioSource> SpeakingByEnemy = new Dictionary<int, AudioSource>();
        private static readonly float[] Samples = new float[1024];
        private static readonly float[] Spectrum = new float[1024];

        private static Enemy ResolveEnemy(GameObject enemyGo)
        {
            if (enemyGo == null) return null;
            return enemyGo.GetComponentInChildren<Enemy>(true) ?? enemyGo.GetComponentInParent<Enemy>();
        }

        internal static void Attach(GameObject enemyGo, AudioSource source)
        {
            if (enemyGo == null || source == null) return;
            var enemy = ResolveEnemy(enemyGo);
            SpeakingByEnemy[enemy != null ? enemy.GetInstanceID() : enemyGo.GetInstanceID()] = source;
            if (enemy == null) return; // a thrown Bomb Thrower head

            // Tricycle sums the spectrum of every source in this list for its rider's jaw — just join the list.
            var tricycle = enemy.GetComponent<EnemyTricycle>();
            if (tricycle != null && tricycle.talkingAudioSources != null && !tricycle.talkingAudioSources.Contains(source))
                tricycle.talkingAudioSources.Add(source);
        }

        private static AudioSource GetSpeaking(Enemy enemy)
        {
            if (enemy == null || !SpeakingByEnemy.TryGetValue(enemy.GetInstanceID(), out var source)) return null;
            if (source == null)
            {
                SpeakingByEnemy.Remove(enemy.GetInstanceID());
                return null;
            }
            return source.isPlaying && source.clip != null ? source : null;
        }

        internal static bool IsSpeaking(Enemy enemy) => GetSpeaking(enemy) != null;

        internal static bool IsSpeakingOn(GameObject go)
        {
            if (go == null) return false;
            var enemy = ResolveEnemy(go);
            if (enemy != null) return IsSpeaking(enemy);
            return SpeakingByEnemy.TryGetValue(go.GetInstanceID(), out var source) && source != null && source.isPlaying;
        }

        // Same measure the game uses for Bang/Elsa: mean |sample| over 1024 samples at the play head.
        internal static float ClipLoudness(Enemy enemy)
        {
            var source = GetSpeaking(enemy);
            if (source == null) return 0f;

            var clip = source.clip;
            var start = Mathf.Clamp(source.timeSamples, 0, Mathf.Max(0, clip.samples - Samples.Length));
            if (!clip.GetData(Samples, start)) return 0f;

            var sum = 0f;
            for (var i = 0; i < Samples.Length; i++) sum += Mathf.Abs(Samples[i]);
            return sum / Samples.Length;
        }

        // Same measure Oogly/Tricycle use: lowest spectrum bin × 100000.
        internal static float SpectrumVolume(Enemy enemy)
        {
            var source = GetSpeaking(enemy);
            if (source == null) return 0f;
            source.GetSpectrumData(Spectrum, 0, FFTWindow.Hamming);
            return Spectrum[0] * 100000f;
        }

        internal static void Clear() => SpeakingByEnemy.Clear();
    }
}
