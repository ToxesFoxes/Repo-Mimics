using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Photon.Voice;
using UnityEngine;

namespace TFS_Mimics
{
    public class TFS_Mimics : MonoBehaviour
    {
        private sealed class CachedAudioEntry
        {
            public byte[] AudioData;
            public bool ApplyVoiceFilter;
            public int SampleRate;
            public int SourceActor;
            public string SourcePlayerId;
            public string SourceName;
            public float ReceivedAt;
        }

        private sealed class HudPlaybackCandidate
        {
            public GameObject Enemy;
            public GameObject Target;
            public Vector3 Position;
            public float Distance;
            public string EnemyName;
            public bool IsPlaying;
            public float PlaybackEndsAt;
        }

        private sealed class PlayersIndexData
        {
            public List<PlayerIndexEntry> players { get; set; } = new List<PlayerIndexEntry>();
        }

        private sealed class PlayerIndexEntry
        {
            public string id { get; set; }
            public string name { get; set; }
        }

        private sealed class IncomingAudioTransmission
        {
            public int ExpectedChunkCount;
            public bool ApplyFilter;
            public int SampleRate;
            public int SenderActor;
            public string SenderPlayerId;
            public string SenderName;
            public float LastUpdatedAt;
            public readonly List<byte[]> Chunks = new List<byte[]>();
        }

        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("TFS_Mimics");

        public PhotonView photonView;

        private PlayerVoiceChat playerVoiceChat;
        private FieldInfo voiceChatField;

        private int sampleRate;
        private float[] audioBuffer;
        private int bufferPosition;
        private float[] preSpeechBuffer;
        private int preSpeechWritePos;
        private int preSpeechCount;
        private int postSpeechSamplesRemaining;
        private int pendingSilenceSamples;
        private const float SilenceMergeSeconds = 3f;
        private const float MergeSilenceBridgeSeconds = 0.2f;
        private const int RecordingBufferSeconds = 30;
        private bool isRecording;
        private bool capturingSpeech;
        private bool fileSaved;

        private Dictionary<string, bool> filter;

        private readonly Dictionary<string, IncomingAudioTransmission> incomingAudioTransmissions = new Dictionary<string, IncomingAudioTransmission>();
        private readonly Dictionary<int, AudioSource> reusableEnemyAudioSources = new Dictionary<int, AudioSource>();
        private readonly Dictionary<int, float> playbackBusyUntilByTargetKey = new Dictionary<int, float>();
        private readonly Dictionary<int, float> playbackStartedAtByTargetKey = new Dictionary<int, float>();
        private readonly Dictionary<int, float> playbackClipLengthByTargetKey = new Dictionary<int, float>();
        private readonly List<CachedAudioEntry> cachedAudio = new List<CachedAudioEntry>();
        private string currentPlaybackEnemyName = "None";
        private string currentPlaybackSourcePlayerId = "None";
        private float currentPlaybackEndsAt;
        private GUIStyle hudTextStyle;
        private readonly List<string> nearestPlaybackTargetsHud = new List<string>();
        private readonly List<HudPlaybackCandidate> nearestPlaybackCandidatesHud = new List<HudPlaybackCandidate>();
        private Vector3 hudLastSelectedEnemyPos;
        private bool hudHasSelectedEnemyPos;
        private GameObject hudTrackedEnemy;
        private GameObject hudTrackedTarget;
        private float hudNextRefreshAt;
        private bool wasInLevel;
        private bool persistenceInitialized;
        private readonly HashSet<string> loadedPersistedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> playerNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const float IncomingTransmissionTimeoutSeconds = 30f;

        private string DebugContext()
        {
            var local = PhotonNetwork.LocalPlayer;
            var localId = local != null ? local.ActorNumber : -1;
            var localName = local != null ? local.NickName : "null";
            var owner = photonView != null ? photonView.Owner : null;
            var ownerId = owner != null ? owner.ActorNumber : -1;
            var ownerName = owner != null ? owner.NickName : "null";
            return $"[ctx local={localId}:{localName} owner={ownerId}:{ownerName} isMine={(photonView != null && photonView.IsMine)} time={Time.time:F2}]";
        }

        private void DLog(string message)
        {
            if (Plugin.configDebugVerbose != null && Plugin.configDebugVerbose.Value)
            {
                Log.LogInfo(message);
            }
        }

        private static string FitHudText(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "None";
            }

            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 3) + "...";
        }

        private static string FormatHudVector(Vector3 value)
        {
            return $"({value.x:F1}, {value.y:F1}, {value.z:F1})";
        }

        private void OnGUI()
        {
            if (Plugin.configDebugVerbose == null || !Plugin.configDebugVerbose.Value)
            {
                return;
            }

            if (photonView == null || !photonView.IsMine || !SemiFunc.RunIsLevel())
            {
                return;
            }

            if (hudTextStyle == null)
            {
                hudTextStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 10,
                    wordWrap = false,
                    normal =
                    {
                        textColor = Color.white
                    }
                };
            }

            var activePlaybackName = Time.time <= currentPlaybackEndsAt ? currentPlaybackEnemyName : "None";
            var activePlaybackSourcePlayerId = Time.time <= currentPlaybackEndsAt ? currentPlaybackSourcePlayerId : "None";
            var targetLines = nearestPlaybackTargetsHud.Count > 0 ? nearestPlaybackTargetsHud : new List<string> { "No candidates" };
            var localNick = PhotonNetwork.LocalPlayer != null && !string.IsNullOrWhiteSpace(PhotonNetwork.LocalPlayer.NickName)
                ? PhotonNetwork.LocalPlayer.NickName
                : "Unknown";
            var recordingDurationSec = sampleRate > 0 ? bufferPosition / (float)sampleRate : 0f;
            var recordingStatus = capturingSpeech
                ? $"Recording now: {localNick} ({recordingDurationSec:F1}s)"
                : "Recording now: None";
            var lines = new List<string>
            {
                $"Sounds Cached: {cachedAudio.Count}",
                $"Current Sound Enemy: {activePlaybackName}",
                $"Current Sound Source PlayerId: {activePlaybackSourcePlayerId}",
                $"Player Pos: {FormatHudVector(transform.position)}",
                $"Selected Enemy Pos: {(hudHasSelectedEnemyPos ? FormatHudVector(hudLastSelectedEnemyPos) : "None")}",
                "Nearest Playback Targets:"
            };
            lines.AddRange(targetLines);
            lines.Add(string.Empty);
            lines.Add(recordingStatus);

            var maxLineWidth = 260f;
            foreach (var line in lines)
            {
                var width = hudTextStyle.CalcSize(new GUIContent(line)).x;
                if (width > maxLineWidth)
                {
                    maxLineWidth = width;
                }
            }

            var panelWidth = Mathf.Clamp(maxLineWidth + 24f, 320f, 920f);
            var lineHeight = 17f;
            var panelHeight = 30f + lines.Count * lineHeight + 8f;
            var rect = new Rect(Screen.width - panelWidth - 16f, 16f, panelWidth, panelHeight);
            GUI.Box(rect, "Mimics Monitor");

            for (var i = 0; i < lines.Count; i++)
            {
                GUI.Label(
                    new Rect(rect.x + 10f, rect.y + 24f + i * lineHeight, rect.width - 20f, lineHeight),
                    lines[i],
                    hudTextStyle
                );
            }
        }

        private void Update()
        {
            if (photonView == null || !photonView.IsMine)
            {
                return;
            }

            var inLevel = SemiFunc.RunIsLevel();
            if (inLevel && !wasInLevel)
            {
                OnEnteredLevel();
            }
            else if (!inLevel && wasInLevel)
            {
                wasInLevel = false;
                persistenceInitialized = false;
            }

            if (!inLevel)
            {
                return;
            }

            if (Plugin.configDebugVerbose == null || !Plugin.configDebugVerbose.Value)
            {
                return;
            }

            if (Time.time < hudNextRefreshAt)
            {
                return;
            }

            hudNextRefreshAt = Time.time + 0.25f;
            RefreshHudTargetsSnapshot();
        }

        private void OnEnteredLevel()
        {
            wasInLevel = true;
            EnsurePersistenceInitialized();
            LogPlayerRecordingSummaryForWorldEntry();
        }

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            if (photonView == null)
            {
                Log.LogError("PhotonView not found on Mimics.");
                return;
            }

            DLog($"Mimics Awake {DebugContext()} object={gameObject.name}");

            var avatar = GetComponent<PlayerAvatar>();
            if (avatar == null)
            {
                Log.LogError("PlayerAvatar not found on Mimics.");
                return;
            }

            voiceChatField = typeof(PlayerAvatar).GetField("voiceChat", BindingFlags.Instance | BindingFlags.NonPublic);
            if (voiceChatField == null)
            {
                Log.LogError("Could not find 'voiceChat' field in PlayerAvatar.");
                return;
            }

            sampleRate = Plugin.configSamplingRate.Value;
            DLog($"Configured sampleRate={sampleRate} minDelay={Plugin.configMinDelay.Value} maxDelay={Plugin.configMaxDelay.Value} volume={Plugin.configVoiceVolume.Value} hearSelf={Plugin.configHearYourself.Value} filterEnabled={Plugin.configFilterEnabled.Value} {DebugContext()}");

            if (Plugin.configFilterEnabled.Value)
            {
                filter = new Dictionary<string, bool>();
                SetEnemyFilter();
            }
            else
            {
                DLog("Filter disabled, all enemies can mimic.");
            }

            StartCoroutine(WaitForVoiceChat(avatar));
        }

        private IEnumerator WaitForVoiceChat(PlayerAvatar avatar)
        {
            var waits = 0;
            while (playerVoiceChat == null)
            {
                playerVoiceChat = (PlayerVoiceChat)voiceChatField.GetValue(avatar);
                if (playerVoiceChat == null)
                {
                    waits++;
                    if (waits % 120 == 0)
                    {
                        DLog($"Waiting for PlayerVoiceChat... frames={waits} {DebugContext()}");
                    }
                    yield return null;
                }
            }

            DLog($"PlayerVoiceChat initialized after waitFrames={waits} {DebugContext()}");

            if (photonView.IsMine && SemiFunc.RunIsLevel())
            {
                EnsurePersistenceInitialized();
                StartRecording();
                StartCoroutine(PlayCachedAudioAtRandomIntervals());
                DLog($"Local loops started: speech capture + random playback {DebugContext()}");
            }
            else
            {
                DLog($"Not starting local loops because isMine={photonView.IsMine} runIsLevel={SemiFunc.RunIsLevel()} {DebugContext()}");
            }
        }

        private IEnumerator PlayCachedAudioAtRandomIntervals()
        {
            while (true)
            {
                var delay = UnityEngine.Random.Range(Plugin.configMinDelay.Value, Plugin.configMaxDelay.Value);
                DLog($"Next random playback check in {delay:F2}s, cachedClips={cachedAudio.Count} {DebugContext()}");
                yield return new WaitForSeconds(delay);
                TryPlayRandomCachedAudio();
            }
        }

        private void StartRecording()
        {
            if (isRecording)
            {
                return;
            }

            audioBuffer = new float[sampleRate * RecordingBufferSeconds];
            preSpeechBuffer = new float[Mathf.Max(1, (int)(sampleRate * 0.25f))];
            bufferPosition = 0;
            preSpeechWritePos = 0;
            preSpeechCount = 0;
            postSpeechSamplesRemaining = 0;
            pendingSilenceSamples = 0;
            isRecording = true;
            capturingSpeech = false;
            fileSaved = false;
            DLog($"StartRecording: bufferSamples={audioBuffer.Length} (~{audioBuffer.Length / (float)sampleRate:F2}s) {DebugContext()}");
        }

        private void PushToPreSpeechBuffer(short[] voiceData)
        {
            if (preSpeechBuffer == null || preSpeechBuffer.Length == 0 || voiceData == null || voiceData.Length == 0)
            {
                return;
            }

            for (var i = 0; i < voiceData.Length; i++)
            {
                preSpeechBuffer[preSpeechWritePos] = voiceData[i] / 32768f;
                preSpeechWritePos = (preSpeechWritePos + 1) % preSpeechBuffer.Length;
                if (preSpeechCount < preSpeechBuffer.Length)
                {
                    preSpeechCount++;
                }
            }
        }

        private void PrependPreSpeechToCapture()
        {
            if (preSpeechBuffer == null || preSpeechCount <= 0 || audioBuffer == null)
            {
                return;
            }

            var available = audioBuffer.Length - bufferPosition;
            var copyCount = Mathf.Min(preSpeechCount, available);
            if (copyCount <= 0)
            {
                return;
            }

            var start = (preSpeechWritePos - preSpeechCount + preSpeechBuffer.Length) % preSpeechBuffer.Length;
            for (var i = 0; i < copyCount; i++)
            {
                var idx = (start + i) % preSpeechBuffer.Length;
                audioBuffer[bufferPosition + i] = preSpeechBuffer[idx];
            }

            bufferPosition += copyCount;
        }

        private int AppendVoiceDataToCapture(short[] voiceData)
        {
            if (voiceData == null || voiceData.Length == 0 || audioBuffer == null || bufferPosition >= audioBuffer.Length)
            {
                return 0;
            }

            var copyCount = Mathf.Min(voiceData.Length, audioBuffer.Length - bufferPosition);
            for (var i = 0; i < copyCount; i++)
            {
                audioBuffer[bufferPosition + i] = voiceData[i] / 32768f;
            }

            bufferPosition += copyCount;
            return copyCount;
        }

        private int AppendSilenceToCapture(int sampleCount)
        {
            if (sampleCount <= 0 || audioBuffer == null || bufferPosition >= audioBuffer.Length)
            {
                return 0;
            }

            var copyCount = Mathf.Min(sampleCount, audioBuffer.Length - bufferPosition);
            for (var i = 0; i < copyCount; i++)
            {
                audioBuffer[bufferPosition + i] = 0f;
            }

            bufferPosition += copyCount;
            return copyCount;
        }

        public void ProcessVoiceData(short[] voiceData)
        {
            if (!isRecording || !photonView.IsMine || playerVoiceChat == null)
            {
                return;
            }

            PushToPreSpeechBuffer(voiceData);

            var isTalkingField = typeof(PlayerVoiceChat).GetField("isTalking", BindingFlags.Instance | BindingFlags.NonPublic);
            var isTalking = isTalkingField != null && (bool)isTalkingField.GetValue(playerVoiceChat);

            if (isTalking && !capturingSpeech)
            {
                capturingSpeech = true;
                bufferPosition = 0;
                PrependPreSpeechToCapture();
                postSpeechSamplesRemaining = Mathf.Max(1, (int)(sampleRate * SilenceMergeSeconds));
                var localPlayer = PhotonNetwork.LocalPlayer;
                var localPlayerId = GetPlayerPersistentId(localPlayer);
                var localPlayerName = GetPlayerDisplayName(localPlayer);
                Log.LogInfo($"Started recording player [{localPlayerName}]({localPlayerId}) {DebugContext()}");
                DLog($"Speech detected: begin capture voiceDataSamples={voiceData.Length} {DebugContext()}");
            }

            if (!capturingSpeech)
            {
                return;
            }

            var copyCount = 0;
            if (isTalking)
            {
                if (pendingSilenceSamples > 0)
                {
                    var bridgeMaxSamples = Mathf.Max(1, (int)(sampleRate * MergeSilenceBridgeSeconds));
                    var bridgeSamples = Mathf.Min(pendingSilenceSamples, bridgeMaxSamples);
                    AppendSilenceToCapture(bridgeSamples);
                    pendingSilenceSamples = 0;
                }

                copyCount = AppendVoiceDataToCapture(voiceData);
                postSpeechSamplesRemaining = Mathf.Max(1, (int)(sampleRate * SilenceMergeSeconds));
            }
            else
            {
                pendingSilenceSamples = Mathf.Min(
                    pendingSilenceSamples + voiceData.Length,
                    Mathf.Max(1, (int)(sampleRate * SilenceMergeSeconds))
                );
                postSpeechSamplesRemaining -= voiceData.Length;
                if (postSpeechSamplesRemaining <= 0 && bufferPosition > sampleRate / 4 && !fileSaved)
                {
                    DLog($"Speech ended after smart silence-merge window: finalize bufferedSamples={bufferPosition} mergeSilenceSec={SilenceMergeSeconds:F1} bridgeSec={MergeSilenceBridgeSeconds:F1} {DebugContext()}");
                    FinalizeCaptureAndSend(bufferPosition);
                    return;
                }
            }

            if (bufferPosition % (sampleRate / 2) < copyCount)
            {
                DLog($"Capture progress: bufferPosition={bufferPosition}/{audioBuffer.Length} copiedNow={copyCount} {DebugContext()}");
            }
            if (bufferPosition < audioBuffer.Length || fileSaved)
            {
                return;
            }

            DLog($"Capture finished by full buffer: totalSamples={audioBuffer.Length} {DebugContext()}");
            FinalizeCaptureAndSend(audioBuffer.Length);
        }

        private void FinalizeCaptureAndSend(int usedSamples)
        {
            if (fileSaved || usedSamples <= 0)
            {
                return;
            }

            var finalSamples = new float[usedSamples];
            Array.Copy(audioBuffer, finalSamples, usedSamples);

            isRecording = false;
            capturingSpeech = false;
            fileSaved = true;

            var audioBytes = ConvertFloatArrayToByteArray(finalSamples);
            var durationSec = sampleRate > 0 ? usedSamples / (float)sampleRate : 0f;
            var localPlayer = PhotonNetwork.LocalPlayer;
            var localPlayerId = GetPlayerPersistentId(localPlayer);
            var localPlayerName = GetPlayerDisplayName(localPlayer);
            Log.LogInfo($"Finished recording player [{localPlayerName}]({localPlayerId}) duration={durationSec:F2}s bytes={audioBytes.Length} {DebugContext()}");
            DLog($"FinalizeCaptureAndSend: usedSamples={usedSamples} bytes={audioBytes.Length} {DebugContext()}");
            StartCoroutine(SendAudioInChunks(audioBytes));
            StartRecording();
        }

        private IEnumerator SendAudioInChunks(byte[] audioData)
        {
            var chunks = ChunkAudioData(audioData, 8192);
            var transmissionId = Guid.NewGuid().ToString("N");
            var localPlayer = PhotonNetwork.LocalPlayer;
            var localPlayerId = GetPlayerPersistentId(localPlayer);
            var localPlayerName = GetPlayerDisplayName(localPlayer);
            var target = Plugin.configHearYourself.Value ? RpcTarget.All : RpcTarget.Others;
            Log.LogInfo($"Start sending audio to players [{localPlayerName}]({localPlayerId}) tx={transmissionId} bytes={audioData.Length} chunks={chunks.Count} target={target} {DebugContext()}");
            DLog($"SendAudioInChunks begin: totalBytes={audioData.Length} chunks={chunks.Count} chunkSize=8192 {DebugContext()}");

            for (var i = 0; i < chunks.Count; i++)
            {
                if (!PhotonNetwork.IsConnectedAndReady)
                {
                    Log.LogWarning($"Photon disconnected during send at chunk={i}/{chunks.Count} {DebugContext()}");
                    yield break;
                }

                var applyVoiceFilter = false;

                photonView.RPC(
                    "ReceiveAudioChunkV2",
                    target,
                    chunks[i],
                    i,
                    chunks.Count,
                    applyVoiceFilter,
                    sampleRate,
                    transmissionId
                );

                DLog($"RPC sent: tx={transmissionId} chunk={i + 1}/{chunks.Count} bytes={chunks[i].Length} target={target} applyVoiceFilter={applyVoiceFilter} senderSampleRate={sampleRate} {DebugContext()}");

                yield return new WaitForSeconds(0.125f);
            }

            Log.LogInfo($"Finished sending audio to players [{localPlayerName}]({localPlayerId}) tx={transmissionId} chunksSent={chunks.Count} target={target} {DebugContext()}");
            DLog($"SendAudioInChunks complete: chunksSent={chunks.Count} {DebugContext()}");
        }

        private void EnsurePersistenceInitialized()
        {
            if (persistenceInitialized)
            {
                return;
            }

            persistenceInitialized = true;
            if (Plugin.configPersistAudioCache == null || !Plugin.configPersistAudioCache.Value)
            {
                return;
            }

            LoadPersistedAudioEntriesFromDisk();
        }

        private string GetAudioCacheDirectoryPath()
        {
            return Path.Combine(Paths.BepInExRootPath, "plugins", "ToxesFoxes-Mimics", "audio-cache");
        }

        private void LoadPersistedAudioEntriesFromDisk()
        {
            var dir = GetAudioCacheDirectoryPath();
            if (!Directory.Exists(dir))
            {
                return;
            }

            LoadPlayersIndexFromDisk();

            var files = Directory.GetFiles(dir, "audio_*_*.bin", SearchOption.AllDirectories);
            var loaded = 0;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || loadedPersistedFiles.Contains(file))
                {
                    continue;
                }

                if (!TryReadAudioEntryFromDisk(file, out var entry) || entry == null)
                {
                    continue;
                }

                cachedAudio.Add(entry);
                RegisterPlayerInIndex(entry.SourcePlayerId, entry.SourceName);
                loadedPersistedFiles.Add(file);
                loaded++;
            }

            SavePlayersIndexToDisk();
            DLog($"Persistence load complete: loaded={loaded} totalCached={cachedAudio.Count} path={dir} {DebugContext()}");
        }

        private void SaveAudioEntryToDisk(CachedAudioEntry entry)
        {
            try
            {
                if (entry == null || entry.AudioData == null || entry.AudioData.Length == 0)
                {
                    return;
                }

                var dir = GetAudioCacheDirectoryPath();
                Directory.CreateDirectory(dir);

                var actorId = entry.SourceActor;
                var playerId = !string.IsNullOrWhiteSpace(entry.SourcePlayerId) ? entry.SourcePlayerId : $"actor_{actorId}";
                var safePlayerId = SanitizePlayerIdForFileName(playerId);
                var playerDir = Path.Combine(dir, safePlayerId);
                Directory.CreateDirectory(playerDir);
                var fileName = $"audio_{safePlayerId}_{Guid.NewGuid():N}.bin";
                var path = Path.Combine(playerDir, fileName);

                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write("MIMC2");
                    writer.Write(actorId);
                    writer.Write(playerId);
                    writer.Write(entry.SampleRate);
                    writer.Write(entry.ApplyVoiceFilter);
                    writer.Write(entry.SourceName ?? string.Empty);
                    writer.Write(entry.AudioData.Length);
                    writer.Write(entry.AudioData);
                }

                RegisterPlayerInIndex(playerId, entry.SourceName);
                SavePlayersIndexToDisk();
                EnforcePlayerAudioFileLimit(playerDir);
                loadedPersistedFiles.Add(path);
                DLog($"Persistence save: file={fileName} playerId={playerId} actor={actorId} bytes={entry.AudioData.Length} {DebugContext()}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Persistence save failed: {ex.Message} {DebugContext()}");
            }
        }

        private bool TryReadAudioEntryFromDisk(string path, out CachedAudioEntry entry)
        {
            entry = null;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(fs))
                {
                    var signature = reader.ReadString();
                    if (!string.Equals(signature, "MIMC1", StringComparison.Ordinal) && !string.Equals(signature, "MIMC2", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var actorId = reader.ReadInt32();
                    var playerId = string.Empty;
                    if (string.Equals(signature, "MIMC2", StringComparison.Ordinal))
                    {
                        playerId = reader.ReadString();
                    }

                    var sr = reader.ReadInt32();
                    var apply = reader.ReadBoolean();
                    var sourceName = reader.ReadString();
                    var len = reader.ReadInt32();
                    if (len <= 0 || len > 50 * 1024 * 1024)
                    {
                        return false;
                    }

                    var data = reader.ReadBytes(len);
                    if (data.Length != len)
                    {
                        return false;
                    }

                    if (actorId < 0 && TryParsePlayerIdFromAudioFileName(path, out var filePlayerId))
                    {
                        playerId = filePlayerId;
                    }

                    if (string.IsNullOrWhiteSpace(playerId))
                    {
                        playerId = actorId >= 0 ? $"actor_{actorId}" : "unknown";
                    }

                    entry = new CachedAudioEntry
                    {
                        AudioData = data,
                        ApplyVoiceFilter = apply,
                        SampleRate = sr,
                        SourceActor = actorId,
                        SourcePlayerId = playerId,
                        SourceName = sourceName,
                        ReceivedAt = Time.time
                    };

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Persistence read failed file={Path.GetFileName(path)} error={ex.Message} {DebugContext()}");
                return false;
            }
        }

        private static string SanitizePlayerIdForFileName(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return "unknown";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(playerId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }

        private static bool TryParsePlayerIdFromAudioFileName(string filePath, out string playerId)
        {
            playerId = string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            const string prefix = "audio_";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var body = fileName.Substring(prefix.Length);
            var lastSeparator = body.LastIndexOf('_');
            if (lastSeparator <= 0)
            {
                return false;
            }

            playerId = body.Substring(0, lastSeparator);
            return !string.IsNullOrWhiteSpace(playerId);
        }

        private string GetPlayersIndexPath()
        {
            return Path.Combine(GetAudioCacheDirectoryPath(), "players.json");
        }

        private void RegisterPlayerInIndex(string playerId, string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            var normalizedName = string.IsNullOrWhiteSpace(playerName) ? "unknown" : playerName;
            if (!playerNameById.TryGetValue(playerId, out var existingName) || string.IsNullOrWhiteSpace(existingName) || string.Equals(existingName, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                playerNameById[playerId] = normalizedName;
            }
        }

        private void LoadPlayersIndexFromDisk()
        {
            // Index is rebuilt from persisted audio headers on load.
        }

        private void SavePlayersIndexToDisk()
        {
            try
            {
                var dir = GetAudioCacheDirectoryPath();
                Directory.CreateDirectory(dir);

                var ordered = playerNameById.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"players\": [");
                for (var i = 0; i < ordered.Count; i++)
                {
                    var kv = ordered[i];
                    var id = EscapeJsonString(kv.Key);
                    var name = EscapeJsonString(string.IsNullOrWhiteSpace(kv.Value) ? "unknown" : kv.Value);
                    var suffix = i < ordered.Count - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {{ \"id\": \"{id}\", \"name\": \"{name}\" }}{suffix}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                File.WriteAllText(GetPlayersIndexPath(), sb.ToString());
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to save players index JSON: {ex.Message} {DebugContext()}");
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private void EnforcePlayerAudioFileLimit(string playerDir)
        {
            if (string.IsNullOrWhiteSpace(playerDir) || !Directory.Exists(playerDir))
            {
                return;
            }

            var limit = Plugin.configPersistMaxFilesPerPlayer != null ? Plugin.configPersistMaxFilesPerPlayer.Value : 200;
            if (limit < 1)
            {
                return;
            }

            try
            {
                var files = Directory.GetFiles(playerDir, "audio_*_*.bin", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => File.GetCreationTimeUtc(path))
                    .ToList();

                var removeCount = files.Count - limit;
                if (removeCount <= 0)
                {
                    return;
                }

                for (var i = 0; i < removeCount; i++)
                {
                    var file = files[i];
                    File.Delete(file);
                    loadedPersistedFiles.Remove(file);
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to enforce per-player audio file limit: {ex.Message} dir={playerDir} {DebugContext()}");
            }
        }

        private static string GetPlayerPersistentId(Player player)
        {
            if (player == null)
            {
                return "unknown";
            }

            if (!string.IsNullOrWhiteSpace(player.UserId))
            {
                return player.UserId;
            }

            if (player.CustomProperties != null)
            {
                var keys = new[] { "steamid", "steam_id", "SteamId", "SteamID", "playerId", "PlayerId" };
                foreach (var key in keys)
                {
                    if (player.CustomProperties.TryGetValue(key, out var value) && value != null)
                    {
                        var text = value.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }

            return $"actor_{player.ActorNumber}";
        }

        private static string GetPlayerDisplayName(Player player)
        {
            if (player == null)
            {
                return "unknown";
            }

            if (!string.IsNullOrWhiteSpace(player.NickName))
            {
                return player.NickName;
            }

            return $"actor_{player.ActorNumber}";
        }

        private HashSet<string> GetOnlinePlayerIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (PhotonNetwork.PlayerList != null)
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (player == null)
                    {
                        continue;
                    }

                    ids.Add(GetPlayerPersistentId(player));
                }
            }

            return ids;
        }

        private void LogPlayerRecordingSummaryForWorldEntry()
        {
            var online = GetOnlinePlayerIds().OrderBy(x => x).ToList();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in cachedAudio)
            {
                if (entry == null)
                {
                    continue;
                }

                var id = !string.IsNullOrWhiteSpace(entry.SourcePlayerId) ? entry.SourcePlayerId : $"actor_{entry.SourceActor}";
                counts[id] = counts.TryGetValue(id, out var current)
                    ? current + 1
                    : 1;
            }

            var summary = counts.Count == 0
                ? "none"
                : string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

            Log.LogInfo($"World entry player IDs online=[{string.Join(",", online)}] recordingsByPlayer=[{summary}] {DebugContext()}");
        }

        [PunRPC]
        public void ReceiveAudioChunk(byte[] chunk, int chunkIndex, int totalChunks, bool applyFilter, int senderSampleRate, PhotonMessageInfo info)
        {
            // Legacy compatibility path for older senders.
            ReceiveAudioChunkInternal(chunk, chunkIndex, totalChunks, applyFilter, senderSampleRate, "legacy", info);
        }

        [PunRPC]
        public void ReceiveAudioChunkV2(byte[] chunk, int chunkIndex, int totalChunks, bool applyFilter, int senderSampleRate, string transmissionId, PhotonMessageInfo info)
        {
            ReceiveAudioChunkInternal(chunk, chunkIndex, totalChunks, applyFilter, senderSampleRate, transmissionId, info);
        }

        private void ReceiveAudioChunkInternal(byte[] chunk, int chunkIndex, int totalChunks, bool applyFilter, int senderSampleRate, string transmissionId, PhotonMessageInfo info)
        {
            var sender = info.Sender;
            var senderActor = sender != null ? sender.ActorNumber : -1;
            var senderPlayerId = sender != null ? GetPlayerPersistentId(sender) : "unknown";
            var senderName = sender != null ? sender.NickName : "unknown";
            var tx = string.IsNullOrWhiteSpace(transmissionId) ? "legacy" : transmissionId;

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
                ReceivedAt = Time.time
            };
            cachedAudio.Add(entry);

            if (Plugin.configPersistAudioCache != null && Plugin.configPersistAudioCache.Value)
            {
                SaveAudioEntryToDisk(entry);
            }

            DLog($"ReceiveAudioChunk: complete tx={tx} totalBytes={audioData.Length} source={entry.SourceActor}:{entry.SourceName} playerId={entry.SourcePlayerId} cachedTotal={cachedAudio.Count} {DebugContext()}");
            incomingAudioTransmissions.Remove(key);
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
            if (cachedAudio.Count == 0)
            {
                DLog($"TryPlayRandomCachedAudio: cache empty, nothing to play {DebugContext()}");
                return;
            }

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
            if (playableEntries.Count == 0)
            {
                DLog($"TryPlayRandomCachedAudio: no cached clips from players currently in game onlineIds=[{string.Join(",", onlinePlayerIds.OrderBy(x => x))}] cached={cachedAudio.Count} {DebugContext()}");
                return;
            }

            var idx = UnityEngine.Random.Range(0, playableEntries.Count);
            var entry = playableEntries[idx];
            DLog($"TryPlayRandomCachedAudio: selected clip idx={idx} source={entry.SourceActor}:{entry.SourceName} playerId={entry.SourcePlayerId} bytes={entry.AudioData.Length} age={Time.time - entry.ReceivedAt:F1}s {DebugContext()}");
            PlayReceivedAudio(entry.AudioData, entry.SampleRate, entry.SourceActor, entry.SourcePlayerId, entry.SourceName);
        }

        private void PlayReceivedAudio(byte[] audioData, int senderSampleRate, int sourceActor, string sourcePlayerId, string sourceName)
        {
            var playbackFilterEnabled = Plugin.configPlaybackVoiceFilterEnabled == null || Plugin.configPlaybackVoiceFilterEnabled.Value;
            var applyVoiceFilter = playbackFilterEnabled && UnityEngine.Random.value > 0.9f;
            DLog($"PlayReceivedAudio start: bytes={audioData.Length} applyVoiceFilter={applyVoiceFilter} senderSampleRate={senderSampleRate} source={sourceActor}:{sourceName} {DebugContext()}");
            var samples = ConvertByteArrayToFloatArray(audioData, applyVoiceFilter, senderSampleRate);
            var clip = AudioClip.Create("ReceivedClip", samples.Length, 1, senderSampleRate, false);
            clip.SetData(samples, 0);

            DLog($"AudioClip created: samples={samples.Length} lengthSec={clip.length:F2} channels=1 frequency={senderSampleRate} {DebugContext()}");

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
            sourceComponent.volume = Mathf.Clamp01(Plugin.configVoiceVolume.Value / 100f);
            sourceComponent.spatialBlend = 1f;
            sourceComponent.dopplerLevel = 0.5f;
            sourceComponent.minDistance = 1f;
            sourceComponent.maxDistance = 20f;
            sourceComponent.rolloffMode = AudioRolloffMode.Linear;

            if (playerVoiceChat != null)
            {
                sourceComponent.outputAudioMixerGroup = playerVoiceChat.mixerMicrophoneSound;
            }

            sourceComponent.Play();
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
            DLog($"Playback started from reusable anchor at enemy={selected.EnemyName} pos={FormatHudVector(selected.Position)} volume={sourceComponent.volume:F2} clipLen={clip.length:F2} source={sourceActor}:{sourceName} {DebugContext()}");
            StartCoroutine(FollowAudioSourceDuringPlayback(sourceComponent, selected.Enemy, selected.Target, clip.length + 0.1f));
            StartCoroutine(ResetReusableAudioSourceAfterDelay(sourceComponent, clip.length + 0.1f));
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
            var parent = target != null ? target.transform : (enemy != null ? enemy.transform : null);
            if (parent == null)
            {
                return null;
            }

            var key = parent.GetInstanceID();
            if (reusableEnemyAudioSources.TryGetValue(key, out var source) && source != null)
            {
                var sourceTransform = source.transform;
                if (sourceTransform.parent != parent)
                {
                    sourceTransform.SetParent(parent, true);
                }

                sourceTransform.position = position;
                return source;
            }

            var existingAnchor = parent.Find("MimicsAudioAnchor");
            var anchor = existingAnchor != null ? existingAnchor.gameObject : new GameObject("MimicsAudioAnchor");
            anchor.transform.SetParent(parent, true);
            anchor.transform.position = position;

            source = anchor.GetComponent<AudioSource>() ?? anchor.AddComponent<AudioSource>();
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
                    var enemyName = enemy.name.Replace("(Clone)", "").Trim();
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

            var normalized = enemyName.Replace("Enemy - ", "").Trim();
            if (filter.TryGetValue(normalized, out enabled))
            {
                return enabled;
            }

            return true;
        }

        private GameObject GetEnemyAudioTarget(GameObject enemy)
        {
            var t = enemy.transform.Find("Enable/Controller") ?? enemy.transform.Find("Controller");
            return t != null ? t.gameObject : enemy;
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

            var centerProp = runtimeType.GetProperty("CenterTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? runtimeType.GetProperty("centerTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? runtimeType.GetProperty("Transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? runtimeType.GetProperty("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (centerProp != null && centerProp.CanRead)
            {
                var transformValue = centerProp.GetValue(runtimeEnemy, null) as Transform;
                if (transformValue != null)
                {
                    position = transformValue.position;
                    return true;
                }
            }

            var centerField = runtimeType.GetField("CenterTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? runtimeType.GetField("centerTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? runtimeType.GetField("Transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? runtimeType.GetField("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
            var enemyProp = parentType.GetProperty("Enemy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? parentType.GetProperty("enemy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (enemyProp != null && enemyProp.CanRead)
            {
                runtimeEnemy = enemyProp.GetValue(enemyParent, null);
                if (runtimeEnemy != null)
                {
                    return true;
                }
            }

            var enemyField = parentType.GetField("Enemy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? parentType.GetField("enemy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
                    var stateProp = runtimeType.GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? runtimeType.GetProperty("currentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (stateProp != null && stateProp.CanRead)
                    {
                        currentState = stateProp.GetValue(runtimeEnemy, null);
                    }
                    else
                    {
                        var stateField = runtimeType.GetField("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? runtimeType.GetField("currentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

                var stateProp = type.GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? type.GetProperty("currentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (stateProp != null && stateProp.CanRead)
                {
                    stateValue = stateProp.GetValue(component, null);
                }
                else
                {
                    var stateField = type.GetField("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? type.GetField("currentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

        private static void WriteWavHeader(BinaryWriter writer, int sampleCount, int sampleRate)
        {
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + sampleCount * 2);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(sampleCount * 2);
        }

        private static byte[] ConvertFloatArrayToByteArray(float[] audioData)
        {
            var bytes = new byte[audioData.Length * 2];
            for (var i = 0; i < audioData.Length; i++)
            {
                var value = (short)(audioData[i] * 32767f);
                BitConverter.GetBytes(value).CopyTo(bytes, i * 2);
            }

            return bytes;
        }

        private byte[] CombineChunks(List<byte[]> chunks)
        {
            var total = chunks.Sum(chunk => chunk.Length);
            var output = new byte[total];
            var offset = 0;

            foreach (var chunk in chunks)
            {
                Array.Copy(chunk, 0, output, offset, chunk.Length);
                offset += chunk.Length;
            }

            DLog($"CombineChunks: chunkCount={chunks.Count} totalBytes={total} {DebugContext()}");

            return output;
        }

        private List<byte[]> ChunkAudioData(byte[] audioData, int chunkSize)
        {
            var list = new List<byte[]>();
            for (var i = 0; i < audioData.Length; i += chunkSize)
            {
                var len = Mathf.Min(chunkSize, audioData.Length - i);
                var chunk = new byte[len];
                Array.Copy(audioData, i, chunk, 0, len);
                list.Add(chunk);
            }

            DLog($"ChunkAudioData: inputBytes={audioData.Length} chunkSize={chunkSize} chunkCount={list.Count} {DebugContext()}");

            return list;
        }

        private float[] ConvertByteArrayToFloatArray(byte[] bytes, bool applyVoiceFilter, int senderSampleRate)
        {
            var fadeSamples = (int)(senderSampleRate * 0.02f);
            var silencePadding = (int)(senderSampleRate * 0.5f);
            var sampleCount = bytes.Length / 2;
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            }

            samples = ApplyLowPassFilter(samples, 4500f);

            if (applyVoiceFilter)
            {
                var mode = UnityEngine.Random.Range(0, 3);
                if (mode == 0)
                {
                    samples = ApplyPitchShift(samples, 0.5f);
                }
                else if (mode == 1)
                {
                    samples = ApplyPitchShift(samples, 1.2f);
                }
                else
                {
                    samples = ApplyAlienFilter(samples);
                }
            }

            var output = new float[samples.Length + silencePadding * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var gain = 1f;
                if (i < fadeSamples)
                {
                    gain = i / (float)fadeSamples;
                }
                else if (i >= samples.Length - fadeSamples)
                {
                    gain = (samples.Length - i) / (float)fadeSamples;
                }

                output[i + silencePadding] = samples[i] * gain;
            }

            return output;
        }

        private static float[] ApplyPitchShift(float[] samples, float pitchFactor)
        {
            var newLength = (int)(samples.Length / pitchFactor);
            var shifted = new float[newLength];

            for (var i = 0; i < newLength; i++)
            {
                var src = i * pitchFactor;
                var idx = (int)src;
                var frac = src - idx;

                if (idx + 1 < samples.Length)
                {
                    shifted[i] = samples[idx] * (1f - frac) + samples[idx + 1] * frac;
                }
                else if (idx < samples.Length)
                {
                    shifted[i] = samples[idx];
                }
            }

            return shifted;
        }

        private float[] ApplyAlienFilter(float[] samples)
        {
            var output = new float[samples.Length];
            var lfoFreq = 5f;
            var lfoDepth = 0.05f;
            var ringModFreq = 200f;
            var ringModMix = 0.3f;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)sampleRate;
                var lfo = Mathf.Sin(MathF.PI * 2f * lfoFreq * t) * lfoDepth;
                var warpedPos = i * (1f + lfo);

                var idx = (int)warpedPos;
                var frac = warpedPos - idx;
                var dry = 0f;

                if (idx + 1 < samples.Length)
                {
                    dry = samples[idx] * (1f - frac) + samples[idx + 1] * frac;
                }
                else if (idx < samples.Length)
                {
                    dry = samples[idx];
                }

                var ring = Mathf.Sin(MathF.PI * 2f * ringModFreq * t);
                var wet = dry * ring * ringModMix;
                output[i] = Mathf.Clamp(dry * (1f - ringModMix) + wet, -1f, 1f);
            }

            return output;
        }

        private float[] ApplyLowPassFilter(float[] samples, float cutoffFreq)
        {
            var forward = new float[samples.Length];
            var rc = 1f / (MathF.PI * 2f * cutoffFreq);
            var dt = 1f / sampleRate;
            var alpha = dt / (rc + dt);

            forward[0] = samples[0];
            for (var i = 1; i < samples.Length; i++)
            {
                forward[i] = forward[i - 1] + alpha * (samples[i] - forward[i - 1]);
            }

            var backward = new float[samples.Length];
            backward[samples.Length - 1] = forward[samples.Length - 1];
            for (var i = samples.Length - 2; i >= 0; i--)
            {
                backward[i] = backward[i + 1] + alpha * (forward[i] - backward[i + 1]);
            }

            return backward;
        }
    }

    [BepInPlugin("TFS_Mimics", "TFS_Mimics", "1.1.6")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource PluginLogger;
        private static Harmony harmony;

        public static ConfigEntry<bool> configDebugVerbose;
        public static ConfigEntry<int> configVoiceVolume;
        public static ConfigEntry<int> configPlaybackNearRadius;
        public static ConfigEntry<int> configMinDelay;
        public static ConfigEntry<int> configMaxDelay;
        public static ConfigEntry<bool> configHearYourself;
        public static ConfigEntry<bool> configFilterEnabled;
        public static ConfigEntry<bool> configPlaybackVoiceFilterEnabled;
        public static ConfigEntry<int> configSamplingRate;
        public static ConfigEntry<bool> configPersistAudioCache;
        public static ConfigEntry<int> configPersistMaxFilesPerPlayer;

        public static readonly Dictionary<string, ConfigEntry<bool>> enemyConfigEntries = new Dictionary<string, ConfigEntry<bool>>();

        private void Awake()
        {
            PluginLogger = base.Logger;
            PluginLogger.LogInfo("Plugin TFS_Mimics loaded.");

            configVoiceVolume = Config.Bind("General", "Volume", 10, new ConfigDescription("Volume of mimic voices (percent).", new AcceptableValueRange<int>(0, 20), Array.Empty<object>()));
            configPlaybackNearRadius = Config.Bind("General", "Playback Near Radius", 15, new ConfigDescription("Preferred radius around local player for selecting a mimic playback enemy.", new AcceptableValueRange<int>(5, 100), Array.Empty<object>()));
            configMinDelay = Config.Bind("General", "MinDelay", 15, new ConfigDescription("Minimum delay before record/play.", new AcceptableValueRange<int>(5, 300), Array.Empty<object>()));
            configMaxDelay = Config.Bind("General", "MaxDelay", 60, new ConfigDescription("Maximum delay before record/play.", new AcceptableValueRange<int>(10, 600), Array.Empty<object>()));
            configHearYourself = Config.Bind("General", "Hear Yourself?", true, "If false, only other clients hear mimic playback.");
            configPlaybackVoiceFilterEnabled = Config.Bind("General", "Playback Voice Filters Enabled", true, "If false, playback never applies pitch/alien voice filters.");
            configPersistAudioCache = Config.Bind("General", "Persist Audio Cache", true, "If true, received mimic audio clips are saved to disk and loaded on world entry.");
            configPersistMaxFilesPerPlayer = Config.Bind("General", "Persist Max Files Per Player", 100, new ConfigDescription("Maximum number of persisted recordings to keep per player folder.", new AcceptableValueRange<int>(1, 5000), Array.Empty<object>()));
            configDebugVerbose = Config.Bind("Debug", "Verbose Logging", false, "Enable very detailed debug logs for the whole mimic pipeline.");
            configSamplingRate = Config.Bind("Experimental", "Sampling Rate", 48000, new ConfigDescription("Microphone/sample rate.", new AcceptableValueRange<int>(16000, 48000), Array.Empty<object>()));
            configFilterEnabled = Config.Bind("Filter", "Filter Enabled?", false, "Enable per-enemy mimic filter.");

            harmony = new Harmony("TFS_Mimics");
            harmony.PatchAll();

            patches.EnemyDirectorStartPatch.Initialize(Config);
        }
    }
}

namespace TFS_Mimics.patches
{
    [HarmonyPatch(typeof(EnemyDirector))]
    internal class EnemyDirectorStartPatch
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("TFS_Mimics");

        private static HashSet<string> filterEnemies = new HashSet<string>();
        public static IReadOnlyCollection<string> KnownEnemyNames => filterEnemies;
        private static bool setupComplete;
        private static ConfigFile configFile;

        private static void DLog(string message)
        {
            if (Plugin.configDebugVerbose != null && Plugin.configDebugVerbose.Value)
            {
                Log.LogInfo(message);
            }
        }

        public static void Initialize(ConfigFile config)
        {
            configFile = config;
        }

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void SetupEnemies(EnemyDirector __instance)
        {
            if (setupComplete || configFile == null)
            {
                return;
            }

            var pools = new[]
            {
                __instance.enemiesDifficulty1,
                __instance.enemiesDifficulty2,
                __instance.enemiesDifficulty3
            };

            filterEnemies = new HashSet<string>();
            foreach (var list in pools)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (var enemy in list)
                {
                    if (enemy == null || enemy.spawnObjects == null || enemy.spawnObjects.Count == 0)
                    {
                        continue;
                    }

                    var first = enemy.spawnObjects[0];
                    if (first == null)
                    {
                        continue;
                    }

                    var firstName = first.PrefabName;
                    if (string.IsNullOrWhiteSpace(firstName))
                    {
                        continue;
                    }

                    var name = firstName;
                    if (firstName.Contains("Director") && enemy.spawnObjects.Count > 1 && enemy.spawnObjects[1] != null && !string.IsNullOrWhiteSpace(enemy.spawnObjects[1].PrefabName))
                    {
                        name = enemy.spawnObjects[1].PrefabName;
                    }

                    filterEnemies.Add(name);
                }
            }

            foreach (var enemyName in filterEnemies)
            {
                Plugin.enemyConfigEntries[enemyName] = configFile.Bind(
                    "Enemies",
                    enemyName,
                    true,
                    "Enables/disables mimic for " + enemyName
                );
            }

            var orderedNames = filterEnemies
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n)
                .ToList();

            Log.LogInfo($"Registered enemy entities: count={orderedNames.Count}");
            Log.LogInfo("Registered enemy entities list: " + string.Join(", ", orderedNames));

            if (Plugin.configDebugVerbose != null && Plugin.configDebugVerbose.Value)
            {
                foreach (var enemyName in orderedNames)
                {
                    Log.LogInfo("Registered enemy entity -> " + enemyName);
                }
            }

            setupComplete = true;
            DLog("Enemy filter config initialized.");
        }
    }

    public class MimicsFinder : MonoBehaviour
    {
        private static MimicsFinder instance;

        public static TFS_Mimics LocalMimics { get; set; }

        public static void EnsureInitialized()
        {
            if (instance != null)
            {
                return;
            }

            instance = new GameObject("MimicsFinder").AddComponent<MimicsFinder>();
            DontDestroyOnLoad(instance.gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                LocalMimics = null;
                instance = null;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), "Awake")]
    internal class PlayerAvatarPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerAvatar __instance)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                return;
            }

            var mimics = __instance.GetComponent<TFS_Mimics>();
            if (mimics == null)
            {
                mimics = __instance.gameObject.AddComponent<TFS_Mimics>();
            }

            var view = __instance.GetComponent<PhotonView>();
            if (view != null && view.IsMine)
            {
                MimicsFinder.LocalMimics = mimics;
            }
        }
    }

    [HarmonyPatch(typeof(LocalVoiceFramed<short>), "PushDataAsync")]
    internal class LocalVoiceFramedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(short[] buf)
        {
            MimicsFinder.EnsureInitialized();

            var local = MimicsFinder.LocalMimics;
            if (local == null || local.photonView == null || !local.photonView.IsMine)
            {
                return;
            }

            local.ProcessVoiceData(buf);
        }
    }
}
