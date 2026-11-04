using UnityEngine;
using UnityEngine.Audio;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        // ─── Make a mimic voice sound like a real player's voice ──────────────────
        // Players' voices go through the game's voice mixer group (room reverb, the voice volume slider) and are
        // muffled behind walls by AudioLowPassLogic. Mimic voices get the same treatment, so a wall or another room
        // doesn't give them away. Off → the old direct output, always clear.
        private static AudioMixerGroup _playerVoiceMixerGroup;

        private static AudioMixerGroup GetPlayerVoiceMixerGroup()
        {
            if (_playerVoiceMixerGroup != null) return _playerVoiceMixerGroup;
            foreach (var avatar in SemiFunc.PlayerGetList())
            {
                if (avatar != null && avatar.voiceChat != null && avatar.voiceChat.mixerMicrophoneSound != null)
                {
                    _playerVoiceMixerGroup = avatar.voiceChat.mixerMicrophoneSound;
                    break;
                }
            }
            return _playerVoiceMixerGroup;
        }

        // Distance falloff exactly like a player's voice source (set on the game's prefab), so a mimic up close
        // is no louder than a teammate standing there.
        private static AudioSource _playerVoiceSource;

        private static void CopyPlayerVoiceFalloff(AudioSource source)
        {
            if (_playerVoiceSource == null)
            {
                foreach (var avatar in SemiFunc.PlayerGetList())
                {
                    if (avatar != null && avatar.voiceChat != null && avatar.voiceChat.audioSource != null)
                    {
                        _playerVoiceSource = avatar.voiceChat.audioSource;
                        break;
                    }
                }
            }
            var reference = _playerVoiceSource;
            if (reference == null) return;

            source.rolloffMode = reference.rolloffMode;
            source.minDistance = reference.minDistance;
            // A voice behind a wall has its range shrunk by the game; its low-pass logic remembers the real one.
            var logic = reference.GetComponent<AudioLowPassLogic>();
            source.maxDistance = logic != null && logic.Falloff > 0f ? logic.Falloff : reference.maxDistance;
            source.spread = reference.spread;
            if (reference.rolloffMode == AudioRolloffMode.Custom)
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, reference.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        }

        // Call after the source's volume / maxDistance / spatialBlend are set and right before Play().
        private static void RouteLikePlayerVoice(AudioSource source)
        {
            if (source == null) return;
            var useGame = Plugin.configGameVoiceRouting == null || Plugin.configGameVoiceRouting.Value;
            var group = useGame ? GetPlayerVoiceMixerGroup() : null;
            source.outputAudioMixerGroup = group;

            var logic = source.GetComponent<AudioLowPassLogic>();
            if (!useGame || group == null)
            {
                if (logic != null) logic.enabled = false;
                var filter = source.GetComponent<AudioLowPassFilter>();
                if (filter != null) filter.cutoffFrequency = 22000f;
                return;
            }

            CopyPlayerVoiceFalloff(source);

            if (logic == null) logic = source.gameObject.AddComponent<AudioLowPassLogic>(); // adds AudioLowPassFilter too
            // The logic remembers volume, falloff and spatial blend at setup and drives them from there,
            // so it's re-armed for every clip with this playback's values.
            logic.enabled = false;
            logic.Fetch = true;
            logic.volumeFetched = false;
            logic.enabled = true;
            logic.Setup();
        }
    }
}
