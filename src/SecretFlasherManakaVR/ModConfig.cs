using BepInEx.Configuration;
using BepInEx.Logging;
using SecretFlasherManakaVR.OpenVR;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;

namespace SecretFlasherManakaVR;

public enum MirrorMode
{
    MainCamera,
    LeftEye,
    RightEye,
    Disabled
}

public sealed class ModConfig
{
    private const string GeneralSection = "General";
    private const string StereoSection = "Stereo";
    private const string InputSection = "Input";
    private const string CompatibilitySection = "Compatibility";
    private const string DebugSection = "Debug";

    private ModConfig(ConfigFile config)
    {
        EnableVR = config.Bind(
            GeneralSection,
            nameof(EnableVR),
            true,
            "Enable the SteamVR/OpenVR runtime. If initialization fails, the plugin logs the error and leaves the normal game running.");

        AutoStartSteamVR = config.Bind(
            GeneralSection,
            nameof(AutoStartSteamVR),
            true,
            "Allow the runtime layer to ask OpenVR to start SteamVR if it is not already running.");

        IPDScale = config.Bind(
            StereoSection,
            nameof(IPDScale),
            1.0f,
            new ConfigDescription(
                "Multiplier applied to the HMD eye separation reported by OpenVR.",
                new AcceptableValueRange<float>(0.1f, 3.0f)));

        WorldScale = config.Bind(
            StereoSection,
            nameof(WorldScale),
            1.0f,
            new ConfigDescription(
                "Multiplier converting OpenVR meters into Unity world units.",
                new AcceptableValueRange<float>(0.01f, 100.0f)));

        CameraHeightOffset = config.Bind(
            StereoSection,
            nameof(CameraHeightOffset),
            0.0f,
            new ConfigDescription(
                "Additional vertical offset in Unity world units applied to the VR camera rig.",
                new AcceptableValueRange<float>(-5.0f, 5.0f)));

        RecenteringKey = config.Bind(
            InputSection,
            nameof(RecenteringKey),
            KeyCode.F12,
            "Keyboard key used to recenter the current HMD forward direction.");

        AutoRecenterOnStart = config.Bind(
            InputSection,
            nameof(AutoRecenterOnStart),
            true,
            "Automatically recenter once when the first valid HMD pose is received. This prevents SteamVR standing height from being added on top of the game camera.");

        SuppressMouseLookInput = config.Bind(
            InputSection,
            nameof(SuppressMouseLookInput),
            true,
            "When VR is active, force legacy Mouse X/Y axes to zero so keyboard movement remains usable while HMD pose owns camera look.");

        SourceRotationMode = config.Bind(
            StereoSection,
            nameof(SourceRotationMode),
            VrSourceRotationMode.SourceYawOnly,
            "Controls how much of the game camera rotation is used as the VR base. SourceYawOnly avoids mouse pitch/roll fighting the HMD.");

        MirrorMode = config.Bind(
            GeneralSection,
            nameof(MirrorMode),
            SecretFlasherManakaVR.MirrorMode.MainCamera,
            "Controls what the normal desktop window shows while VR output is active.");

        LogPoseDebug = config.Bind(
            DebugSection,
            nameof(LogPoseDebug),
            false,
            "Write verbose HMD pose diagnostics to the BepInEx log.");

        SceneTransitionVrPauseSeconds = config.Bind(
            CompatibilitySection,
            nameof(SceneTransitionVrPauseSeconds),
            1.5f,
            new ConfigDescription(
                "Pause VR eye rendering briefly after scene changes while reflection objects are sanitized. This reduces scene-transition hangs and GPU spikes.",
                new AcceptableValueRange<float>(0.0f, 10.0f)));

        RenderScale = config.Bind(
            StereoSection,
            nameof(RenderScale),
            0.5f,
            new ConfigDescription(
                "Scale applied to the OpenVR recommended eye texture size. Lower values are safer while stabilizing the injected renderer.",
                new AcceptableValueRange<float>(0.25f, 1.5f)));

        UseOpenVRProjection = config.Bind(
            StereoSection,
            nameof(UseOpenVRProjection),
            true,
            "Use OpenVR eye projection matrices for correct stereo. Disable only if stereo projection itself is broken.");

        OpenVRProjectionMode = config.Bind(
            StereoSection,
            nameof(OpenVRProjectionMode),
            SecretFlasherManakaVR.OpenVR.OpenVRProjectionMode.RawSwapVertical,
            "OpenVR projection conversion mode. RawSwapVertical is the current default; try RawInvertVertical or ValveMatrix if scenes disappear.");

        UseSourceProjectionForCulling = config.Bind(
            StereoSection,
            nameof(UseSourceProjectionForCulling),
            true,
            "Use the game camera projection for Unity culling while rendering with OpenVR projection. This keeps scene objects from disappearing with injected asymmetric eye frustums.");

        FlipSubmitV = config.Bind(
            StereoSection,
            nameof(FlipSubmitV),
            true,
            "Flip submitted eye texture V coordinates for OpenVR. Unity D3D render textures otherwise appear upside down in SteamVR on this path.");

        SuppressBlackCensorOnChangeNullRefs = config.Bind(
            CompatibilitySection,
            nameof(SuppressBlackCensorOnChangeNullRefs),
            true,
            "Suppress repeated NullReferenceException throws from BlackCensorController.OnChange. This prevents the game's delayed option-change scheduler from log-spamming or stalling VR gameplay.");

        DisableReflectionCameras = config.Bind(
            CompatibilitySection,
            nameof(DisableReflectionCameras),
            true,
            "Disable likely mirror/reflection cameras while VR is active. This avoids recursive or duplicate mirror rendering from injected stereo eye cameras.");

        DisableTargetTextureCameras = config.Bind(
            CompatibilitySection,
            nameof(DisableTargetTextureCameras),
            true,
            "Also disable non-source/non-VR cameras that render to a RenderTexture while VR eyes render. Mirrors often use unnamed target-texture cameras.");

        KeepReflectionCamerasDisabledWhileVrActive = config.Bind(
            CompatibilitySection,
            nameof(KeepReflectionCamerasDisabledWhileVrActive),
            true,
            "Keep reflection/target-texture cameras disabled for the whole active VR scene instead of only during the VR eye render. This is more aggressive but avoids mirrors resuming their render loop between VR frames.");

        PreventReflectionReenableWhileVrActive = config.Bind(
            CompatibilitySection,
            nameof(PreventReflectionReenableWhileVrActive),
            true,
            "Block scripts from re-enabling reflection cameras while VR is active. This targets per-frame mirror visibility scripts that revive disabled mirror cameras.");

        BlockReflectionCameraRenderWhileVrActive = config.Bind(
            CompatibilitySection,
            nameof(BlockReflectionCameraRenderWhileVrActive),
            true,
            "Block manual Camera.Render calls from likely reflection/target-texture cameras while VR is active, even outside the plugin's own eye render.");

        BlockNestedCameraRenderDuringVrRender = config.Bind(
            CompatibilitySection,
            nameof(BlockNestedCameraRenderDuringVrRender),
            true,
            "Skip manual Camera.Render calls from non-VR-eye cameras while the plugin is rendering VR eyes. This targets reflection scripts that render recursively during stereo rendering.");

        LogReflectionCameraDiagnostics = config.Bind(
            CompatibilitySection,
            nameof(LogReflectionCameraDiagnostics),
            true,
            "Log a one-shot list of active cameras and reflection-camera candidates to verify whether the mirror camera is being caught.");

        ReflectionCameraNameKeywords = config.Bind(
            CompatibilitySection,
            nameof(ReflectionCameraNameKeywords),
            "mirror,reflect,reflection,planar,water",
            "Comma-separated name keywords used to identify mirror/reflection cameras for DisableReflectionCameras.");
    }

    public ConfigEntry<bool> EnableVR { get; }
    public ConfigEntry<bool> AutoStartSteamVR { get; }
    public ConfigEntry<float> IPDScale { get; }
    public ConfigEntry<float> WorldScale { get; }
    public ConfigEntry<float> CameraHeightOffset { get; }
    public ConfigEntry<KeyCode> RecenteringKey { get; }
    public ConfigEntry<bool> AutoRecenterOnStart { get; }
    public ConfigEntry<bool> SuppressMouseLookInput { get; }
    public ConfigEntry<VrSourceRotationMode> SourceRotationMode { get; }
    public ConfigEntry<MirrorMode> MirrorMode { get; }
    public ConfigEntry<bool> LogPoseDebug { get; }
    public ConfigEntry<float> SceneTransitionVrPauseSeconds { get; }
    public ConfigEntry<float> RenderScale { get; }
    public ConfigEntry<bool> UseOpenVRProjection { get; }
    public ConfigEntry<OpenVRProjectionMode> OpenVRProjectionMode { get; }
    public ConfigEntry<bool> UseSourceProjectionForCulling { get; }
    public ConfigEntry<bool> FlipSubmitV { get; }
    public ConfigEntry<bool> SuppressBlackCensorOnChangeNullRefs { get; }
    public ConfigEntry<bool> DisableReflectionCameras { get; }
    public ConfigEntry<bool> DisableTargetTextureCameras { get; }
    public ConfigEntry<bool> KeepReflectionCamerasDisabledWhileVrActive { get; }
    public ConfigEntry<bool> PreventReflectionReenableWhileVrActive { get; }
    public ConfigEntry<bool> BlockReflectionCameraRenderWhileVrActive { get; }
    public ConfigEntry<bool> BlockNestedCameraRenderDuringVrRender { get; }
    public ConfigEntry<bool> LogReflectionCameraDiagnostics { get; }
    public ConfigEntry<string> ReflectionCameraNameKeywords { get; }

    public static ModConfig Bind(ConfigFile config, ManualLogSource logger)
    {
        var settings = new ModConfig(config);
        logger.LogInfo("VR configuration bound.");
        return settings;
    }
}
