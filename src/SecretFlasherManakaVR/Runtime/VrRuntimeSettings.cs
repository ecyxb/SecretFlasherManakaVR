using UnityEngine;
using SecretFlasherManakaVR.OpenVR;

namespace SecretFlasherManakaVR.Runtime
{
    public enum VrMirrorMode
    {
        SourceCamera = 0,
        Disabled = 1,
        LeftEye = 2,
        RightEye = 3
    }

    public enum VrSourceRotationMode
    {
        FullSourceCamera = 0,
        SourceYawOnly = 1,
        RecenterYaw = 2,
        HmdOnly = 3
    }

    public enum VrUiFollowMode
    {
        HeadLocked = 0,
        SourceCameraLocked = 1,
        WorldFixed = 2
    }

    public sealed class VrRuntimeSettings
    {
        public bool EnableVR = true;
        public bool AutoStartSteamVR = true;
        public float IPDScale = 1.0f;
        public float WorldScale = 1.0f;
        public float CameraHeightOffset = 0.0f;
        public KeyCode RecenteringKey = KeyCode.F12;
        public bool AutoRecenterOnStart = true;
        public bool SuppressMouseLookInput = true;
        public bool IgnoreHeadPositionForVrCamera = false;
        public float HeadPositionCameraOffsetMinX = -1.0f;
        public float HeadPositionCameraOffsetMaxX = 1.0f;
        public float HeadPositionCameraOffsetMinY = -1.0f;
        public float HeadPositionCameraOffsetMaxY = 1.0f;
        public float HeadPositionCameraOffsetMinZ = -1.0f;
        public float HeadPositionCameraOffsetMaxZ = 1.0f;
        public VrSourceRotationMode SourceRotationMode = VrSourceRotationMode.SourceYawOnly;
        public VrMirrorMode MirrorMode = VrMirrorMode.SourceCamera;
        public float SceneTransitionVrPauseSeconds = 1.5f;
        public bool UseOpenVRProjection = true;
        public OpenVRProjectionMode OpenVRProjectionMode = OpenVRProjectionMode.RawSwapVertical;
        public bool UseSourceProjectionForCulling = true;
        public bool FlipSubmitV = true;
        public bool DisableReflectionCameras = true;
        public bool DisableSourceCameraRendering = false;
        public bool DisableMirrorManagersWhileVrActive = false;
        public bool DisableTargetTextureCameras = true;
        public bool KeepReflectionCamerasDisabledWhileVrActive = true;
        public bool PreventReflectionReenableWhileVrActive = true;
        public bool BlockReflectionCameraRenderWhileVrActive = true;
        public bool BlockNestedCameraRenderDuringVrRender = true;
        public bool DisableReflectionProbes = false;
        public string ReflectionCameraNameKeywords = "mirror,reflect,reflection,planar,water";
        public bool EnableVrUiBridge = true;
        public bool ConvertOverlayCanvasToWorldSpace = true;
        public VrUiFollowMode VrUiFollowMode = VrUiFollowMode.HeadLocked;
        public float VrUiDistance = 1.4f;
        public float VrUiScale = 0.001f;
        public float VrUiVerticalOffset = -0.1f;
        public float VrUiPanelScale = 1.05f;
        public float VrUiPanelPixelOffsetY = -100.0f;
        public float VrUiStatusInfoOffsetX = -150.0f;
        public float VrUiStatusInfoOffsetY = 0.0f;
        public bool EnableVrFullscreenEffectLayer = true;
        public float VrFullscreenEffectPanelScale = 1.08f;
        public float VrFullscreenEffectCurveDegrees = 36.0f;
        public float VrFullscreenEffectDepthOffset = 0.01f;
        public float VrUiFaceRtOffsetX = 0.0f;
        public float VrUiFaceRtOffsetY = 0.0f;
        public float VrUiFaceRtScale = 1.0f;
        public float VrUiBodyRtOffsetX = 0.0f;
        public float VrUiBodyRtOffsetY = 0.0f;
        public float VrUiBodyRtScale = 1.0f;
        public float VrUiMaxScanInterval = 1.0f;
        public string VrUiCanvasNameWhitelist = string.Empty;
        public string VrUiCanvasNameBlacklist = string.Empty;
        public bool FixNpcWorldSpaceUi = false;
        public float NpcWorldSpaceUiVerticalOffset = 0.25f;
        public float NpcWorldSpaceUiScale = 0.0015f;
        public float NpcWorldSpaceUiMinScaleDistance = 3.0f;
        public float NpcWorldSpaceUiMaxScaleDistance = 7.0f;

        public float RenderScale = 1.0f;
        public int FallbackRenderWidth = 1512;
        public int FallbackRenderHeight = 1680;
        public int AntiAliasing = 1;
        public float MissingCameraRetrySeconds = 1.0f;
        public float RenderTargetCheckSeconds = 2.0f;

        public void Sanitize()
        {
            IPDScale = Mathf.Clamp(IPDScale, 0.1f, 4.0f);
            WorldScale = Mathf.Clamp(WorldScale, 0.01f, 100.0f);
            RenderScale = Mathf.Clamp(RenderScale, 0.25f, 2.0f);
            SceneTransitionVrPauseSeconds = Mathf.Clamp(SceneTransitionVrPauseSeconds, 0.0f, 10.0f);
            FallbackRenderWidth = Mathf.Clamp(FallbackRenderWidth, 256, 8192);
            FallbackRenderHeight = Mathf.Clamp(FallbackRenderHeight, 256, 8192);
            AntiAliasing = Mathf.Clamp(AntiAliasing, 1, 8);
            MissingCameraRetrySeconds = Mathf.Clamp(MissingCameraRetrySeconds, 0.1f, 10.0f);
            RenderTargetCheckSeconds = Mathf.Clamp(RenderTargetCheckSeconds, 0.25f, 30.0f);
            SanitizeRange(ref HeadPositionCameraOffsetMinX, ref HeadPositionCameraOffsetMaxX);
            SanitizeRange(ref HeadPositionCameraOffsetMinY, ref HeadPositionCameraOffsetMaxY);
            SanitizeRange(ref HeadPositionCameraOffsetMinZ, ref HeadPositionCameraOffsetMaxZ);
            VrUiDistance = Mathf.Clamp(VrUiDistance, 0.25f, 5.0f);
            VrUiScale = Mathf.Clamp(VrUiScale, 0.0001f, 0.02f);
            VrUiVerticalOffset = Mathf.Clamp(VrUiVerticalOffset, -2.0f, 2.0f);
            VrUiPanelScale = Mathf.Clamp(VrUiPanelScale, 0.25f, 3.0f);
            VrUiPanelPixelOffsetY = Mathf.Clamp(VrUiPanelPixelOffsetY, -2160.0f, 2160.0f);
            VrUiStatusInfoOffsetX = Mathf.Clamp(VrUiStatusInfoOffsetX, -4096.0f, 4096.0f);
            VrUiStatusInfoOffsetY = Mathf.Clamp(VrUiStatusInfoOffsetY, -4096.0f, 4096.0f);
            VrFullscreenEffectPanelScale = Mathf.Clamp(VrFullscreenEffectPanelScale, 0.25f, 3.0f);
            VrFullscreenEffectCurveDegrees = Mathf.Clamp(VrFullscreenEffectCurveDegrees, 0.0f, 120.0f);
            VrFullscreenEffectDepthOffset = Mathf.Clamp(VrFullscreenEffectDepthOffset, -0.25f, 0.25f);
            VrUiFaceRtOffsetX = Mathf.Clamp(VrUiFaceRtOffsetX, -4096.0f, 4096.0f);
            VrUiFaceRtOffsetY = Mathf.Clamp(VrUiFaceRtOffsetY, -4096.0f, 4096.0f);
            VrUiFaceRtScale = Mathf.Clamp(VrUiFaceRtScale, 0.1f, 5.0f);
            VrUiBodyRtOffsetX = Mathf.Clamp(VrUiBodyRtOffsetX, -4096.0f, 4096.0f);
            VrUiBodyRtOffsetY = Mathf.Clamp(VrUiBodyRtOffsetY, -4096.0f, 4096.0f);
            VrUiBodyRtScale = Mathf.Clamp(VrUiBodyRtScale, 0.1f, 5.0f);
            VrUiMaxScanInterval = Mathf.Clamp(VrUiMaxScanInterval, 0.25f, 10.0f);
            NpcWorldSpaceUiVerticalOffset = Mathf.Clamp(NpcWorldSpaceUiVerticalOffset, -1.0f, 2.0f);
            NpcWorldSpaceUiScale = Mathf.Clamp(NpcWorldSpaceUiScale, 0.0002f, 0.02f);
            NpcWorldSpaceUiMinScaleDistance = Mathf.Clamp(NpcWorldSpaceUiMinScaleDistance, 0.25f, 50.0f);
            NpcWorldSpaceUiMaxScaleDistance = Mathf.Clamp(NpcWorldSpaceUiMaxScaleDistance, NpcWorldSpaceUiMinScaleDistance, 100.0f);
            if (string.IsNullOrWhiteSpace(ReflectionCameraNameKeywords))
            {
                ReflectionCameraNameKeywords = "mirror,reflect,reflection,planar,water";
            }
        }

        private static void SanitizeRange(ref float minimum, ref float maximum)
        {
            minimum = Mathf.Clamp(minimum, -10.0f, 10.0f);
            maximum = Mathf.Clamp(maximum, -10.0f, 10.0f);
            if (minimum <= maximum)
            {
                return;
            }

            float swap = minimum;
            minimum = maximum;
            maximum = swap;
        }

        public VrRuntimeSettings Clone()
        {
            return (VrRuntimeSettings)MemberwiseClone();
        }
    }
}
