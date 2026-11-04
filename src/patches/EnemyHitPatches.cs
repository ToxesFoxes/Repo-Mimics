using HarmonyLib;

namespace TFS_Mimics.patches
{
    // Enemy attacks go through HurtCollider with enemyHost set; enemy logic only runs on the master client.

    [HarmonyPatch(typeof(HurtCollider), "EnemyHurt")]
    internal class EnemyHitEnemyDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HurtCollider __instance, Enemy _enemy, bool __result)
        {
            if (__result && __instance.enemyHost != null && _enemy != null && _enemy != __instance.enemyHost)
                TFS_Mimics.DirectorOnEnemyHitEnemy(__instance.enemyHost, _enemy);
        }
    }

    [HarmonyPatch(typeof(HurtCollider), "PhysObjectHurt")]
    internal class EnemyHitObjectDirectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HurtCollider __instance, PhysGrabObject physGrabObject, HurtCollider.BreakImpact impact, bool __result)
        {
            if (__result && __instance.enemyHost != null && physGrabObject != null && impact != HurtCollider.BreakImpact.None)
                TFS_Mimics.DirectorOnEnemyHitObject(__instance.enemyHost, physGrabObject);
        }
    }
}
