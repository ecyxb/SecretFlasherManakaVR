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

public enum Quest3CursorRayDirection
{
    Forward,
    Back,
    Up,
    Down,
    Right,
    Left
}

public sealed class ModConfig
{
    private const string GeneralSection = "General";
    private const string StereoSection = "Stereo";
    private const string InputSection = "Input";
    private const string CompatibilitySection = "Compatibility";
    private const string VrUiSection = "VR UI";
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

        EnableQuest3InputMapping = config.Bind(
            InputSection,
            nameof(EnableQuest3InputMapping),
            true,
            "Enable Quest 3 controller mapping through SteamVR/OpenVR input. Disable to leave the game's original input untouched.");

        Quest3InputActionManifestPath = config.Bind(
            InputSection,
            nameof(Quest3InputActionManifestPath),
            string.Empty,
            "Optional absolute path to the SteamVR action manifest. Empty uses BepInEx/plugins/SecretFlasherManakaVR_Input/actions.json when present.");

        Quest3LongPressSeconds = config.Bind(
            InputSection,
            nameof(Quest3LongPressSeconds),
            0.5f,
            new ConfigDescription(
                "Long-press threshold used by the Quest 3 input state machine.",
                new AcceptableValueRange<float>(0.1f, 2.0f)));

        Quest3RightStickDeadzone = config.Bind(
            InputSection,
            nameof(Quest3RightStickDeadzone),
            0.35f,
            new ConfigDescription(
                "Deadzone for cursor-mode right-stick Q/E/scroll mapping.",
                new AcceptableValueRange<float>(0.0f, 0.95f)));

        Quest3RightStickDiagonalGuardDegrees = config.Bind(
            InputSection,
            nameof(Quest3RightStickDiagonalGuardDegrees),
            15.0f,
            new ConfigDescription(
                "Invalid angle around right-stick diagonals in cursor mode.",
                new AcceptableValueRange<float>(0.0f, 40.0f)));

        Quest3TriggerPressThreshold = config.Bind(
            InputSection,
            nameof(Quest3TriggerPressThreshold),
            0.35f,
            new ConfigDescription(
                "Analog Quest 3 trigger pull value treated as a button press.",
                new AcceptableValueRange<float>(0.01f, 0.95f)));

        Quest3CursorRayDirection = config.Bind(
            InputSection,
            nameof(Quest3CursorRayDirection),
            SecretFlasherManakaVR.Quest3CursorRayDirection.Forward,
            "Controller-local axis used for the visible cursor ray. Change if the ray starts at the controller but points in the wrong direction.");

        Quest3CursorRayLength = config.Bind(
            InputSection,
            nameof(Quest3CursorRayLength),
            6.0f,
            new ConfigDescription(
                "Visible length of the Quest 3 cursor ray in Unity world units.",
                new AcceptableValueRange<float>(0.25f, 30.0f)));

        Quest3CursorRayPitchOffsetDegrees = config.Bind(
            InputSection,
            nameof(Quest3CursorRayPitchOffsetDegrees),
            0.0f,
            new ConfigDescription(
                "Extra local pitch correction for the Quest 3 cursor ray. Use this when SteamVR supplies a grip/raw pose instead of a true pointer pose.",
                new AcceptableValueRange<float>(-90.0f, 90.0f)));

        Quest3CursorRayYawOffsetDegrees = config.Bind(
            InputSection,
            nameof(Quest3CursorRayYawOffsetDegrees),
            0.0f,
            new ConfigDescription(
                "Extra local yaw correction for the Quest 3 cursor ray.",
                new AcceptableValueRange<float>(-90.0f, 90.0f)));

        Quest3CursorRayRollOffsetDegrees = config.Bind(
            InputSection,
            nameof(Quest3CursorRayRollOffsetDegrees),
            0.0f,
            new ConfigDescription(
                "Extra local roll correction for the Quest 3 cursor ray.",
                new AcceptableValueRange<float>(-90.0f, 90.0f)));

        LogActiveUiOnInput = config.Bind(
            InputSection,
            nameof(LogActiveUiOnInput),
            false,
            "Log active POP/Circle UI context when Quest 3 input is received.");

        LogInputConsumers = config.Bind(
            InputSection,
            nameof(LogInputConsumers),
            false,
            "Log the game input methods consumed by Quest 3 virtual input.");

        LogCurrentSelectedUi = config.Bind(
            InputSection,
            nameof(LogCurrentSelectedUi),
            false,
            "Include EventSystem.current.currentSelectedGameObject in Quest 3 input diagnostics.");

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

        DisableSourceCameraRendering = config.Bind(
            CompatibilitySection,
            nameof(DisableSourceCameraRendering),
            false,
            "Disable the game source camera component while VR is active, using it only as a transform/settings template for the VR eye cameras.");

        DisableMirrorManagersWhileVrActive = config.Bind(
            CompatibilitySection,
            nameof(DisableMirrorManagersWhileVrActive),
            false,
            "Disable AkilliMum MirrorManager components while VR is active. This is safer than patching mirror render callbacks and allows experimenting with source-camera desktop rendering.");

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

        DisableReflectionProbes = config.Bind(
            CompatibilitySection,
            nameof(DisableReflectionProbes),
            false,
            "Disable ReflectionProbe components while VR is active. Night scenes can create internal reflection probe cameras that bypass Camera.Render patches.");

        LogReflectionProbeDiagnostics = config.Bind(
            CompatibilitySection,
            nameof(LogReflectionProbeDiagnostics),
            true,
            "Log a one-shot list of active ReflectionProbe components per scene while VR is active.");

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

        EnableVrUiBridge = config.Bind(
            VrUiSection,
            nameof(EnableVrUiBridge),
            true,
            "Convert supported game UI canvases so they are visible in the VR eye cameras.");

        ConvertOverlayCanvasToWorldSpace = config.Bind(
            VrUiSection,
            nameof(ConvertOverlayCanvasToWorldSpace),
            true,
            "Capture supported screen-space canvases into a VR texture panel while VR is active.");

        VrUiFollowMode = config.Bind(
            VrUiSection,
            nameof(VrUiFollowMode),
            SecretFlasherManakaVR.Runtime.VrUiFollowMode.HeadLocked,
            "Controls where converted UI appears. HeadLocked keeps it in front of the headset.");

        VrUiDistance = config.Bind(
            VrUiSection,
            nameof(VrUiDistance),
            1.4f,
            new ConfigDescription(
                "Distance in Unity world units from the headset/source camera to the converted UI plane.",
                new AcceptableValueRange<float>(0.25f, 5.0f)));

        VrUiScale = config.Bind(
            VrUiSection,
            nameof(VrUiScale),
            0.001f,
            new ConfigDescription(
                "Legacy UI scale setting kept for config compatibility. The RenderTexture panel size is driven by VrUiDistance.",
                new AcceptableValueRange<float>(0.0001f, 0.02f)));

        VrUiVerticalOffset = config.Bind(
            VrUiSection,
            nameof(VrUiVerticalOffset),
            -0.1f,
            new ConfigDescription(
                "Vertical offset in headset/source-camera local space for converted UI.",
                new AcceptableValueRange<float>(-2.0f, 2.0f)));

        VrUiMaxScanInterval = config.Bind(
            VrUiSection,
            nameof(VrUiMaxScanInterval),
            1.0f,
            new ConfigDescription(
                "How often the UI bridge scans for new overlay canvases after scene changes or menu changes.",
                new AcceptableValueRange<float>(0.25f, 10.0f)));

        VrUiCanvasNameWhitelist = config.Bind(
            VrUiSection,
            nameof(VrUiCanvasNameWhitelist),
            string.Empty,
            "Optional comma-separated canvas name keywords. When set, only matching canvases are converted.");

        VrUiCanvasNameBlacklist = config.Bind(
            VrUiSection,
            nameof(VrUiCanvasNameBlacklist),
            string.Empty,
            "Optional comma-separated canvas name keywords. Matching canvases are not converted.");

        LogVrUiDiagnostics = config.Bind(
            VrUiSection,
            nameof(LogVrUiDiagnostics),
            false,
            "Log UI capture panel diagnostics from the VR UI bridge.");

        FixNpcWorldSpaceUi = config.Bind(
            VrUiSection,
            nameof(FixNpcWorldSpaceUi),
            false,
            "Experimental. While VR is active, reproject NPC head markers with the HMD pose before the HUD is captured.");

        NpcWorldSpaceUiVerticalOffset = config.Bind(
            VrUiSection,
            nameof(NpcWorldSpaceUiVerticalOffset),
            0.25f,
            new ConfigDescription(
                "Additional world-space height above the NPC head for the NPC marker VR fix.",
                new AcceptableValueRange<float>(-1.0f, 2.0f)));

        NpcWorldSpaceUiScale = config.Bind(
            VrUiSection,
            nameof(NpcWorldSpaceUiScale),
            0.0015f,
            new ConfigDescription(
                "World-space scale for NPC circular/question UI when the VR NPC UI fix is enabled.",
                new AcceptableValueRange<float>(0.0002f, 0.02f)));

        NpcWorldSpaceUiMinScaleDistance = config.Bind(
            VrUiSection,
            nameof(NpcWorldSpaceUiMinScaleDistance),
            3.0f,
            new ConfigDescription(
                "Closest distance used for NPC world-space UI scale compensation.",
                new AcceptableValueRange<float>(0.25f, 50.0f)));

        NpcWorldSpaceUiMaxScaleDistance = config.Bind(
            VrUiSection,
            nameof(NpcWorldSpaceUiMaxScaleDistance),
            7.0f,
            new ConfigDescription(
                "Farthest distance used for NPC world-space UI scale compensation.",
                new AcceptableValueRange<float>(0.25f, 100.0f)));

        LogNpcWorldSpaceUiDiagnostics = config.Bind(
            VrUiSection,
            nameof(LogNpcWorldSpaceUiDiagnostics),
            false,
            "Log one-shot hierarchy diagnostics for NPC head markers and nearby UI canvases while VR is active.");
    }

    public ConfigEntry<bool> EnableVR { get; }
    public ConfigEntry<bool> AutoStartSteamVR { get; }
    public ConfigEntry<float> IPDScale { get; }
    public ConfigEntry<float> WorldScale { get; }
    public ConfigEntry<float> CameraHeightOffset { get; }
    public ConfigEntry<KeyCode> RecenteringKey { get; }
    public ConfigEntry<bool> AutoRecenterOnStart { get; }
    public ConfigEntry<bool> SuppressMouseLookInput { get; }
    public ConfigEntry<bool> EnableQuest3InputMapping { get; }
    public ConfigEntry<string> Quest3InputActionManifestPath { get; }
    public ConfigEntry<float> Quest3LongPressSeconds { get; }
    public ConfigEntry<float> Quest3RightStickDeadzone { get; }
    public ConfigEntry<float> Quest3RightStickDiagonalGuardDegrees { get; }
    public ConfigEntry<float> Quest3TriggerPressThreshold { get; }
    public ConfigEntry<Quest3CursorRayDirection> Quest3CursorRayDirection { get; }
    public ConfigEntry<float> Quest3CursorRayLength { get; }
    public ConfigEntry<float> Quest3CursorRayPitchOffsetDegrees { get; }
    public ConfigEntry<float> Quest3CursorRayYawOffsetDegrees { get; }
    public ConfigEntry<float> Quest3CursorRayRollOffsetDegrees { get; }
    public ConfigEntry<bool> LogActiveUiOnInput { get; }
    public ConfigEntry<bool> LogInputConsumers { get; }
    public ConfigEntry<bool> LogCurrentSelectedUi { get; }
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
    public ConfigEntry<bool> DisableSourceCameraRendering { get; }
    public ConfigEntry<bool> DisableMirrorManagersWhileVrActive { get; }
    public ConfigEntry<bool> DisableTargetTextureCameras { get; }
    public ConfigEntry<bool> KeepReflectionCamerasDisabledWhileVrActive { get; }
    public ConfigEntry<bool> PreventReflectionReenableWhileVrActive { get; }
    public ConfigEntry<bool> BlockReflectionCameraRenderWhileVrActive { get; }
    public ConfigEntry<bool> BlockNestedCameraRenderDuringVrRender { get; }
    public ConfigEntry<bool> DisableReflectionProbes { get; }
    public ConfigEntry<bool> LogReflectionProbeDiagnostics { get; }
    public ConfigEntry<bool> LogReflectionCameraDiagnostics { get; }
    public ConfigEntry<string> ReflectionCameraNameKeywords { get; }
    public ConfigEntry<bool> EnableVrUiBridge { get; }
    public ConfigEntry<bool> ConvertOverlayCanvasToWorldSpace { get; }
    public ConfigEntry<VrUiFollowMode> VrUiFollowMode { get; }
    public ConfigEntry<float> VrUiDistance { get; }
    public ConfigEntry<float> VrUiScale { get; }
    public ConfigEntry<float> VrUiVerticalOffset { get; }
    public ConfigEntry<float> VrUiMaxScanInterval { get; }
    public ConfigEntry<string> VrUiCanvasNameWhitelist { get; }
    public ConfigEntry<string> VrUiCanvasNameBlacklist { get; }
    public ConfigEntry<bool> LogVrUiDiagnostics { get; }
    public ConfigEntry<bool> FixNpcWorldSpaceUi { get; }
    public ConfigEntry<float> NpcWorldSpaceUiVerticalOffset { get; }
    public ConfigEntry<float> NpcWorldSpaceUiScale { get; }
    public ConfigEntry<float> NpcWorldSpaceUiMinScaleDistance { get; }
    public ConfigEntry<float> NpcWorldSpaceUiMaxScaleDistance { get; }
    public ConfigEntry<bool> LogNpcWorldSpaceUiDiagnostics { get; }

    public static ModConfig Bind(ConfigFile config, ManualLogSource logger)
    {
        var settings = new ModConfig(config);
        logger.LogInfo("VR configuration bound.");
        return settings;
    }
}
