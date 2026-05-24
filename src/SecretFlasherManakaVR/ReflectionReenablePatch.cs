using HarmonyLib;
using UnityEngine;

namespace SecretFlasherManakaVR;

[HarmonyPatch(typeof(Behaviour), "set_enabled")]
internal static class BehaviourSetEnabledPatch
{
    private static float nextLogTime;
    private static int blockedSinceLastLog;

    private static bool Prefix(Behaviour __instance, bool value)
    {
        if (!value || !ShouldBlock(__instance))
        {
            return true;
        }

        blockedSinceLastLog++;
        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + 5.0f;
            Plugin.Logger.LogInfo(
                "Blocked reflection camera re-enable while VR is active: " +
                ReflectionBlocker.CameraDescription((Camera)__instance) +
                " count=" + blockedSinceLastLog + ".");
            blockedSinceLastLog = 0;
        }

        return false;
    }

    private static bool ShouldBlock(Behaviour behaviour)
    {
        return VrRuntimeState.IsVrReady &&
            Plugin.Settings != null &&
            Plugin.Settings.PreventReflectionReenableWhileVrActive.Value &&
            behaviour is Camera camera &&
            ReflectionBlocker.IsReflectionCameraCandidate(camera);
    }
}
