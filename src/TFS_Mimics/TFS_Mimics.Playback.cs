using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Audio;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        [PunRPC]
        public void ReceiveAudioChunkV2(byte[] chunk, int chunkIndex, int totalChunks, bool applyFilter, int senderSampleRate, string transmissionId, PhotonMessageInfo info)
        {
            ReceiveAudioChunkInternal(chunk, chunkIndex, totalChunks, applyFilter, senderSampleRate, transmissionId, info.Sender);
        }

        internal void ReceiveAudioChunkInternal(byte[] chunk, int chunkIndex, int totalChunks, bool applyFilter, int senderSampleRate, string transmissionId, Photon.Realtime.Player sender)
        {
            var senderActor = sender != null ? sender.ActorNumber : -1;
            var senderPlayerId = sender != null ? GetPlayerPersistentId(sender) : "unknown";
            var senderName = sender != null ? sender.NickName : "unknown";
            var tx = string.IsNullOrWhiteSpace(transmissionId) ? "legacy" : transmissionId;

            if (senderActor == PhotonNetwork.LocalPlayer.ActorNumber)
            {
                // We already saved this clip locally in SendAudioInChunks
                return;
            }

            CleanupStaleIncomingTransmissions();

            if (chunk == null || totalChunks <= 0 || chunkIndex < 0 || chunkIndex >= totalChunks)
            {
                Log.LogWarning($"ReceiveAudioChunk: invalid packet tx={tx} chunkIndex={chunkIndex} totalChunks={totalChunks} dropping sender={senderActor}:{senderName} {DebugContext()}");
                return;
            }

            var key = senderActor + ":" + tx;
            if (!incomingAudioTransmissions.TryGetValue(key, out var transmission) || transmission == null)
            {
                transmission = new IncomingAudioTransmission
                {
                    ExpectedChunkCount = totalChunks,
                    ApplyFilter = false,
                    SampleRate = senderSampleRate,
                    SenderActor = senderActor,
                    SenderPlayerId = senderPlayerId,
                    SenderName = senderName,
                    LastUpdatedAt = Time.time
                };
                incomingAudioTransmissions[key] = transmission;
                DLog($"ReceiveAudioChunk: transmission start tx={tx} expectedChunks={totalChunks} sender={senderActor}:{senderName} playerId={senderPlayerId} {DebugContext()}");
            }

            if (transmission.ExpectedChunkCount != totalChunks)
            {
                DLog($"ReceiveAudioChunk: resetting tx={tx} because totalChunks changed old={transmission.ExpectedChunkCount} new={totalChunks} sender={senderActor}:{senderName} {DebugContext()}");
                transmission.ExpectedChunkCount = totalChunks;
                transmission.Chunks.Clear();
            }

            transmission.ApplyFilter = false;
            transmission.SampleRate = senderSampleRate;
            transmission.SenderPlayerId = senderPlayerId;
            transmission.SenderName = senderName;
            transmission.LastUpdatedAt = Time.time;

            while (transmission.Chunks.Count < transmission.ExpectedChunkCount)
            {
                transmission.Chunks.Add(null);
            }

            transmission.Chunks[chunkIndex] = chunk;
            DLog($"ReceiveAudioChunk: got tx={tx} chunk={chunkIndex + 1}/{transmission.ExpectedChunkCount} bytes={chunk.Length} sender={senderActor}:{senderName} playerId={senderPlayerId} {DebugContext()}");

            var chunksDone = transmission.Chunks.Count(c => c != null);
            PushVoiceLog(true, tx, senderPlayerId, senderName, 0, false, chunksDone, transmission.ExpectedChunkCount);

            if (transmission.Chunks.Any(c => c == null))
            {
                return;
            }

            var audioData = CombineChunks(transmission.Chunks);
            var entry = new CachedAudioEntry
            {
                AudioData = audioData,
                ApplyVoiceFilter = false,
                SampleRate = transmission.SampleRate,
                SourceActor = transmission.SenderActor,
                SourcePlayerId = transmission.SenderPlayerId,
                SourceName = transmission.SenderName,
                ReceivedAt = Time.time,
                SoundGuid = tx
            };
            AddToAudioCache(entry);

            // Notify host that this client now has this sound
            NotifyHostSoundReady(tx);

            if (Plugin.configPersistAudioCache != null && Plugin.configPersistAudioCache.Value)
            {
                SaveAudioEntryToDisk(entry);
            }

            DLog($"ReceiveAudioChunk: complete tx={tx} totalBytes={audioData.Length} source={entry.SourceActor}:{entry.SourceName} playerId={entry.SourcePlayerId} cachedTotal={cachedAudio.Count} {DebugContext()}");
            incomingAudioTransmissions.Remove(key);
            PushVoiceLog(true, tx, entry.SourcePlayerId, entry.SourceName, audioData.Length, true, transmission.ExpectedChunkCount, transmission.ExpectedChunkCount);
        }

        private void CleanupStaleIncomingTransmissions()
        {
            if (incomingAudioTransmissions.Count == 0)
            {
                return;
            }

            var now = Time.time;
            var stale = incomingAudioTransmissions
                .Where(kv => kv.Value == null || now - kv.Value.LastUpdatedAt > IncomingTransmissionTimeoutSeconds)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in stale)
            {
                incomingAudioTransmissions.Remove(key);
            }
        }

        private void TryPlayRandomCachedAudio()
        {
            var onlinePlayerIds = GetOnlinePlayerIds();
            var playableEntries = cachedAudio
                .Where(e =>
                {
                    if (e == null || e.AudioData == null || e.AudioData.Length == 0)
                    {
                        return false;
                    }

                    var entryPlayerId = !string.IsNullOrWhiteSpace(e.SourcePlayerId) ? e.SourcePlayerId : (e.SourceActor >= 0 ? $"actor_{e.SourceActor}" : "unknown");
                    return onlinePlayerIds.Contains(entryPlayerId);
                })
                .ToList();

            var playableCustom = _customAudioClips.Where(e => e?.Clip != null).ToList();

            var totalCount = playableEntries.Count + playableCustom.Count;
            if (totalCount == 0)
            {
                DLog($"TryPlayRandomCachedAudio: nothing to play — cached={cachedAudio.Count} onlineFiltered={playableEntries.Count} custom={playableCustom.Count} onlineIds=[{string.Join(",", onlinePlayerIds.OrderBy(x => x))}] {DebugContext()}");
                return;
            }

            var idx = UnityEngine.Random.Range(0, totalCount);
            if (idx < playableEntries.Count)
            {
                var entry = playableEntries[idx];
                DLog($"TryPlayRandomCachedAudio: selected cached clip idx={idx} source={entry.SourceActor}:{entry.SourceName} playerId={entry.SourcePlayerId} bytes={entry.AudioData.Length} age={Time.time - entry.ReceivedAt:F1}s {DebugContext()}");
                PlayReceivedAudio(entry.AudioData, entry.SampleRate, entry.SourceActor, entry.SourcePlayerId, entry.SourceName);
            }
            else
            {
                var customEntry = playableCustom[idx - playableEntries.Count];
                DLog($"TryPlayRandomCachedAudio: selected custom clip '{customEntry.FileName}' length={customEntry.Clip.length:F1}s {DebugContext()}");
                PlayCustomAudioEntry(customEntry);
            }
        }

        private void PlayReceivedAudio(byte[] audioData, int senderSampleRate, int sourceActor, string sourcePlayerId, string sourceName)
        {
            var playbackFilterEnabled = Plugin.configPlaybackVoiceFilterEnabled == null || Plugin.configPlaybackVoiceFilterEnabled.Value;
            var applyVoiceFilter = playbackFilterEnabled && UnityEngine.Random.value > 0.9f;
            DLog($"PlayReceivedAudio start: bytes={audioData.Length} applyVoiceFilter={applyVoiceFilter} senderSampleRate={senderSampleRate} source={sourceActor}:{sourceName} {DebugContext()}");
            // TryWriteDebugWav($"play_{SanitizePlayerIdForFileName(sourcePlayerId ?? $"actor_{sourceActor}")}", audioData, senderSampleRate);
            var samples = ConvertByteArrayToFloatArray(audioData, applyVoiceFilter, senderSampleRate);
            var clip = AudioClip.Create("ReceivedClip", samples.Length, 1, senderSampleRate, false);
            clip.SetData(samples, 0);

            if (Plugin.configDebugVerbose != null && Plugin.configDebugVerbose.Value)
            {
                var sumSq = 0f;
                for (var i = 0; i < samples.Length; i++) sumSq += samples[i] * samples[i];
                var rms = Mathf.Sqrt(sumSq / Mathf.Max(1, samples.Length));
                DLog($"AudioClip created: samples={samples.Length} lengthSec={clip.length:F2} channels=1 frequency={senderSampleRate} rms={rms:F5} {DebugContext()}");
            }

            var enemies = GetEnemiesList().Where(e => e != null).ToList();
            DLog($"Eligible enemies for playback: count={enemies.Count} filterEnabled={Plugin.configFilterEnabled.Value} {DebugContext()}");
            var playbackTargets = BuildPlaybackTargets(enemies, true);

            if (playbackTargets.Count == 0)
            {
                hudHasSelectedEnemyPos = false;
                nearestPlaybackTargetsHud.Clear();
                nearestPlaybackTargetsHud.Add("No candidates");
                DLog($"No playback targets found, clip will not be played {DebugContext()}");
                return;
            }

            var listenerPos = transform.position;
            var nearRadius = Plugin.configPlaybackNearRadius != null ? Plugin.configPlaybackNearRadius.Value : 25f;
            UpdateNearestPlaybackTargetsHud(playbackTargets, listenerPos, nearRadius);

            if (nearestPlaybackCandidatesHud.Count == 0)
            {
                DLog($"No nearest HUD candidates after refresh, clip will not be played {DebugContext()}");
                return;
            }

            var inRadiusCandidates = nearestPlaybackCandidatesHud
                .Where(c => c != null && c.Distance <= nearRadius && !c.IsPlaying)
                .ToList();

            if (inRadiusCandidates.Count == 0)
            {
                DLog($"Skipping playback: no candidates inside playback radius radius={nearRadius:F1} nearestCount={nearestPlaybackCandidatesHud.Count} {DebugContext()}");
                return;
            }

            var selected = inRadiusCandidates[UnityEngine.Random.Range(0, inRadiusCandidates.Count)];
            DLog($"Selecting random target inside playback radius: inRadiusCount={inRadiusCandidates.Count} radius={nearRadius:F1} selected={selected.EnemyName} dist={selected.Distance:F1} pos={FormatHudVector(selected.Position)} {DebugContext()}");

            var sourceComponent = GetOrCreateReusableEnemyAudioSource(selected.Enemy, selected.Target, selected.Position);
            if (sourceComponent == null)
            {
                Log.LogWarning($"Playback skipped: failed to get reusable audio source enemy={selected.EnemyName} {DebugContext()}");
                return;
            }

            sourceComponent.clip = clip;
            sourceComponent.volume = GetVolumeForPlayer(sourcePlayerId);
            sourceComponent.mute = false;
            sourceComponent.pitch = 1f;
            sourceComponent.loop = false;
            sourceComponent.bypassEffects = false;
            sourceComponent.bypassListenerEffects = false;
            sourceComponent.spatialBlend = 1f;
            sourceComponent.dopplerLevel = 0.5f;
            sourceComponent.minDistance = 1f;
            sourceComponent.maxDistance = 20f;
            sourceComponent.rolloffMode = AudioRolloffMode.Linear;

            // Null mixer group = Unity's default Master output, always audible.
            sourceComponent.outputAudioMixerGroup = null;

            sourceComponent.Play();
            DLog($"AudioSource.Play() called: isActiveAndEnabled={sourceComponent.isActiveAndEnabled} mute={sourceComponent.mute} pitch={sourceComponent.pitch} volume={sourceComponent.volume:F2} spatialBlend={sourceComponent.spatialBlend} pos={FormatHudVector(sourceComponent.transform.position)} {DebugContext()}");
            var playbackEndsAt = Time.time + clip.length + 0.1f;
            var targetKey = GetPlaybackTargetKey(selected.Enemy, selected.Target);
            if (targetKey != 0)
            {
                playbackBusyUntilByTargetKey[targetKey] = playbackEndsAt;
                playbackStartedAtByTargetKey[targetKey] = Time.time;
                playbackClipLengthByTargetKey[targetKey] = clip.length;
            }

            currentPlaybackEnemyName = selected.EnemyName;
            currentPlaybackSourcePlayerId = string.IsNullOrWhiteSpace(sourcePlayerId)
                ? (sourceActor >= 0 ? $"actor_{sourceActor}" : "unknown")
                : sourcePlayerId;
            hudTrackedEnemy = selected.Enemy;
            hudTrackedTarget = selected.Target;
            hudLastSelectedEnemyPos = selected.Position;
            hudHasSelectedEnemyPos = true;
            currentPlaybackEndsAt = playbackEndsAt;
            DLog($"Playback started on enemy={selected.EnemyName} pos={FormatHudVector(selected.Position)} volume={sourceComponent.volume:F2} clipLen={clip.length:F2} source={sourceActor}:{sourceName} {DebugContext()}");
            // The AudioSource proxy is parented to the moving Enemy Controller,
            // so its world position is updated automatically each frame.
            StartCoroutine(ResetReusableAudioSourceAfterDelay(sourceComponent, clip.length + 0.1f));
        }

        // Returns the AudioMixerGroup from the enemy's own AudioSources,
        // or null to let Unity route through the default output.
        private static AudioMixerGroup FindEnemyAudioMixerGroup(GameObject enemy, GameObject target)
        {
            var root = target != null ? target : enemy;
            if (root != null)
            {
                foreach (var src in root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (src != null && src.outputAudioMixerGroup != null)
                    {
                        return src.outputAudioMixerGroup;
                    }
                }
            }

            return null; // Unity default output — always audible
        }

        private int GetPlaybackTargetKey(GameObject enemy, GameObject target)
        {
            if (target != null)
            {
                return target.GetInstanceID();
            }

            if (enemy != null)
            {
                return enemy.GetInstanceID();
            }

            return 0;
        }

        private void CleanupFinishedPlaybackBusyFlags()
        {
            if (playbackBusyUntilByTargetKey.Count == 0)
            {
                return;
            }

            var now = Time.time;
            var finished = playbackBusyUntilByTargetKey
                .Where(kv => kv.Value <= now)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in finished)
            {
                playbackBusyUntilByTargetKey.Remove(key);
                playbackStartedAtByTargetKey.Remove(key);
                playbackClipLengthByTargetKey.Remove(key);
            }
        }

        private IEnumerator FollowAudioSourceDuringPlayback(AudioSource source, GameObject enemy, GameObject target, float duration)
        {
            var endAt = Time.time + duration;
            while (source != null && source.gameObject != null && Time.time <= endAt)
            {
                source.transform.position = GetEnemyDistancePosition(enemy, target);
                yield return null;
            }
        }

        private AudioSource GetOrCreateReusableEnemyAudioSource(GameObject enemy, GameObject target, Vector3 position)
        {
            // Resolve the best anchor transform (CenterTransform > VisionTransform > Enemy.transform)
            // so the proxy follows the enemy body, not the static EnemyParent root.
            var anchor = GetEnemyAudioAnchor(enemy);
            if (anchor == null)
            {
                return null;
            }

            // Key by anchor instance ID so the dictionary stays consistent.
            var key = anchor.gameObject.GetInstanceID();
            if (reusableEnemyAudioSources.TryGetValue(key, out var source) && source != null && source.gameObject != null)
            {
                return source;
            }

            // Dedicated child proxy — never overwrites existing AudioSources on the enemy.
            // Parented to anchor (CenterTransform) so world position follows automatically.
            const string proxyName = "MimicsAudio";
            var existingProxy = anchor.Find(proxyName);
            GameObject proxyGo;
            if (existingProxy != null)
            {
                proxyGo = existingProxy.gameObject;
            }
            else
            {
                proxyGo = new GameObject(proxyName);
                proxyGo.transform.SetParent(anchor, false);
                proxyGo.transform.localPosition = Vector3.zero;
            }

            source = proxyGo.GetComponent<AudioSource>() ?? proxyGo.AddComponent<AudioSource>();
            reusableEnemyAudioSources[key] = source;
            return source;
        }

        private void RefreshHudTargetsSnapshot()
        {
            var enemies = GetEnemiesList().Where(e => e != null).ToList();
            var playbackTargets = BuildPlaybackTargets(enemies, false);

            if (playbackTargets.Count == 0)
            {
                nearestPlaybackTargetsHud.Clear();
                nearestPlaybackTargetsHud.Add("No candidates");

                if (hudTrackedEnemy == null && hudTrackedTarget == null)
                {
                    hudHasSelectedEnemyPos = false;
                }

                return;
            }

            var listenerPos = transform.position;
            var nearRadius = Plugin.configPlaybackNearRadius != null ? Plugin.configPlaybackNearRadius.Value : 25f;
            UpdateNearestPlaybackTargetsHud(playbackTargets, listenerPos, nearRadius);

            if (hudTrackedEnemy != null || hudTrackedTarget != null)
            {
                hudLastSelectedEnemyPos = GetEnemyDistancePosition(hudTrackedEnemy, hudTrackedTarget);
                hudHasSelectedEnemyPos = true;
            }
        }

        private List<(GameObject enemy, GameObject target)> BuildPlaybackTargets(IEnumerable<GameObject> enemies, bool warnOnMissingTarget)
        {
            var playbackTargets = new List<(GameObject enemy, GameObject target)>();

            foreach (var enemy in enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                if (Plugin.configFilterEnabled.Value)
                {
                    var enemyName = enemy.name.Replace("(Clone)", string.Empty).Trim();
                    if (!IsEnemyEnabledInFilter(enemyName))
                    {
                        DLog($"Enemy skipped by filter: enemy={enemy.name} normalized={enemyName} {DebugContext()}");
                        continue;
                    }
                }

                var target = GetEnemyAudioTarget(enemy);
                if (target == null)
                {
                    if (warnOnMissingTarget)
                    {
                        Log.LogWarning($"Enemy audio target missing: enemy={enemy.name} {DebugContext()}");
                    }

                    continue;
                }

                playbackTargets.Add((enemy, target));
            }

            return playbackTargets;
        }

        private void UpdateNearestPlaybackTargetsHud(List<(GameObject enemy, GameObject target)> playbackTargets, Vector3 listenerPos, float nearRadius)
        {
            CleanupFinishedPlaybackBusyFlags();
            nearestPlaybackTargetsHud.Clear();
            nearestPlaybackCandidatesHud.Clear();

            var nearest = playbackTargets
                .Select(p => new
                {
                    Enemy = p.enemy,
                    Target = p.target,
                    EnemyName = NormalizeEnemyName(p.enemy != null ? p.enemy.name : "Unknown"),
                    Position = GetEnemyDistancePosition(p.enemy, p.target),
                    Distance = Vector3.Distance(GetEnemyDistancePosition(p.enemy, p.target), listenerPos),
                    TargetKey = GetPlaybackTargetKey(p.enemy, p.target)
                })
                .OrderBy(x => x.Distance)
                .Take(5)
                .ToList();

            foreach (var item in nearest)
            {
                var playbackEndsAt = 0f;
                var isPlaying = item.TargetKey != 0
                    && playbackBusyUntilByTargetKey.TryGetValue(item.TargetKey, out playbackEndsAt)
                    && playbackEndsAt > Time.time;
                var marker = item.Distance <= nearRadius ? "*" : "-";
                var playbackInfo = string.Empty;
                if (isPlaying)
                {
                    var playbackStartedAt = playbackStartedAtByTargetKey.TryGetValue(item.TargetKey, out var startedAt)
                        ? startedAt
                        : (playbackEndsAt - 0.1f);
                    var playbackLength = playbackClipLengthByTargetKey.TryGetValue(item.TargetKey, out var clipLength)
                        ? clipLength
                        : Mathf.Max(0f, playbackEndsAt - playbackStartedAt - 0.1f);
                    var playbackPosition = Mathf.Clamp(Time.time - playbackStartedAt, 0f, playbackLength);
                    playbackInfo = $" play={playbackPosition:F1}/{playbackLength:F1}s";
                }

                nearestPlaybackTargetsHud.Add($"{marker} {item.EnemyName} ({item.Distance:F1}m) pos={FormatHudVector(item.Position)}{playbackInfo}");
                nearestPlaybackCandidatesHud.Add(new HudPlaybackCandidate
                {
                    Enemy = item.Enemy,
                    Target = item.Target,
                    EnemyName = item.EnemyName,
                    Position = item.Position,
                    Distance = item.Distance,
                    IsPlaying = isPlaying,
                    PlaybackEndsAt = isPlaying ? playbackEndsAt : 0f
                });
            }

            if (nearestPlaybackTargetsHud.Count == 0)
            {
                nearestPlaybackTargetsHud.Add("No candidates");
            }
        }

        private IEnumerator ResetReusableAudioSourceAfterDelay(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (source != null)
            {
                source.clip = null;
            }
        }

        private bool IsEnemyEnabledInFilter(string enemyName)
        {
            if (filter == null || filter.Count == 0)
            {
                return true;
            }

            if (filter.TryGetValue(enemyName, out var enabled))
            {
                return enabled;
            }

            var withPrefix = "Enemy - " + enemyName;
            if (filter.TryGetValue(withPrefix, out enabled))
            {
                return enabled;
            }

            var normalized = enemyName.Replace("Enemy - ", string.Empty).Trim();
            if (filter.TryGetValue(normalized, out enabled))
            {
                return enabled;
            }

            return true;
        }

        // Returns the Transform that should serve as the audio anchor for the enemy.
        // Priority mirrors Imperium: CenterTransform > VisionTransform > Enemy.transform.
        private Transform GetEnemyAudioAnchor(GameObject enemy)
        {
            var enemyParent = enemy.GetComponent<EnemyParent>();
            if (enemyParent != null && enemyParent.Enemy != null)
            {
                var e = enemyParent.Enemy;
                if (e.CenterTransform != null) return e.CenterTransform;
                if (e.HasVision && e.Vision != null && e.Vision.VisionTransform != null) return e.Vision.VisionTransform;
                return e.transform;
            }

            var t = enemy.transform.Find("Enable/Controller") ?? enemy.transform.Find("Controller");
            return t != null ? t : enemy.transform;
        }

        // Kept for backward-compat with BuildPlaybackTargets/GetOrCreateReusableEnemyAudioSource callers
        // that still pass a GameObject target.
        private GameObject GetEnemyAudioTarget(GameObject enemy)
        {
            return GetEnemyAudioAnchor(enemy).gameObject;
        }

        private Vector3 GetEnemyDistancePosition(GameObject enemy, GameObject fallbackTarget)
        {
            if (enemy != null)
            {
                var enemyParent = enemy.GetComponentInChildren<EnemyParent>(true);
                if (enemyParent != null)
                {
                    if (TryGetRuntimeEnemyObject(enemyParent, out var runtimeEnemy) && TryGetRuntimeEnemyPosition(runtimeEnemy, out var runtimePos))
                    {
                        return runtimePos;
                    }

                    return enemyParent.transform.position;
                }

                var components = enemy.GetComponentsInChildren<Component>(true);
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    var typeName = component.GetType().Name;
                    if (string.Equals(typeName, "Animator", StringComparison.OrdinalIgnoreCase))
                    {
                        return component.transform.position;
                    }
                }
            }

            if (fallbackTarget != null)
            {
                return fallbackTarget.transform.position;
            }

            return enemy != null ? enemy.transform.position : Vector3.zero;
        }

        private static bool TryGetRuntimeEnemyPosition(object runtimeEnemy, out Vector3 position)
        {
            position = Vector3.zero;
            if (runtimeEnemy == null)
            {
                return false;
            }

            if (runtimeEnemy is Component component)
            {
                position = component.transform.position;
                return true;
            }

            var runtimeType = runtimeEnemy.GetType();

            var centerProp = runtimeType.GetProperty("CenterTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? runtimeType.GetProperty("centerTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? runtimeType.GetProperty("Transform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? runtimeType.GetProperty("transform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (centerProp != null && centerProp.CanRead)
            {
                var transformValue = centerProp.GetValue(runtimeEnemy, null) as Transform;
                if (transformValue != null)
                {
                    position = transformValue.position;
                    return true;
                }
            }

            var centerField = runtimeType.GetField("CenterTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? runtimeType.GetField("centerTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? runtimeType.GetField("Transform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? runtimeType.GetField("transform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (centerField != null)
            {
                var transformValue = centerField.GetValue(runtimeEnemy) as Transform;
                if (transformValue != null)
                {
                    position = transformValue.position;
                    return true;
                }
            }

            return false;
        }

        private void SetEnemyFilter()
        {
            filter.Clear();
            foreach (var kv in Plugin.enemyConfigEntries)
            {
                filter[kv.Key ?? string.Empty] = kv.Value.Value;
            }

            DLog($"Enemy filter loaded: entries={filter.Count} enabled={filter.Count(kv => kv.Value)} disabled={filter.Count(kv => !kv.Value)} {DebugContext()}");
        }

        private static string NormalizeEnemyName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("(Clone)", string.Empty).Replace("Enemy - ", string.Empty).Trim();
        }

        private static bool TryGetRuntimeEnemyObject(EnemyParent enemyParent, out object runtimeEnemy)
        {
            runtimeEnemy = null;
            if (enemyParent == null)
            {
                return false;
            }

            var parentType = enemyParent.GetType();
            var enemyProp = parentType.GetProperty("Enemy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? parentType.GetProperty("enemy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (enemyProp != null && enemyProp.CanRead)
            {
                runtimeEnemy = enemyProp.GetValue(enemyParent, null);
                if (runtimeEnemy != null)
                {
                    return true;
                }
            }

            var enemyField = parentType.GetField("Enemy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? parentType.GetField("enemy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (enemyField != null)
            {
                runtimeEnemy = enemyField.GetValue(enemyParent);
            }

            return runtimeEnemy != null;
        }

        private static string GetEnemyIdentityName(GameObject enemy, EnemyParent enemyParent)
        {
            var parentName = enemyParent != null ? enemyParent.enemyName : null;
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                return parentName;
            }

            return enemy != null ? enemy.name : string.Empty;
        }

        private bool IsEnemyInDespawnState(GameObject enemy)
        {
            if (enemy == null)
            {
                return true;
            }

            var enemyParent = enemy.GetComponentInChildren<EnemyParent>(true);
            if (enemyParent != null)
            {
                if (TryGetRuntimeEnemyObject(enemyParent, out var runtimeEnemy))
                {
                    object currentState = null;
                    var runtimeType = runtimeEnemy.GetType();
                    var stateProp = runtimeType.GetProperty("CurrentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        ?? runtimeType.GetProperty("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (stateProp != null && stateProp.CanRead)
                    {
                        currentState = stateProp.GetValue(runtimeEnemy, null);
                    }
                    else
                    {
                        var stateField = runtimeType.GetField("CurrentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                            ?? runtimeType.GetField("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (stateField != null)
                        {
                            currentState = stateField.GetValue(runtimeEnemy);
                        }
                    }

                    if (currentState != null)
                    {
                        var stateText = currentState.ToString();
                        if (!string.IsNullOrWhiteSpace(stateText) && stateText.IndexOf("Despawn", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            DLog($"Skipping despawned enemy via EnemyParent object={enemy.name} enemyName={enemyParent.enemyName} state={stateText} {DebugContext()}");
                            return true;
                        }
                    }
                }
            }

            var components = enemy.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                object stateValue = null;

                var stateProp = type.GetProperty("CurrentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    ?? type.GetProperty("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (stateProp != null && stateProp.CanRead)
                {
                    stateValue = stateProp.GetValue(component, null);
                }
                else
                {
                    var stateField = type.GetField("CurrentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        ?? type.GetField("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                    if (stateField != null)
                    {
                        stateValue = stateField.GetValue(component);
                    }
                }

                if (stateValue == null)
                {
                    continue;
                }

                var stateText = stateValue.ToString();
                if (!string.IsNullOrWhiteSpace(stateText) && stateText.IndexOf("Despawn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    DLog($"Skipping despawned enemy object={enemy.name} state={stateText} component={type.Name} {DebugContext()}");
                    return true;
                }
            }

            return false;
        }

        private List<GameObject> GetEnemiesList()
        {
            var list = new List<GameObject>();
            var known = patches.EnemyDirectorStartPatch.KnownEnemyNames;
            var knownNormalized = known != null
                ? new HashSet<string>(known.Select(NormalizeEnemyName).Where(n => !string.IsNullOrWhiteSpace(n)))
                : new HashSet<string>();

            var enemyParents = FindObjectsByType<EnemyParent>(FindObjectsSortMode.None);
            if (enemyParents == null || enemyParents.Length == 0)
            {
                DLog($"GetEnemiesList: no EnemyParent objects found in scene {DebugContext()}");
                return list;
            }

            foreach (var enemyParent in enemyParents)
            {
                var go = enemyParent != null ? enemyParent.gameObject : null;
                if (go == null)
                {
                    continue;
                }

                if (!TryGetRuntimeEnemyObject(enemyParent, out _))
                {
                    DLog($"GetEnemiesList: skipping object without runtime Enemy reference {go.name} enemyName={enemyParent.enemyName}");
                    continue;
                }

                var identityNormalized = NormalizeEnemyName(GetEnemyIdentityName(go, enemyParent));
                var objectNormalized = NormalizeEnemyName(go.name);
                var matchesKnown = knownNormalized.Count == 0
                    || knownNormalized.Contains(identityNormalized)
                    || knownNormalized.Contains(objectNormalized);
                if (!matchesKnown)
                {
                    DLog($"GetEnemiesList: skipping non-registered enemy object={go.name} identity={identityNormalized} objectName={objectNormalized}");
                    continue;
                }

                if (IsEnemyInDespawnState(go))
                {
                    continue;
                }

                list.Add(go);
            }

            DLog($"GetEnemiesList: found {list.Count} enemies in scene {DebugContext()}");

            return list;
        }

        // ─── Phase 5: Synchronized playback driven by host command ───────────────

        internal void HandleSyncPlayCommand(SyncPlayCommandPacket packet)
        {
            if (packet == null) return;

            var masterClient = PhotonNetwork.MasterClient;
            if (masterClient == null || packet.HostActorNumber != masterClient.ActorNumber)
            {
                DLog($"HandleSyncPlayCommand: rejected — hostActor={packet.HostActorNumber} master={masterClient?.ActorNumber}");
                return;
            }

            var guid = packet.SoundGuid;
            if (string.IsNullOrEmpty(guid) || packet.EnemyViewIds == null || packet.EnemyViewIds.Length == 0) return;

            var voiceEntry = cachedAudio.FirstOrDefault(e => e?.SoundGuid == guid);
            if (voiceEntry != null)
            {
                foreach (var viewId in packet.EnemyViewIds)
                    PlayVoiceEntryOnEnemy(voiceEntry, viewId, packet.VoiceFilterMode);
                return;
            }

            // Try custom audio — match by SoundGuid or by ContentHash (cross-platform GUID mismatch)
            var customEntry = _customAudioClips.FirstOrDefault(e => e?.SoundGuid == guid)
                ?? _customAudioClips.FirstOrDefault(e =>
                    e != null && !string.IsNullOrEmpty(e.ContentHash) &&
                    _customAudioClips.Any(c => c.SoundGuid == guid && c.ContentHash == e.ContentHash));

            if (customEntry != null)
            {
                foreach (var viewId in packet.EnemyViewIds)
                    PlayCustomEntryOnEnemy(customEntry, viewId);
                return;
            }

            DLog($"HandleSyncPlayCommand: no audio found for guid={guid}");
        }

        private void PlayVoiceEntryOnEnemy(CachedAudioEntry entry, int enemyViewId, int voiceFilterMode)
        {
            var enemyGo = GetEnemyGameObjectByViewId(enemyViewId);
            if (enemyGo == null) return;

            var samples = ConvertByteArrayToFloatArray(entry.AudioData, voiceFilterMode, entry.SampleRate);
            var clip = AudioClip.Create("SyncVoiceClip", samples.Length, 1, entry.SampleRate, false);
            clip.SetData(samples, 0);
            PlayClipOnEnemyGameObject(clip, enemyGo, GetVolumeForPlayer(entry.SourcePlayerId));
        }

        private void PlayCustomEntryOnEnemy(CustomAudioEntry entry, int enemyViewId)
        {
            if (entry?.Clip == null) return;
            var enemyGo = GetEnemyGameObjectByViewId(enemyViewId);
            if (enemyGo == null) return;

            if (!entry.IsNormalized)
            {
                NormalizeClip(entry.Clip);
                entry.IsNormalized = true;
            }

            PlayClipOnEnemyGameObject(entry.Clip, enemyGo, GetVolumeForPlayer("custom"));
        }

        private void PlayClipOnEnemyGameObject(AudioClip clip, GameObject enemyGo, float volume)
        {
            var sourceComponent = GetOrCreateReusableEnemyAudioSource(enemyGo, null, enemyGo.transform.position);
            if (sourceComponent == null) return;

            sourceComponent.clip = clip;
            sourceComponent.volume = volume;
            sourceComponent.mute = false;
            sourceComponent.pitch = 1f;
            sourceComponent.loop = false;
            sourceComponent.bypassEffects = false;
            sourceComponent.bypassListenerEffects = false;
            sourceComponent.spatialBlend = 1f;
            sourceComponent.dopplerLevel = 0.5f;
            sourceComponent.minDistance = 1f;
            sourceComponent.maxDistance = 20f;
            sourceComponent.rolloffMode = AudioRolloffMode.Linear;
            sourceComponent.outputAudioMixerGroup = null;
            sourceComponent.Play();

            var targetKey = GetPlaybackTargetKey(enemyGo, null);
            if (targetKey != 0)
            {
                playbackBusyUntilByTargetKey[targetKey] = Time.time + clip.length + 0.1f;
                playbackStartedAtByTargetKey[targetKey] = Time.time;
                playbackClipLengthByTargetKey[targetKey] = clip.length;
            }

            currentPlaybackEnemyName = NormalizeEnemyName(enemyGo.name);
            currentPlaybackSourcePlayerId = "sync";
            hudTrackedEnemy = enemyGo;
            hudLastSelectedEnemyPos = enemyGo.transform.position;
            hudHasSelectedEnemyPos = true;
            currentPlaybackEndsAt = Time.time + clip.length + 0.1f;

            DLog($"SyncPlay: clip on enemy={enemyGo.name} len={clip.length:F2}s vol={volume:F2}");
            StartCoroutine(ResetReusableAudioSourceAfterDelay(sourceComponent, clip.length + 0.1f));
        }

        // ─── Phase 6: Mob identification helper ───────────────────────────────────

        internal static int GetEnemyNetViewId(GameObject enemyGo)
        {
            if (enemyGo == null) return -1;
            var pv = enemyGo.GetComponent<PhotonView>()
                  ?? enemyGo.GetComponentInChildren<PhotonView>(true)
                  ?? enemyGo.GetComponentInParent<PhotonView>();
            return pv?.ViewID ?? -1;
        }

        private static GameObject GetEnemyGameObjectByViewId(int viewId)
        {
            var pv = PhotonView.Find(viewId);
            return pv?.gameObject;
        }
    }
}