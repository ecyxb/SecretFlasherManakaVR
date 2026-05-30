using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecretFlasherManakaVR;

[HarmonyPatch]
internal static class CameraRenderPatch
{
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
