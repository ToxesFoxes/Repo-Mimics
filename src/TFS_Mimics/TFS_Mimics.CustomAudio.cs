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
            public string    FilePath;   // full path for reload (null for API-registered clips)
            /// <summary>
            /// Display name of the mod that added this clip via <see cref="MimicsAPI"/>.
            /// <c>null</c> means the clip was loaded from the built-in custom-audio folder.
            /// </summary>
            public string    SourceMod;  // null = custom-audio folder; set = API-registered
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

        /// <summary>Reload: re-scans the folder. API-registered clips are preserved.</summary>
        internal void ReloadCustomAudio()
        {
            // Only destroy and remove folder-loaded clips (SourceMod == null).
            // API-registered clips (SourceMod != null) are kept in the list.
            foreach (var entry in _customAudioClips)
            {
                if (entry?.SourceMod == null && entry?.Clip != null)
                    Destroy(entry.Clip);
            }
            _customAudioClips.RemoveAll(e => e?.SourceMod == null);
            _customAudioLoaded = false;
            EnsureCustomAudioLoaded();
        }

        // ─── API clip consumer ────────────────────────────────────────────────────
        /// <summary>
        /// Drains any clips queued via <see cref="MimicsAPI"/> and adds them to the pool.
        /// Safe to call multiple times — clips are only consumed once.
        /// </summary>
        internal void ConsumeApiClips()
        {
            var pending = MimicsAPI.TakePending();
            if (pending == null) return;

            foreach (var (modGuid, displayName, clip) in pending)
            {
                if (clip == null) continue;

                NormalizeClip(clip);

                _customAudioClips.Add(new CustomAudioEntry
                {
                    Clip      = clip,
                    FileName  = !string.IsNullOrEmpty(clip.name) ? clip.name : modGuid,
                    FilePath  = null,
                    SourceMod = displayName,
                });

                Log.LogInfo($"[Mimics] API: registered clip '{clip.name}' from mod '{displayName}' (guid={modGuid})");
            }
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
                        Clip      = clip,
                        FileName  = Path.GetFileName(filePath),
                        FilePath  = filePath,
                        SourceMod = null,   // folder-loaded
                    });
                    loaded++;
                    DLog($"CustomAudio: loaded '{clip.name}' length={clip.length:F1}s freq={clip.frequency}Hz channels={clip.channels}");
                }
            }

            Log.LogInfo($"[Mimics] Custom audio: loaded {loaded}/{files.Count} file(s) successfully");

            // Consume any clips that were registered via MimicsAPI before or during loading.
            ConsumeApiClips();
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
            source.volume                = GetVolumeForPlayer("custom");
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
        /// <summary>
        /// Reads all samples from the clip, runs shared peak-normalization, then writes back.
        /// </summary>
        private static void NormalizeClip(AudioClip clip)
        {
            if (clip == null) return;

            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) return;

            NormalizeSamples(samples);

            clip.SetData(samples, 0);
        }
    }
}
