using HarmonyLib;
using UnityEngine;

namespace SecretFlasherManakaVR;

[HarmonyPatch(typeof(Behaviour), "set_enabled")]
internal static class BehaviourSetEnabledPatch
{
    private static bool Prefix(Behaviour __instance, bool value)
    {
        if (!value || !ShouldBlock(__instance))
        {
            return true;
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
}
