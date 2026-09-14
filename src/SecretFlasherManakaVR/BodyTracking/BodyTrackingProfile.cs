using BepInEx.Configuration;

namespace SecretFlasherManakaVR;

public sealed class BodyTrackingProfile
{
    internal BodyTrackingProfile(ConfigFile config, string section, float scale = 1f, bool autoScale = true)
    {
        TrackingScale = config.Bind(section, nameof(TrackingScale), scale,
            new ConfigDescription("Extra multiplier applied to all device positions and eye separation after calibration.", new AcceptableValueRange<float>(.5f, 1.5f)));
        AutoScale = config.Bind(section, nameof(AutoScale), autoScale, "Fit avatar eye height to standing HMD height when calibrating.");
        EyeHeightOffset = config.Bind(section, nameof(EyeHeightOffset), .055f,
            new ConfigDescription("Eye height above the avatar head bone. Shared by the camera and hand mapping; requires recalibration.", new AcceptableValueRange<float>(-.2f, .2f)));
        EyeForwardOffset = config.Bind(section, nameof(EyeForwardOffset), .075f,
            new ConfigDescription("Eye position in front of the avatar head bone in the calibrated frame; requires recalibration.", new AcceptableValueRange<float>(-.2f, .2f)));
        HideHeadInFirstPerson = config.Bind(section, nameof(HideHeadInFirstPerson), true,
            "Hide the neck/head only during first-person eye rendering, restoring bone scale afterwards.");
    }

    public ConfigEntry<float> TrackingScale { get; }
    public ConfigEntry<bool> AutoScale { get; }
    public ConfigEntry<float> EyeHeightOffset { get; }
    public ConfigEntry<float> EyeForwardOffset { get; }
    public ConfigEntry<bool> HideHeadInFirstPerson { get; }
}
