using HarmonyLib;

namespace TFS_Mimics.patches
{
    // Who hurt an enemy? EnemyHealth.Hurt has no source, so the callers that run around it leave a note:
    // a HurtCollider (attacks, traps, pits, weapons) or a physics object hitting the enemy's body.
    // Enemy damage only happens on the master client.
    internal static class EnemyHurtSource
    {
        internal static HurtCollider Collider;
        internal static PhysGrabObjectImpactDetector Impact;
    }

    [HarmonyPatch(typeof(HurtCollider), "EnemyHurt")]
    internal class EnemyHurtSourceColliderPatch
    {
        [HarmonyPrefix]
        private static void Prefix(HurtCollider __instance) => EnemyHurtSource.Collider = __instance;

        [HarmonyFinalizer]
        private static void Finalizer() => EnemyHurtSource.Collider = null;
    }

    [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "OnCollisionStay")]
    internal class EnemyHurtSourceImpactPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PhysGrabObjectImpactDetector __instance) => EnemyHurtSource.Impact = __instance;

        [HarmonyFinalizer]
        private static void Finalizer() => EnemyHurtSource.Impact = null;
    }

    [HarmonyPatch(typeof(EnemyHealth), "Hurt")]
    internal class EnemyHurtDirectorPatch
    {
        [HarmonyPrefix]
        private static void Prefix(EnemyHealth __instance, out int __state) => __state = __instance.healthCurrent;

        [HarmonyPostfix]
        private static void Postfix(EnemyHealth __instance, int __state)
        {
            var lost = __state - __instance.healthCurrent;
            if (lost <= 0 || __instance.enemy == null) return;
            TFS_Mimics.DirectorOnEnemyHurt(__instance.enemy, lost, EnemyHurtSource.Collider, EnemyHurtSource.Impact);
        }
    }
}
