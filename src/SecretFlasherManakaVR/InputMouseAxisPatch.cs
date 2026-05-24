using System;
using HarmonyLib;
using UnityEngine;

namespace SecretFlasherManakaVR;

[HarmonyPatch(typeof(Input), nameof(Input.GetAxis), typeof(string))]
internal static class InputGetAxisPatch
{
    private static void Postfix(string axisName, ref float __result)
    {
        if (ShouldSuppress(axisName))
        {
            __result = 0.0f;
        }
    }

    private static bool ShouldSuppress(string axisName)
    {
        return InputMouseAxisPatchShared.ShouldSuppress(axisName);
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw), typeof(string))]
internal static class InputGetAxisRawPatch
{
    private static void Postfix(string axisName, ref float __result)
    {
        if (InputMouseAxisPatchShared.ShouldSuppress(axisName))
        {
            __result = 0.0f;
        }
    }
}

internal static class InputMouseAxisPatchShared
{
    public static bool ShouldSuppress(string axisName)
    {
        return VrRuntimeState.IsVrReady &&
            Plugin.Settings != null &&
            Plugin.Settings.SuppressMouseLookInput.Value &&
            IsMouseLookAxis(axisName);
    }

    private static bool IsMouseLookAxis(string axisName)
    {
        return !string.IsNullOrEmpty(axisName) &&
            (axisName.Equals("Mouse X", StringComparison.OrdinalIgnoreCase) ||
            axisName.Equals("Mouse Y", StringComparison.OrdinalIgnoreCase));
    }
}
