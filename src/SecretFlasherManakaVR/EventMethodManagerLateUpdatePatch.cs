using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;

namespace SecretFlasherManakaVR;

[HarmonyPatch(typeof(EventMethodManager), "LateUpdate")]
internal static class EventMethodManagerLateUpdatePatch
{
    private static void Postfix()
    {
        VrRunnerHost.Instance?.LateTickFromGameLateUpdate();
    }
}
