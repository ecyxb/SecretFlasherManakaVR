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
                "Blocked reflection object re-enable while VR is active: " +
                Description(__instance) +
                " count=" + blockedSinceLastLog + ".");
            blockedSinceLastLog = 0;
        }

        return false;
    }

    private static bool ShouldBlock(Behaviour behaviour)
    {
        return VrRuntimeState.IsVrReady &&
            !VrRuntimeState.IsRestoringSuppressedObjects &&
            Plugin.Settings != null &&
            Plugin.Settings.PreventReflectionReenableWhileVrActive.Value &&
            ((behaviour is Camera camera && ReflectionBlocker.IsReflectionCameraCandidate(camera)) ||
             (behaviour is ReflectionProbe probe && ReflectionBlocker.IsReflectionProbeCandidate(probe)) ||
             ReflectionBlocker.IsMirrorManagerCandidate(behaviour));
    }

    private static string Description(Behaviour behaviour)
    {
        if (behaviour is Camera camera)
        {
            return ReflectionBlocker.CameraDescription(camera);
        }

        if (behaviour is ReflectionProbe probe)
        {
            return ReflectionBlocker.ProbeDescription(probe);
        }

        return behaviour == null ? "<null>" : behaviour.GetType().FullName;
    }
}
