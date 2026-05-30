using SecretFlasherManakaVR.Runtime;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal readonly struct Quest3CursorRay
{
    public Quest3CursorRay(Vector3 origin, Vector3 direction, float length)
    {
        Origin = origin;
        Direction = direction.sqrMagnitude <= 0.0001f ? Vector3.forward : direction.normalized;
        Length = Mathf.Max(0.01f, length);
    }

    public Vector3 Origin { get; }

    public Vector3 Direction { get; }

    public float Length { get; }

    public Vector3 End => Origin + Direction * Length;

    public static bool TryCreate(Quest3VirtualInputState state, out Quest3CursorRay ray)
    {
        ray = default;
        if (!state.IsCursorMode || !state.Snapshot.Right.HasPose)
        {
            return false;
        }

        var pose = state.Snapshot.Right.Pose;
        if (!VrRuntimeState.TryTransformTrackingPose(pose.Position, pose.Rotation, out var start, out var rotation))
        {
            return false;
        }

        var settings = Plugin.Settings;
        Vector3 localDirection = ResolveLocalDirection(settings == null
            ? Quest3CursorRayDirection.Forward
            : settings.Quest3CursorRayDirection.Value);
        float length = settings == null ? 6.0f : Mathf.Clamp(settings.Quest3CursorRayLength.Value, 0.25f, 30.0f);
        Quaternion correction = settings == null
            ? Quaternion.identity
            : Quaternion.Euler(
                settings.Quest3CursorRayPitchOffsetDegrees.Value,
                settings.Quest3CursorRayYawOffsetDegrees.Value,
                settings.Quest3CursorRayRollOffsetDegrees.Value);

        ray = new Quest3CursorRay(start, rotation * correction * localDirection, length);
        return true;
    }

    private static Vector3 ResolveLocalDirection(Quest3CursorRayDirection direction)
    {
        return direction switch
        {
            Quest3CursorRayDirection.Forward => Vector3.forward,
            Quest3CursorRayDirection.Up => Vector3.up,
            Quest3CursorRayDirection.Down => Vector3.down,
            Quest3CursorRayDirection.Right => Vector3.right,
            Quest3CursorRayDirection.Left => Vector3.left,
            _ => Vector3.back
        };
    }
}
