using ExposureUnnoticed2.ObjectUI.NpcUi;
using HarmonyLib;
using SecretFlasherManakaVR.Runtime;

namespace SecretFlasherManakaVR
{
    [HarmonyPatch(typeof(NpcUiView), "LateUpdate")]
    internal static class NpcUiLateUpdatePatch
    {
        private static void Postfix(NpcUiView __instance)
        {
            NpcWorldSpaceUiFixer.ApplyNpcUiPostLateUpdate(__instance);
        }
    }
}
