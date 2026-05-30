using BepInEx.Configuration;
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

public sealed class FixedConfigValue<T>
{
    public FixedConfigValue(T value)
    {
        Value = value;
    }

    public T Value { get; }
}

public sealed class ModConfig
{
    private const string CoreSection = "01 Core - 核心启动";
    private const string StereoSection = "02 Stereo Rendering - 立体渲染";
    private const string InputSection = "03 Input - 输入";
    private const string BodySection = "04 Body And View - 身体与视角";
    private const string VrUiSection = "05 VR UI Panel - VR界面面板";
    private const string NpcUiSection = "06 NPC UI - NPC标记界面";

    private static FixedConfigValue<T> Fixed<T>(T value)
    {
        return new FixedConfigValue<T>(value);
    }

    private ModConfig(ConfigFile config)
    {
        EnableVR = config.Bind(
            CoreSection,
            nameof(EnableVR),
            true,
            "Enable the SteamVR/OpenVR runtime. If initialization fails, the plugin logs the error and leaves the normal game running.");

        AutoStartSteamVR = config.Bind(
            CoreSection,
            nameof(AutoStartSteamVR),
            true,
            "Allow the runtime layer to ask OpenVR to start SteamVR if it is not already running.");

        IPDScale = Fixed(1.0f);
        WorldScale = Fixed(1.0f);

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

        AutoRecenterOnStart = Fixed(true);
        SuppressMouseLookInput = Fixed(true);

        EnableQuest3InputMapping = config.Bind(
            InputSection,
            nameof(EnableQuest3InputMapping),
            true,
            "Enable Quest 3 controller mapping through SteamVR/OpenVR input. Disable to leave the game's original input untouched.");

        Quest3InputActionManifestPath = Fixed(string.Empty);
        Quest3LongPressSeconds = Fixed(0.5f);

        Quest3RightStickDeadzone = config.Bind(
            InputSection,
            nameof(Quest3RightStickDeadzone),
            0.35f,
            new ConfigDescription(
                "Deadzone for cursor-mode right-stick Q/E/scroll mapping.",
                new AcceptableValueRange<float>(0.0f, 0.95f)));

        Quest3RightStickDiagonalGuardDegrees = Fixed(15.0f);
        Quest3TriggerPressThreshold = Fixed(0.35f);
        Quest3CursorRayDirection = Fixed(SecretFlasherManakaVR.Quest3CursorRayDirection.Forward);
        Quest3CursorRayLength = Fixed(6.0f);
        Quest3CursorRayPitchOffsetDegrees = Fixed(0.0f);
        Quest3CursorRayYawOffsetDegrees = Fixed(0.0f);
        Quest3CursorRayRollOffsetDegrees = Fixed(0.0f);

        EnablePlayerHeadPoseControl = config.Bind(
            BodySection,
            nameof(EnablePlayerHeadPoseControl),
            true,
            "While VR is active and the game camera is attached to the first-person player camera target, add HMD-relative rotation to the player head/neck/chest bones.");

        PlayerHeadPoseFollowDistance = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseFollowDistance),
            0.75f,
            new ConfigDescription(
                "Maximum distance from the source camera to the player's first-person camera target for HMD head pose control to activate. GameOver detached cameras should exceed this.",
                new AcceptableValueRange<float>(0.05f, 3.0f)));

        PlayerHeadPoseYawLimitDegrees = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseYawLimitDegrees),
            70.0f,
            new ConfigDescription(
                "Maximum left/right HMD yaw applied to the player body rig.",
                new AcceptableValueRange<float>(0.0f, 120.0f)));

        PlayerHeadPosePitchLimitDegrees = config.Bind(
            BodySection,
            nameof(PlayerHeadPosePitchLimitDegrees),
            55.0f,
            new ConfigDescription(
                "Maximum up/down HMD pitch applied to the player body rig.",
                new AcceptableValueRange<float>(0.0f, 90.0f)));

        PlayerHeadPoseRollLimitDegrees = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseRollLimitDegrees),
            20.0f,
            new ConfigDescription(
                "Maximum HMD roll applied to the player head. Keep this small to avoid exaggerated sideways head tilt.",
                new AcceptableValueRange<float>(0.0f, 45.0f)));

        PlayerHeadPoseYawTurnsBodyWhileMoving = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseYawTurnsBodyWhileMoving),
            true,
            "When enabled, HMD yaw keeps driving head bones while standing still, but turns the player's body toward the HMD facing direction while the game reports actual player movement.");

        PlayerHeadPoseBodyYawTurnSpeedDegreesPerSecond = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseBodyYawTurnSpeedDegreesPerSecond),
            180.0f,
            new ConfigDescription(
                "Maximum degrees per second used when HMD yaw turns the player body while moving. The consumed HMD yaw is matched to the actual interpolated body yaw step.",
                new AcceptableValueRange<float>(0.0f, 1080.0f)));

        IgnoreHeadPositionForVrCamera = config.Bind(
            BodySection,
            nameof(IgnoreHeadPositionForVrCamera),
            true,
            "When enabled, HMD positional movement is clamped before it is added to the VR camera. HMD rotation, IPD, and player head bone control still apply.");

        HeadPositionCameraOffsetMinX = config.Bind(
            BodySection,
            nameof(HeadPositionCameraOffsetMinX),
            -1.0f,
            new ConfigDescription(
                "Minimum local X HMD positional offset applied to the VR camera when IgnoreHeadPositionForVrCamera is enabled.",
                new AcceptableValueRange<float>(-10.0f, 10.0f)));

        HeadPositionCameraOffsetMaxX = config.Bind(
            BodySection,
            nameof(HeadPositionCameraOffsetMaxX),
            1.0f,
            new ConfigDescription(
                "Maximum local X HMD positional offset applied to the VR camera when IgnoreHeadPositionForVrCamera is enabled.",
                new AcceptableValueRange<float>(-10.0f, 10.0f)));

        HeadPositionCameraOffsetMinY = config.Bind(
            BodySection,
            nameof(HeadPositionCameraOffsetMinY),
            -1.0f,
            new ConfigDescription(
                "Minimum local Y HMD positional offset applied to the VR camera when IgnoreHeadPositionForVrCamera is enabled.",
                new AcceptableValueRange<float>(-10.0f, 10.0f)));

        HeadPositionCameraOffsetMaxY = config.Bind(
            BodySection,
            nameof(HeadPositionCameraOffsetMaxY),
            1.0f,
            new ConfigDescription(
                "Maximum local Y HMD positional offset applied to the VR camera when IgnoreHeadPositionForVrCamera is enabled.",
                new AcceptableValueRange<float>(-10.0f, 10.0f)));

        HeadPositionCameraOffsetMinZ = config.Bind(
            BodySection,
            nameof(HeadPositionCameraOffsetMinZ),
            -1.0f,
            new ConfigDescription(
                "Minimum local Z HMD positional offset applied to the VR camera when IgnoreHeadPositionForVrCamera is enabled.",
                new AcceptableValueRange<float>(-10.0f, 10.0f)));

        HeadPositionCameraOffsetMaxZ = config.Bind(
            BodySection,
            nameof(HeadPositionCameraOffsetMaxZ),
            1.0f,
            new ConfigDescription(
                "Maximum local Z HMD positional offset applied to the VR camera when IgnoreHeadPositionForVrCamera is enabled.",
                new AcceptableValueRange<float>(-10.0f, 10.0f)));

        PlayerHeadPoseChestWeight = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseChestWeight),
            0.15f,
            new ConfigDescription(
                "Fraction of the limited HMD rotation added to the chest.",
                new AcceptableValueRange<float>(0.0f, 1.0f)));

        PlayerHeadPoseNeckWeight = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseNeckWeight),
            0.30f,
            new ConfigDescription(
                "Fraction of the limited HMD rotation added to the neck.",
                new AcceptableValueRange<float>(0.0f, 1.0f)));

        PlayerHeadPoseHeadWeight = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseHeadWeight),
            0.55f,
            new ConfigDescription(
                "Fraction of the limited HMD rotation added to the head.",
                new AcceptableValueRange<float>(0.0f, 1.0f)));

        PlayerHeadPoseSmoothFactor = config.Bind(
            BodySection,
            nameof(PlayerHeadPoseSmoothFactor),
            18.0f,
            new ConfigDescription(
                "Smoothing speed for HMD-to-body head rotation. Higher values follow faster.",
                new AcceptableValueRange<float>(0.0f, 60.0f)));

        SourceRotationMode = Fixed(VrSourceRotationMode.SourceYawOnly);

        MirrorMode = config.Bind(
            CoreSection,
            nameof(MirrorMode),
            SecretFlasherManakaVR.MirrorMode.MainCamera,
            "Controls what the normal desktop window shows while VR output is active.");

        SceneTransitionVrPauseSeconds = Fixed(1.5f);

        RenderScale = config.Bind(
            StereoSection,
            nameof(RenderScale),
            0.5f,
            new ConfigDescription(
                "Scale applied to the OpenVR recommended eye texture size. Lower values are safer while stabilizing the injected renderer.",
                new AcceptableValueRange<float>(0.25f, 1.5f)));

        UseOpenVRProjection = Fixed(true);
        OpenVRProjectionMode = Fixed(SecretFlasherManakaVR.OpenVR.OpenVRProjectionMode.RawSwapVertical);
        UseSourceProjectionForCulling = Fixed(true);
        FlipSubmitV = Fixed(true);
        SuppressBlackCensorOnChangeNullRefs = Fixed(true);
        DisableSourceCameraRendering = Fixed(false);
        DisableMirrorManagersWhileVrActive = Fixed(true);
        DisableTargetTextureCameras = Fixed(true);
        PreventReflectionReenableWhileVrActive = Fixed(true);
        BlockReflectionCameraRenderWhileVrActive = Fixed(true);
        BlockNestedCameraRenderDuringVrRender = Fixed(true);
        DisableReflectionProbes = Fixed(false);
        ReflectionCameraNameKeywords = Fixed("mirror,reflect,reflection,planar,water");

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

        VrUiVerticalOffset = config.Bind(
            VrUiSection,
            nameof(VrUiVerticalOffset),
            -0.1f,
            new ConfigDescription(
                "Vertical offset in headset/source-camera local space for converted UI.",
                new AcceptableValueRange<float>(-2.0f, 2.0f)));

        VrUiPanelScale = config.Bind(
            VrUiSection,
            nameof(VrUiPanelScale),
            1.05f,
            new ConfigDescription(
                "Additional multiplier for the captured VR UI panel size.",
                new AcceptableValueRange<float>(0.25f, 3.0f)));

        VrUiPanelPixelOffsetY = config.Bind(
            VrUiSection,
            nameof(VrUiPanelPixelOffsetY),
            -100.0f,
            new ConfigDescription(
                "Vertical panel offset in captured UI pixels. Negative values move the HUD downward.",
                new AcceptableValueRange<float>(-2160.0f, 2160.0f)));

        VrUiStatusInfoOffsetX = config.Bind(
            VrUiSection,
            nameof(VrUiStatusInfoOffsetX),
            -150.0f,
            new ConfigDescription(
                "Horizontal pixel offset for InGameCanvas/MiddleLayer/Right/StatusInfo in the captured VR HUD.",
                new AcceptableValueRange<float>(-4096.0f, 4096.0f)));

        VrUiStatusInfoOffsetY = config.Bind(
            VrUiSection,
            nameof(VrUiStatusInfoOffsetY),
            0.0f,
            new ConfigDescription(
                "Vertical pixel offset for InGameCanvas/MiddleLayer/Right/StatusInfo in the captured VR HUD.",
                new AcceptableValueRange<float>(-4096.0f, 4096.0f)));

        EnableVrFullscreenEffectLayer = config.Bind(
            VrUiSection,
            nameof(EnableVrFullscreenEffectLayer),
            true,
            "Render InGameCanvas fullscreen vignette/effect elements into a separate VR overlay texture.");

        VrFullscreenEffectPanelScale = config.Bind(
            VrUiSection,
            nameof(VrFullscreenEffectPanelScale),
            1.08f,
            new ConfigDescription(
                "Additional multiplier for the separated fullscreen effect overlay size.",
                new AcceptableValueRange<float>(0.25f, 3.0f)));

        VrFullscreenEffectCurveDegrees = config.Bind(
            VrUiSection,
            nameof(VrFullscreenEffectCurveDegrees),
            36.0f,
            new ConfigDescription(
                "Horizontal inward curvature, in degrees, for the separated fullscreen effect overlay.",
                new AcceptableValueRange<float>(0.0f, 120.0f)));

        VrFullscreenEffectDepthOffset = config.Bind(
            VrUiSection,
            nameof(VrFullscreenEffectDepthOffset),
            0.01f,
            new ConfigDescription(
                "Local depth offset for the fullscreen effect overlay. Positive values move it slightly toward the headset.",
                new AcceptableValueRange<float>(-0.25f, 0.25f)));

        VrUiFaceRtOffsetX = config.Bind(
            VrUiSection,
            nameof(VrUiFaceRtOffsetX),
            0.0f,
            new ConfigDescription(
                "Horizontal pixel offset for the HUD face RenderTexture image.",
                new AcceptableValueRange<float>(-4096.0f, 4096.0f)));

        VrUiFaceRtOffsetY = config.Bind(
            VrUiSection,
            nameof(VrUiFaceRtOffsetY),
            0.0f,
            new ConfigDescription(
                "Vertical pixel offset for the HUD face RenderTexture image.",
                new AcceptableValueRange<float>(-4096.0f, 4096.0f)));

        VrUiFaceRtScale = config.Bind(
            VrUiSection,
            nameof(VrUiFaceRtScale),
            1.0f,
            new ConfigDescription(
                "Scale multiplier for the HUD face RenderTexture image.",
                new AcceptableValueRange<float>(0.1f, 5.0f)));

        VrUiBodyRtOffsetX = config.Bind(
            VrUiSection,
            nameof(VrUiBodyRtOffsetX),
            0.0f,
            new ConfigDescription(
                "Horizontal pixel offset for the HUD body RenderTexture image.",
                new AcceptableValueRange<float>(-4096.0f, 4096.0f)));

        VrUiBodyRtOffsetY = config.Bind(
            VrUiSection,
            nameof(VrUiBodyRtOffsetY),
            0.0f,
            new ConfigDescription(
                "Vertical pixel offset for the HUD body RenderTexture image.",
                new AcceptableValueRange<float>(-4096.0f, 4096.0f)));

        VrUiBodyRtScale = config.Bind(
            VrUiSection,
            nameof(VrUiBodyRtScale),
            1.0f,
            new ConfigDescription(
                "Scale multiplier for the HUD body RenderTexture image.",
                new AcceptableValueRange<float>(0.1f, 5.0f)));

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
            "Optional comma-separated canvas name keywords. Leave empty for auto mode: capture InGameCanvas when present, otherwise capture title/menu canvases.");

        VrUiCanvasNameBlacklist = config.Bind(
            VrUiSection,
            nameof(VrUiCanvasNameBlacklist),
            string.Empty,
            "Optional comma-separated canvas name keywords. Matching canvases are not converted.");

        FixNpcWorldSpaceUi = config.Bind(
            NpcUiSection,
            nameof(FixNpcWorldSpaceUi),
            true,
            "Experimental. While VR is active, reproject NPC head markers with the HMD pose before the HUD is captured.");

        NpcWorldSpaceUiVerticalOffset = config.Bind(
            NpcUiSection,
            nameof(NpcWorldSpaceUiVerticalOffset),
            0.25f,
            new ConfigDescription(
                "Additional world-space height above the NPC head for the NPC marker VR fix.",
                new AcceptableValueRange<float>(-1.0f, 2.0f)));

        NpcWorldSpaceUiScale = config.Bind(
            NpcUiSection,
            nameof(NpcWorldSpaceUiScale),
            0.0015f,
            new ConfigDescription(
                "World-space scale for NPC circular/question UI when the VR NPC UI fix is enabled.",
                new AcceptableValueRange<float>(0.0002f, 0.02f)));

        NpcWorldSpaceUiMinScaleDistance = config.Bind(
            NpcUiSection,
            nameof(NpcWorldSpaceUiMinScaleDistance),
            3.0f,
            new ConfigDescription(
                "Closest distance used for NPC world-space UI scale compensation.",
                new AcceptableValueRange<float>(0.25f, 50.0f)));

        NpcWorldSpaceUiMaxScaleDistance = config.Bind(
            NpcUiSection,
            nameof(NpcWorldSpaceUiMaxScaleDistance),
            7.0f,
            new ConfigDescription(
                "Farthest distance used for NPC world-space UI scale compensation.",
                new AcceptableValueRange<float>(0.25f, 100.0f)));

    }

    public ConfigEntry<bool> EnableVR { get; }
    public ConfigEntry<bool> AutoStartSteamVR { get; }
    public FixedConfigValue<float> IPDScale { get; }
    public FixedConfigValue<float> WorldScale { get; }
    public ConfigEntry<float> CameraHeightOffset { get; }
    public ConfigEntry<KeyCode> RecenteringKey { get; }
    public FixedConfigValue<bool> AutoRecenterOnStart { get; }
    public FixedConfigValue<bool> SuppressMouseLookInput { get; }
    public ConfigEntry<bool> EnableQuest3InputMapping { get; }
    public FixedConfigValue<string> Quest3InputActionManifestPath { get; }
    public FixedConfigValue<float> Quest3LongPressSeconds { get; }
    public ConfigEntry<float> Quest3RightStickDeadzone { get; }
    public FixedConfigValue<float> Quest3RightStickDiagonalGuardDegrees { get; }
    public FixedConfigValue<float> Quest3TriggerPressThreshold { get; }
    public FixedConfigValue<Quest3CursorRayDirection> Quest3CursorRayDirection { get; }
    public FixedConfigValue<float> Quest3CursorRayLength { get; }
    public FixedConfigValue<float> Quest3CursorRayPitchOffsetDegrees { get; }
    public FixedConfigValue<float> Quest3CursorRayYawOffsetDegrees { get; }
    public FixedConfigValue<float> Quest3CursorRayRollOffsetDegrees { get; }
    public ConfigEntry<bool> EnablePlayerHeadPoseControl { get; }
    public ConfigEntry<float> PlayerHeadPoseFollowDistance { get; }
    public ConfigEntry<float> PlayerHeadPoseYawLimitDegrees { get; }
    public ConfigEntry<float> PlayerHeadPosePitchLimitDegrees { get; }
    public ConfigEntry<float> PlayerHeadPoseRollLimitDegrees { get; }
    public ConfigEntry<bool> PlayerHeadPoseYawTurnsBodyWhileMoving { get; }
    public ConfigEntry<float> PlayerHeadPoseBodyYawTurnSpeedDegreesPerSecond { get; }
    public ConfigEntry<bool> IgnoreHeadPositionForVrCamera { get; }
    public ConfigEntry<float> HeadPositionCameraOffsetMinX { get; }
    public ConfigEntry<float> HeadPositionCameraOffsetMaxX { get; }
    public ConfigEntry<float> HeadPositionCameraOffsetMinY { get; }
    public ConfigEntry<float> HeadPositionCameraOffsetMaxY { get; }
    public ConfigEntry<float> HeadPositionCameraOffsetMinZ { get; }
    public ConfigEntry<float> HeadPositionCameraOffsetMaxZ { get; }
    public ConfigEntry<float> PlayerHeadPoseChestWeight { get; }
    public ConfigEntry<float> PlayerHeadPoseNeckWeight { get; }
    public ConfigEntry<float> PlayerHeadPoseHeadWeight { get; }
    public ConfigEntry<float> PlayerHeadPoseSmoothFactor { get; }
    public FixedConfigValue<VrSourceRotationMode> SourceRotationMode { get; }
    public ConfigEntry<MirrorMode> MirrorMode { get; }
    public FixedConfigValue<float> SceneTransitionVrPauseSeconds { get; }
    public ConfigEntry<float> RenderScale { get; }
    public FixedConfigValue<bool> UseOpenVRProjection { get; }
    public FixedConfigValue<OpenVRProjectionMode> OpenVRProjectionMode { get; }
    public FixedConfigValue<bool> UseSourceProjectionForCulling { get; }
    public FixedConfigValue<bool> FlipSubmitV { get; }
    public FixedConfigValue<bool> SuppressBlackCensorOnChangeNullRefs { get; }
    public FixedConfigValue<bool> DisableSourceCameraRendering { get; }
    public FixedConfigValue<bool> DisableMirrorManagersWhileVrActive { get; }
    public FixedConfigValue<bool> DisableTargetTextureCameras { get; }
    public FixedConfigValue<bool> PreventReflectionReenableWhileVrActive { get; }
    public FixedConfigValue<bool> BlockReflectionCameraRenderWhileVrActive { get; }
    public FixedConfigValue<bool> BlockNestedCameraRenderDuringVrRender { get; }
    public FixedConfigValue<bool> DisableReflectionProbes { get; }
    public FixedConfigValue<string> ReflectionCameraNameKeywords { get; }
    public ConfigEntry<bool> EnableVrUiBridge { get; }
    public ConfigEntry<bool> ConvertOverlayCanvasToWorldSpace { get; }
    public ConfigEntry<VrUiFollowMode> VrUiFollowMode { get; }
    public ConfigEntry<float> VrUiDistance { get; }
    public ConfigEntry<float> VrUiVerticalOffset { get; }
    public ConfigEntry<float> VrUiPanelScale { get; }
    public ConfigEntry<float> VrUiPanelPixelOffsetY { get; }
    public ConfigEntry<float> VrUiStatusInfoOffsetX { get; }
    public ConfigEntry<float> VrUiStatusInfoOffsetY { get; }
    public ConfigEntry<bool> EnableVrFullscreenEffectLayer { get; }
    public ConfigEntry<float> VrFullscreenEffectPanelScale { get; }
    public ConfigEntry<float> VrFullscreenEffectCurveDegrees { get; }
    public ConfigEntry<float> VrFullscreenEffectDepthOffset { get; }
    public ConfigEntry<float> VrUiFaceRtOffsetX { get; }
    public ConfigEntry<float> VrUiFaceRtOffsetY { get; }
    public ConfigEntry<float> VrUiFaceRtScale { get; }
    public ConfigEntry<float> VrUiBodyRtOffsetX { get; }
    public ConfigEntry<float> VrUiBodyRtOffsetY { get; }
    public ConfigEntry<float> VrUiBodyRtScale { get; }
    public ConfigEntry<float> VrUiMaxScanInterval { get; }
    public ConfigEntry<string> VrUiCanvasNameWhitelist { get; }
    public ConfigEntry<string> VrUiCanvasNameBlacklist { get; }
    public ConfigEntry<bool> FixNpcWorldSpaceUi { get; }
    public ConfigEntry<float> NpcWorldSpaceUiVerticalOffset { get; }
    public ConfigEntry<float> NpcWorldSpaceUiScale { get; }
    public ConfigEntry<float> NpcWorldSpaceUiMinScaleDistance { get; }
    public ConfigEntry<float> NpcWorldSpaceUiMaxScaleDistance { get; }

    public static ModConfig Bind(ConfigFile config)
    {
        return new ModConfig(config);
    }
}
