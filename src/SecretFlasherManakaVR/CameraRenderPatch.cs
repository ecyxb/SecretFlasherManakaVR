using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecretFlasherManakaVR;

[HarmonyPatch]
internal static class CameraRenderPatch
{
    private static float nextLogTime;
    private static int suppressedSinceLastLog;

    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Camera), nameof(Camera.Render), Type.EmptyTypes);
    }

    private static bool Prefix(Camera __instance)
    {
        if (!ShouldBlock(__instance))
        {
            return true;
        }

        suppressedSinceLastLog++;
        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + 5.0f;
            Plugin.Logger.LogInfo(
                "Blocked reflection Camera.Render while VR is active: " +
                ReflectionBlocker.CameraDescription(__instance) +
                " count=" + suppressedSinceLastLog + ".");
            suppressedSinceLastLog = 0;
        }

        return false;
    }

    private static bool ShouldBlock(Camera camera)
    {
        if (!VrRuntimeState.IsVrReady || Plugin.Settings == null || camera == null)
        {
            return false;
        }

        if (VrRuntimeState.IsAllowedVrEyeRenderCamera(camera))
        {
            return false;
        }

        bool nestedNonVrCamera = VrRuntimeState.IsRenderingVrEyes &&
            Plugin.Settings.BlockNestedCameraRenderDuringVrRender.Value;
        bool reflectionCamera = Plugin.Settings.BlockReflectionCameraRenderWhileVrActive.Value &&
            ReflectionBlocker.IsReflectionCameraCandidate(camera);

        return nestedNonVrCamera || reflectionCamera;
    }
}
