using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Photon.Pun;
using Photon.Realtime;
using Photon.Voice;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics : MonoBehaviour
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
        private float vadHoldUntil;

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

        private static string FormatHudVector(Vector3 value)
        {
            return $"({value.x:F1}, {value.y:F1}, {value.z:F1})";
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
                _debugWindowOpen = false;
                _fpmOpen = false;
                _debugWindowFocused = true;
                _showGizmos = false;
                DestroyAllOverlays();
                SetCursorForGui(false);
            }

            if (!inLevel)
            {
                return;
            }

            DebugGuiUpdate();
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

            StartCoroutine(InitializeVoiceChat(avatar));
        }

        // Finds PlayerVoiceChat via component search first, then falls back to reflection.
        // Avoids storing a FieldInfo member whose sole purpose mirrors the original mod's approach.
        private IEnumerator InitializeVoiceChat(PlayerAvatar avatar)
        {
            // Direct component search — works if PlayerVoiceChat is on this or a child object.
            playerVoiceChat = GetComponentInChildren<PlayerVoiceChat>(true);
            if (playerVoiceChat == null)
            {
                playerVoiceChat = GetComponentInParent<PlayerVoiceChat>();
            }

            if (playerVoiceChat == null)
            {
                // Reflection fallback: the field may not yet be set, so poll each frame.
                var field = typeof(PlayerAvatar).GetField("voiceChat", BindingFlags.Instance | BindingFlags.NonPublic);
                var waits = 0;
                while (playerVoiceChat == null)
                {
                    if (field != null)
                    {
                        playerVoiceChat = field.GetValue(avatar) as PlayerVoiceChat;
                    }

                    if (playerVoiceChat == null)
                    {
                        waits++;
                        if (waits % 120 == 0)
                        {
                            DLog($"Waiting for PlayerVoiceChat via reflection... frames={waits} {DebugContext()}");
                        }

                        yield return null;
                    }
                }

                DLog($"PlayerVoiceChat found via reflection after waitFrames={waits} {DebugContext()}");
            }
            else
            {
                DLog($"PlayerVoiceChat found via component search {DebugContext()}");
            }

            if (photonView.IsMine && SemiFunc.RunIsLevel())
            {
                EnsurePersistenceInitialized();
                EnsureCustomAudioLoaded();
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
    }
}