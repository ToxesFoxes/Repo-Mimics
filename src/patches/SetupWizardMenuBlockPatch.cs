using HarmonyLib;

namespace TFS_Mimics.patches
{
    // While the setup wizard is open, the game's menu must not react to the mouse under it
    // (IMGUI doesn't block the game's own UI hover/click detection).
    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.UIMouseHover))]
    internal class SetupWizardMenuBlockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!MimicsSetupWizard.IsOpen)
                return true;

            __result = false;
            return false;
        }
    }
}
