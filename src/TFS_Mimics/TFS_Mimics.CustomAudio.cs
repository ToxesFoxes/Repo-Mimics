using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Custom Audio Entry ───────────────────────────────────────────────────
        private sealed class CustomAudioEntry
        {
            public AudioClip Clip;
            public string    FileName;   // e.g. "scary.wav"
            public string    FilePath;   // full path for reload
        }

        // ─── State ───────────────────────────────────────────────────────────────
        private readonly List<CustomAudioEntry> _customAudioClips = new List<CustomAudioEntry>();
        private bool _customAudioLoaded;

        // ─── Path ────────────────────────────────────────────────────────────────
        private string GetCustomAudioDirectoryPath()
        {
            return Path.Combine(Paths.BepInExRootPath, "plugins", "ToxesFoxes-Mimics", "custom-audio");
        }

        // ─── Public entry-point — idempotent ─────────────────────────────────────
        internal void EnsureCustomAudioLoaded()
        {
            if (_customAudioLoaded) return;
            _customAudioLoaded = true;
            StartCoroutine(LoadCustomAudioClipsCoroutine());
        }

        /// <summary>Reload: clears existing clips and re-scans the folder.</summary>
        internal void ReloadCustomAudio()
        {
            // Unload previously loaded clips to free memory
            foreach (var entry in _customAudioClips)
            {
                if (entry?.Clip != null)
                    Destroy(entry.Clip);
            }
            _customAudioClips.Clear();
            _customAudioLoaded = false;
            EnsureCustomAudioLoaded();
        }

        // ─── Coroutine loader ─────────────────────────────────────────────────────
        private IEnumerator LoadCustomAudioClipsCoroutine()
        {
            var dir = GetCustomAudioDirectoryPath();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Log.LogInfo($"[Mimics] Custom audio: created folder at {dir}");
                yield break;
            }

            var files = new List<string>();
            foreach (var pattern in new[] { "*.mp3", "*.wav" })
                files.AddRange(Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly));

            files.Sort(StringComparer.OrdinalIgnoreCase);

            if (files.Count == 0)
            {
                DLog($"CustomAudio: no mp3/wav files found in {dir}");
                yield break;
            }

            Log.LogInfo($"[Mimics] Custom audio: found {files.Count} file(s) in {dir}");

            var loaded = 0;
            foreach (var filePath in files)
            {
                var ext       = Path.GetExtension(filePath).ToLowerInvariant();
                var audioType = ext == ".mp3" ? AudioType.MPEG : AudioType.WAV;
                var url       = "file:///" + filePath.Replace('\\', '/');

                using (var req = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Log.LogWarning($"[Mimics] Custom audio: failed to load '{Path.GetFileName(filePath)}': {req.error}");
                        continue;
                    }

                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null)
                    {
                        Log.LogWarning($"[Mimics] Custom audio: null clip from '{Path.GetFileName(filePath)}'");
                        continue;
                    }

                    NormalizeClip(clip);

                    clip.name = Path.GetFileNameWithoutExtension(filePath);
                    _customAudioClips.Add(new CustomAudioEntry
                    {
                        Clip     = clip,
                        FileName = Path.GetFileName(filePath),
                        FilePath = filePath,
                    });
                    loaded++;
                    DLog($"CustomAudio: loaded '{clip.name}' length={clip.length:F1}s freq={clip.frequency}Hz channels={clip.channels}");
                }
            }

            Log.LogInfo($"[Mimics] Custom audio: loaded {loaded}/{files.Count} file(s) successfully");
        }

        // ─── Playback ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Plays a custom audio clip on the nearest eligible enemy AudioSource.
        /// No online-player check — custom clips are always playable.
        /// </summary>
        private void PlayCustomAudioEntry(CustomAudioEntry entry)
        {
            if (entry?.Clip == null) return;

            var enemies        = GetEnemiesList().Where(e => e != null).ToList();
            var playbackTargets = BuildPlaybackTargets(enemies, true);

            if (playbackTargets.Count == 0)
            {
                hudHasSelectedEnemyPos = false;
                nearestPlaybackTargetsHud.Clear();
                nearestPlaybackTargetsHud.Add("No candidates");
                DLog($"PlayCustomAudioEntry '{entry.FileName}': no targets {DebugContext()}");
                return;
            }

            var listenerPos = transform.position;
            var nearRadius  = Plugin.configPlaybackNearRadius != null ? Plugin.configPlaybackNearRadius.Value : 25f;
            UpdateNearestPlaybackTargetsHud(playbackTargets, listenerPos, nearRadius);

            var inRadiusCandidates = nearestPlaybackCandidatesHud
                .Where(c => c != null && c.Distance <= nearRadius && !c.IsPlaying)
                .ToList();

            if (inRadiusCandidates.Count == 0)
            {
                DLog($"PlayCustomAudioEntry '{entry.FileName}': no candidates inside radius={nearRadius:F1} {DebugContext()}");
                return;
            }

            var selected = inRadiusCandidates[UnityEngine.Random.Range(0, inRadiusCandidates.Count)];
            var source   = GetOrCreateReusableEnemyAudioSource(selected.Enemy, selected.Target, selected.Position);
            if (source == null)
            {
                Log.LogWarning($"[Mimics] PlayCustomAudioEntry '{entry.FileName}': failed to get AudioSource {DebugContext()}");
                return;
            }

            source.clip                  = entry.Clip;
            source.volume                = Mathf.Clamp01(Plugin.configVoiceVolume.Value / 20f);
            source.mute                  = false;
            source.pitch                 = 1f;
            source.loop                  = false;
            source.bypassEffects         = false;
            source.bypassListenerEffects = false;
            source.spatialBlend          = 1f;
            source.dopplerLevel          = 0.5f;
            source.minDistance           = 1f;
            source.maxDistance           = 20f;
            source.rolloffMode           = AudioRolloffMode.Linear;
            source.outputAudioMixerGroup = null;
            source.Play();

            var playbackEndsAt = Time.time + entry.Clip.length + 0.1f;
            var targetKey      = GetPlaybackTargetKey(selected.Enemy, selected.Target);
            if (targetKey != 0)
            {
                playbackBusyUntilByTargetKey[targetKey] = playbackEndsAt;
                playbackStartedAtByTargetKey[targetKey] = Time.time;
                playbackClipLengthByTargetKey[targetKey] = entry.Clip.length;
            }

            currentPlaybackEnemyName     = selected.EnemyName;
            currentPlaybackSourcePlayerId = "custom";
            hudTrackedEnemy              = selected.Enemy;
            hudTrackedTarget             = selected.Target;
            hudLastSelectedEnemyPos      = selected.Position;
            hudHasSelectedEnemyPos       = true;
            currentPlaybackEndsAt        = playbackEndsAt;

            DLog($"PlayCustomAudioEntry: playing '{entry.FileName}' length={entry.Clip.length:F1}s on '{selected.EnemyName}' dist={selected.Distance:F1} {DebugContext()}");
        }
        // ─── Normalization ────────────────────────────────────────────────────────
        private const float NormalizeTarget = 0.85f;   // peak target: leaves 15% headroom

        /// <summary>
        /// Peak-normalises an AudioClip in-place so custom files always play at a
        /// consistent loudness regardless of how loudly they were mastered.
        /// Works across all channels (stereo / mono).
        /// </summary>
        private static void NormalizeClip(AudioClip clip)
        {
            if (clip == null) return;

            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) return;

            // Find peak absolute value
            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var abs = Mathf.Abs(samples[i]);
                if (abs > peak) peak = abs;
            }

            // Skip silent or already-near-zero clips
            if (peak < 0.0001f) return;

            var scale = NormalizeTarget / peak;

            // Already loud enough — don't amplify if it would exceed target
            if (scale >= 1f && peak >= NormalizeTarget) return;

            for (var i = 0; i < samples.Length; i++)
                samples[i] *= scale;

            clip.SetData(samples, 0);
        }
    }
}
