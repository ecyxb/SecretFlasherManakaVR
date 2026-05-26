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
        public VrSourceRotationMode SourceRotationMode = VrSourceRotationMode.SourceYawOnly;
        public VrMirrorMode MirrorMode = VrMirrorMode.SourceCamera;
        public bool LogPoseDebug = false;
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
        public bool BlockReflectionProbeRenderWhileVrActive = false;
        public bool LogReflectionProbeDiagnostics = true;
        public bool LogReflectionCameraDiagnostics = true;
        public string ReflectionCameraNameKeywords = "mirror,reflect,reflection,planar,water";

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
            if (string.IsNullOrWhiteSpace(ReflectionCameraNameKeywords))
            {
                ReflectionCameraNameKeywords = "mirror,reflect,reflection,planar,water";
            }
        }

        public VrRuntimeSettings Clone()
        {
            return (VrRuntimeSettings)MemberwiseClone();
        }
    }
}
