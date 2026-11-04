using HarmonyLib;
using UnityEngine;

namespace TFS_Mimics.patches
{
    // Stage 2: after the game sets the jaw from its own audio, override it with ours when the mimic voice is louder.
    // Each postfix reuses the enemy's own jaw formula so it looks native.

    [HarmonyPatch(typeof(EnemyBang), "TalkLogic")]
    internal class BangLipSyncPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnemyBang __instance, float ___talkClipLoudness)
        {
            if (__instance.enemy == null || __instance.enemy.Health.dead) return;
            var loudness = MimicMouths.ClipLoudness(__instance.enemy);
            if (loudness <= ___talkClipLoudness) return;
            __instance.talkTopTarget.localRotation = Quaternion.Euler(loudness * -45f, 0f, 0f);
            __instance.talkBottomTarget.localRotation = Quaternion.Euler(loudness * 90f, 0f, 0f);
        }
    }

    [HarmonyPatch(typeof(EnemyElsa), "BarkLogic")]
    internal class ElsaLipSyncPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnemyElsa __instance, float ___barkClipLoudness)
        {
            if (__instance.enemy == null || __instance.IsBig()) return; // the jaw rig belongs to the small form
            var loudness = MimicMouths.ClipLoudness(__instance.enemy);
            if (loudness <= ___barkClipLoudness) return;
            __instance.jawTransformTarget.localRotation = Quaternion.Euler(loudness * 72f - 6f, 0f, 0f);
            __instance.jawTransform.localRotation = SemiFunc.SpringQuaternionGet(__instance.jawSpring, __instance.jawTransformTarget.localRotation);
        }
    }

    [HarmonyPatch(typeof(EnemyOogly), "CodeAnimatedTalk")]
    internal class OoglyLipSyncPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnemyOogly __instance, ref float ___talkVolume, Quaternion ___jawStartRotation,
            ref Quaternion ___jawTargetRotation, SpringQuaternion ___jawSpring)
        {
            if (__instance.enemy == null) return;
            var volume = Mathf.Min(60f, MimicMouths.SpectrumVolume(__instance.enemy));
            if (volume <= ___talkVolume) return;
            ___talkVolume = volume;
            ___jawTargetRotation = ___jawStartRotation * Quaternion.Euler(volume, 0f, 0f);
            __instance.jawTransform.localRotation = SemiFunc.SpringQuaternionGet(___jawSpring, ___jawTargetRotation);
        }
    }
}
