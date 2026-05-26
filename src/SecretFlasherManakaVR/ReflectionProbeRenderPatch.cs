using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace SecretFlasherManakaVR;

[HarmonyPatch]
internal static class ReflectionProbeRenderPatch
{
    private static float nextLogTime;
    private static int suppressedSinceLastLog;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        MethodInfo? renderProbe = AccessTools.Method(typeof(ReflectionProbe), nameof(ReflectionProbe.RenderProbe), Type.EmptyTypes);
        if (renderProbe != null)
        {
            yield return renderProbe;
        }

        MethodInfo? renderProbeToTexture = AccessTools.Method(
            typeof(ReflectionProbe),
            nameof(ReflectionProbe.RenderProbe),
            new[] { typeof(RenderTexture) });
        if (renderProbeToTexture != null)
        {
            yield return renderProbeToTexture;
        }

        MethodInfo? scheduleRender = AccessTools.Method(
            typeof(ReflectionProbe),
            nameof(ReflectionProbe.ScheduleRender),
            new[] { typeof(ReflectionProbeTimeSlicingMode), typeof(RenderTexture) });
        if (scheduleRender != null)
        {
            yield return scheduleRender;
        }
    }

    private static bool Prefix(ReflectionProbe __instance, ref int __result)
    {
        if (!ShouldBlock(__instance))
        {
            return true;
        }

        __result = -1;
        suppressedSinceLastLog++;
        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + 5.0f;
            Plugin.Logger.LogInfo(
                "Blocked ReflectionProbe render while VR is active: " +
                ReflectionBlocker.ProbeDescription(__instance) +
                " count=" + suppressedSinceLastLog + ".");
            suppressedSinceLastLog = 0;
        }

        return false;
    }

    private static bool ShouldBlock(ReflectionProbe probe)
    {
        return VrRuntimeState.IsVrReady &&
            Plugin.Settings != null &&
            Plugin.Settings.BlockReflectionProbeRenderWhileVrActive.Value &&
            ReflectionBlocker.IsReflectionProbeCandidate(probe);
    }
}
