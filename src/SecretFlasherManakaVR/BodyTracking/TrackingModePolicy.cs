namespace SecretFlasherManakaVR;

public enum BodyTrackingMode
{
    Legacy = 0,
    ThreePoint = 3,
    SixPoint = 6,
    EightPoint = 8,
    TenPoint = 10,
    ElevenPoint = 11
}

// Selection owns behavior even while calibration is pending or devices are lost.
internal static class TrackingModePolicy
{
    internal static bool IsKnown(BodyTrackingMode mode) => mode is BodyTrackingMode.Legacy
        or BodyTrackingMode.ThreePoint or BodyTrackingMode.SixPoint or BodyTrackingMode.EightPoint
        or BodyTrackingMode.TenPoint or BodyTrackingMode.ElevenPoint;
    internal static bool IsSupported(BodyTrackingMode mode) => mode is BodyTrackingMode.Legacy or BodyTrackingMode.ThreePoint;
    internal static bool UsesLegacyView(BodyTrackingMode mode) => mode == BodyTrackingMode.Legacy;
    internal static bool AllowsLegacyBones(BodyTrackingMode mode) => mode == BodyTrackingMode.Legacy;
    internal static bool CanCalibrate(BodyTrackingMode mode) => mode == BodyTrackingMode.ThreePoint;
    internal static bool NeedsViewReset(BodyTrackingMode? previous, BodyTrackingMode current, bool wasActive, bool active)
        => previous != current || (wasActive && !active);
}
