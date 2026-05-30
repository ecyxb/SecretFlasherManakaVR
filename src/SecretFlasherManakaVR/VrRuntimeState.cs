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

    public static Quaternion HeadTrackingRotation { get; private set; } = Quaternion.identity;

    public static Camera? SourceCamera { get; private set; }

    public static int RecenterSerial { get; private set; }

    private static bool recenterRequested;
    private static Vector3 trackingBasePosition;
    private static Quaternion trackingBaseRotation = Quaternion.identity;
    private static Vector3 trackingRecenterPosition;
    private static Quaternion trackingRecenterYaw = Quaternion.identity;
    private static float trackingWorldScale = 1.0f;
    private static bool trackingRecenterSet;
    private static bool hasTrackingToWorldTransform;

    private static readonly HashSet<int> AllowedRenderCameraIds = new HashSet<int>();

    public static void SetHeadPose(Vector3 position, Quaternion rotation)
    {
        HeadPosition = position;
        HeadRotation = rotation;
        HeadTrackingRotation = rotation;
        HasHeadPose = true;
    }

    public static void SetHeadPose(Vector3 position, Quaternion rotation, Quaternion trackingRotation)
    {
        HeadPosition = position;
        HeadRotation = rotation;
        HeadTrackingRotation = trackingRotation;
        HasHeadPose = true;
    }

    public static void ClearHeadPose()
    {
        HeadPosition = Vector3.zero;
        HeadRotation = Quaternion.identity;
        HeadTrackingRotation = Quaternion.identity;
        HasHeadPose = false;
        hasTrackingToWorldTransform = false;
    }

    public static void SetSourceCamera(Camera? camera)
    {
        SourceCamera = camera;
    }

    public static void SetTrackingToWorldTransform(
        Vector3 basePosition,
        Quaternion baseRotation,
        Vector3 recenterPosition,
        Quaternion recenterYaw,
        float worldScale,
        bool recenterSet)
    {
        trackingBasePosition = basePosition;
        trackingBaseRotation = baseRotation;
        trackingRecenterPosition = recenterPosition;
        trackingRecenterYaw = recenterYaw;
        trackingWorldScale = Mathf.Max(0.0001f, worldScale);
        trackingRecenterSet = recenterSet;
        hasTrackingToWorldTransform = true;
    }

    public static bool TryTransformTrackingPose(Vector3 trackingPosition, Quaternion trackingRotation, out Vector3 worldPosition, out Quaternion worldRotation)
    {
        if (!hasTrackingToWorldTransform)
        {
            worldPosition = trackingPosition;
            worldRotation = trackingRotation;
            return false;
        }

        Vector3 scaledPosition = trackingPosition * trackingWorldScale;
        Quaternion rotation = trackingRotation;
        if (trackingRecenterSet)
        {
            scaledPosition = trackingRecenterYaw * (scaledPosition - trackingRecenterPosition);
            rotation = trackingRecenterYaw * rotation;
        }

        worldPosition = trackingBasePosition + trackingBaseRotation * scaledPosition;
        worldRotation = trackingBaseRotation * rotation;
        return true;
    }

    public static void RequestRecenter()
    {
        recenterRequested = true;
    }

    public static void MarkRecentered()
    {
        RecenterSerial++;
    }

    public static bool ConsumeRecenterRequest()
    {
        if (!recenterRequested)
        {
            return false;
        }

        recenterRequested = false;
        return true;
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
