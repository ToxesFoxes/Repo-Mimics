using System;
using System.Diagnostics;
using HarmonyLib;
using Photon.Voice;

namespace TFS_Mimics.patches
{
    [HarmonyPatch(typeof(LocalVoiceFramed<short>), "PushDataAsync")]
    internal class LocalVoiceFramedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(LocalVoiceFramed<short> __instance, short[] buf)
        {
            VoiceInputStats.Add(__instance, buf);
            VoiceDataBus.Dispatch(buf);
        }
    }

    // Verbose-only: periodic level summary of the captured mic stream, to tune VAD against the game's input.
    internal static class VoiceInputStats
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _nextReportMs;
        private static int _frames;
        private static double _sumSq;
        private static long _count;
        private static float _maxFrameRms;
        private static int _peak;

        internal static void Add(LocalVoice voice, short[] buf)
        {
            if (buf == null || buf.Length == 0 || Plugin.configDebugVerbose == null || !Plugin.configDebugVerbose.Value)
                return;

            double frameSq = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                var s = buf[i];
                var a = s < 0 ? -s : s;
                if (a > _peak) _peak = a;
                var n = s / 32768.0;
                frameSq += n * n;
            }

            _sumSq += frameSq;
            _count += buf.Length;
            _frames++;
            var frameRms = (float)Math.Sqrt(frameSq / buf.Length);
            if (frameRms > _maxFrameRms) _maxFrameRms = frameRms;

            var now = Clock.ElapsedMilliseconds;
            if (now < _nextReportMs)
                return;

            _nextReportMs = now + 2000;
            var info = voice?.Info;
            var vad = VoiceDataBus.LocalMimics?.GetVadDebugString() ?? "n/a";
            Plugin.PluginLogger.LogInfo(
                $"[VoiceInput] voice={voice?.GetType().Name} rate={info?.SamplingRate} ch={info?.Channels} " +
                $"frames={_frames} frameLen={buf.Length} avgRms={Math.Sqrt(_sumSq / Math.Max(1, _count)):F4} " +
                $"maxFrameRms={_maxFrameRms:F4} peak={_peak / 32768f:F3} {vad}");

            _frames = 0;
            _sumSq = 0;
            _count = 0;
            _maxFrameRms = 0f;
            _peak = 0;
        }
    }
}
