using System.Numerics;

namespace SecretFlasherManakaVR;

// Tracking-space deltas expressed in the calibrated forward frame, independent of game root motion.
internal static class TrackingSpaceMath
{
    internal static Vector3 CameraHeadOffset(BodyTrackingMode mode, Vector3 raw, bool legacyClamp, Vector3 minimum, Vector3 maximum)
        => TrackingModePolicy.UsesLegacyView(mode) && legacyClamp ? Vector3.Clamp(raw, minimum, maximum) : raw;

    internal static float EyeSeparation(float physicalIpd, float ipdScale, float trackingScale)
        => physicalIpd * ipdScale * trackingScale;

    internal static Vector3 PositionDelta(Vector3 position, Vector3 origin, Quaternion inverseYaw, float scale)
        => Vector3.Transform(position - origin, inverseYaw) * scale;

    internal static Quaternion RotationDelta(Quaternion rotation, Quaternion origin, Quaternion inverseYaw)
        => Quaternion.Normalize(inverseYaw * rotation * Quaternion.Inverse(origin) * Quaternion.Inverse(inverseYaw));

    // All devices share the HMD calibration origin. An animation's wrist position is
    // subtracted only to encode the solver offset, never used as a tracking anchor.
    internal static Vector3 HandPositionOffset(Vector3 position, Vector3 headOrigin, Quaternion inverseYaw,
        float scale, Vector3 avatarEye, Vector3 animationHand)
        => avatarEye + PositionDelta(position, headOrigin, inverseYaw, scale) - animationHand;
}
