using System.Collections.Generic;
using UnityEngine;

namespace SecretFlasherManakaVR;

internal static class VrRuntimeState
{
    public static bool IsVrReady { get; set; }

    public static bool IsRenderingVrEyes { get; private set; }

    public static bool IsRestoringSuppressedObjects { get; private set; }

    public static bool HasHeadPose { get; private set; }

    public static Vector3 HeadPosition { get; private set; }

    public static Quaternion HeadRotation { get; private set; } = Quaternion.identity;

    private static readonly HashSet<int> AllowedRenderCameraIds = new HashSet<int>();

    public static void SetHeadPose(Vector3 position, Quaternion rotation)
    {
        HeadPosition = position;
        HeadRotation = rotation;
        HasHeadPose = true;
    }

    public static void ClearHeadPose()
    {
        HeadPosition = Vector3.zero;
        HeadRotation = Quaternion.identity;
        HasHeadPose = false;
    }

    public static void BeginVrEyeRender(params Camera[] cameras)
    {
        AllowedRenderCameraIds.Clear();
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null)
            {
                AllowedRenderCameraIds.Add(camera.GetInstanceID());
            }
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
