using ExposureUnnoticed2.Scripts.Base;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SecretFlasherManakaVR.InputMapping;

[HarmonyPatch(typeof(InputManager), nameof(InputManager.IsDown), typeof(InputManager.InputType))]
internal static class Quest3InputManagerIsDownPatch
{
    private static void Postfix(InputManager.InputType type, ref bool __result)
    {
        Quest3InputSystem.TryApplyInputDown(type, ref __result);
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.IsDownFrame), typeof(InputManager.InputType))]
internal static class Quest3InputManagerIsDownFramePatch
{
    private static void Postfix(InputManager.InputType type, ref bool __result)
    {
        Quest3InputSystem.TryApplyInputDownFrame(type, ref __result);
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.IsUpFrame), typeof(InputManager.InputType))]
internal static class Quest3InputManagerIsUpFramePatch
{
    private static void Postfix(InputManager.InputType type, ref bool __result)
    {
        Quest3InputSystem.TryApplyInputUpFrame(type, ref __result);
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetShortUp), typeof(InputManager.InputType), typeof(int))]
internal static class Quest3InputManagerGetShortUpPatch
{
    private static void Postfix(InputManager.InputType type, ref bool __result)
    {
        Quest3InputSystem.TryApplyInputUpFrame(type, ref __result);
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetVector2), typeof(InputManager.InputType))]
internal static class Quest3InputManagerGetVector2Patch
{
    private static void Postfix(InputManager.InputType type, ref Vector2 __result)
    {
        Quest3InputSystem.TryApplyVector2(type, ref __result);
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetVector3), typeof(InputManager.InputType))]
internal static class Quest3InputManagerGetVector3Patch
{
    private static void Postfix(InputManager.InputType type, ref Vector3 __result)
    {
        Quest3InputSystem.TryApplyVector3(type, ref __result);
    }
}

[HarmonyPatch(typeof(Input), "get_mousePosition")]
internal static class Quest3LegacyInputMousePositionPatch
{
    private static void Postfix(ref Vector3 __result)
    {
        Quest3InputSystem.TryGetMousePosition(ref __result);
    }
}

[HarmonyPatch(typeof(BaseInput), "get_mousePosition")]
internal static class Quest3BaseInputMousePositionPatch
{
    private static void Postfix(ref Vector2 __result)
    {
        Vector3 mouse = new Vector3(__result.x, __result.y, 0.0f);
        if (Quest3InputSystem.TryGetMousePosition(ref mouse))
        {
            __result = new Vector2(mouse.x, mouse.y);
        }
    }
}

[HarmonyPatch(typeof(Input), "get_mouseScrollDelta")]
internal static class Quest3LegacyInputMouseScrollDeltaPatch
{
    private static void Postfix(ref Vector2 __result)
    {
        Quest3InputSystem.TryGetMouseScrollDelta(ref __result);
    }
}

[HarmonyPatch(typeof(BaseInput), "get_mouseScrollDelta")]
internal static class Quest3BaseInputMouseScrollDeltaPatch
{
    private static void Postfix(ref Vector2 __result)
    {
        Quest3InputSystem.TryGetMouseScrollDelta(ref __result);
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton), typeof(int))]
internal static class Quest3LegacyInputGetMouseButtonPatch
{
    private static void Postfix(int button, ref bool __result)
    {
        __result = __result || Quest3InputSystem.IsMouseButtonDown(button);
    }
}

[HarmonyPatch(typeof(BaseInput), nameof(BaseInput.GetMouseButton), typeof(int))]
internal static class Quest3BaseInputGetMouseButtonPatch
{
    private static void Postfix(int button, ref bool __result)
    {
        __result = __result || Quest3InputSystem.IsMouseButtonDown(button);
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown), typeof(int))]
internal static class Quest3LegacyInputGetMouseButtonDownPatch
{
    private static void Postfix(int button, ref bool __result)
    {
        __result = __result || Quest3InputSystem.IsMouseButtonDownFrame(button);
    }
}

[HarmonyPatch(typeof(BaseInput), nameof(BaseInput.GetMouseButtonDown), typeof(int))]
internal static class Quest3BaseInputGetMouseButtonDownPatch
{
    private static void Postfix(int button, ref bool __result)
    {
        __result = __result || Quest3InputSystem.IsMouseButtonDownFrame(button);
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp), typeof(int))]
internal static class Quest3LegacyInputGetMouseButtonUpPatch
{
    private static void Postfix(int button, ref bool __result)
    {
        __result = __result || Quest3InputSystem.IsMouseButtonUpFrame(button);
    }
}

[HarmonyPatch(typeof(BaseInput), nameof(BaseInput.GetMouseButtonUp), typeof(int))]
internal static class Quest3BaseInputGetMouseButtonUpPatch
{
    private static void Postfix(int button, ref bool __result)
    {
        __result = __result || Quest3InputSystem.IsMouseButtonUpFrame(button);
    }
}
