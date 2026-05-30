using System;
using HarmonyLib;
using SecretFlasherManakaVR.InputMapping;
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
            return;
        }

        if (InputMouseAxisPatchShared.IsMouseScrollWheelAxis(axisName))
        {
            Quest3InputSystem.TryGetMouseScrollWheelAxis(ref __result);
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
            return;
        }

        if (InputMouseAxisPatchShared.IsMouseScrollWheelAxis(axisName))
        {
            Quest3InputSystem.TryGetMouseScrollWheelAxis(ref __result);
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

    public static bool IsMouseScrollWheelAxis(string axisName)
    {
        return !string.IsNullOrEmpty(axisName) &&
            axisName.Equals("Mouse ScrollWheel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMouseLookAxis(string axisName)
    {
        return !string.IsNullOrEmpty(axisName) &&
            (axisName.Equals("Mouse X", StringComparison.OrdinalIgnoreCase) ||
            axisName.Equals("Mouse Y", StringComparison.OrdinalIgnoreCase));
    }
}
