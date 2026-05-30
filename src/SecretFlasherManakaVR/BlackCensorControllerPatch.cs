using System;
using System.Reflection;
using HarmonyLib;

namespace SecretFlasherManakaVR;

[HarmonyPatch]
internal static class BlackCensorControllerOnChangePatch
{
    private const string ControllerTypeName = "ExposureUnnoticed2.Object3D.Player.Scripts.Other.BlackCensorController";
    private const string EventTypeName = "ExposureUnnoticed2.Scripts.Base.OptionChangeEvent";

    private static MethodBase? targetMethod;

    private static bool Prepare()
    {
        Type? controllerType = AccessTools.TypeByName(ControllerTypeName);
        Type? eventType = AccessTools.TypeByName(EventTypeName);
        if (controllerType == null || eventType == null)
        {
            return false;
        }

        targetMethod = AccessTools.Method(controllerType, "OnChange", new[] { eventType });
        return targetMethod != null;
    }

    private static MethodBase? TargetMethod()
    {
        return targetMethod;
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (!ShouldSuppress(__exception))
        {
            return __exception;
        }

        return null;
    }

    private static bool ShouldSuppress(Exception exception)
    {
        if (Plugin.Settings == null || !Plugin.Settings.SuppressBlackCensorOnChangeNullRefs.Value)
        {
            return false;
        }

        if (exception is NullReferenceException)
        {
            return true;
        }

        string exceptionText = exception.ToString();
        return exception.GetType().FullName == "Il2CppInterop.Runtime.Il2CppException" &&
            exceptionText.IndexOf("System.NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
