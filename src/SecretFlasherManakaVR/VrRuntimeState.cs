using System.Collections.Generic;
using UnityEngine;

namespace SecretFlasherManakaVR;

internal static class VrRuntimeState
{
    public static bool IsVrReady { get; set; }

    public static bool IsRenderingVrEyes { get; private set; }

    public static bool IsRestoringSuppressedObjects { get; private set; }

    private static readonly HashSet<int> AllowedRenderCameraIds = new HashSet<int>();

    public static void BeginVrEyeRender(Camera leftEye, Camera rightEye)
    {
        AllowedRenderCameraIds.Clear();
        if (leftEye != null)
        {
            AllowedRenderCameraIds.Add(leftEye.GetInstanceID());
        }

        if (rightEye != null)
        {
            AllowedRenderCameraIds.Add(rightEye.GetInstanceID());
        }

        IsRenderingVrEyes = true;
    }

    public static void EndVrEyeRender()
    {
        IsRenderingVrEyes = false;
        AllowedRenderCameraIds.Clear();
    }

    public static bool IsAllowedVrEyeRenderCamera(Camera camera)
    {
        return camera != null && AllowedRenderCameraIds.Contains(camera.GetInstanceID());
    }

    public static void BeginSuppressedObjectRestore()
    {
        IsRestoringSuppressedObjects = true;
    }

    public static void EndSuppressedObjectRestore()
    {
        IsRestoringSuppressedObjects = false;
    }
}
