using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;

namespace SecretFlasherManakaVR;

[HarmonyPatch(typeof(InGameManager), nameof(InGameManager.OnLateUpdate))]
internal static class InGameManagerLateUpdatePatch
{
    private static void Postfix()
    {
        VrRunnerHost.Instance?.LateTickFromGame();
    }
}
