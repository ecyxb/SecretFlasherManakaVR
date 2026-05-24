using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SecretFlasherManakaVR.OpenVR;

namespace SecretFlasherManakaVR.Runtime
{
    public sealed class VrRuntimeManager
    {
        private const float DefaultIpdMeters = 0.064f;
        private const float OpenVRRetrySeconds = 5.0f;

        private readonly RateLimitedLogger limitedLog;
        private readonly IVrRuntimeLogger logger;

        private VrRuntimeSettings settings;
        private IOpenVRBridge bridge;
        private VrCameraRig rig;
        private Camera sourceCamera;
        private RuntimePose lastPose;
        private Quaternion recenterYaw = Quaternion.identity;
        private Vector3 recenterPosition = Vector3.zero;
        private Quaternion recenterSourceYaw = Quaternion.identity;
        private bool recenterSet;
        private bool initialized;
        private bool vrReady;
        private float nextOpenVRRetryTime;
        private float nextCameraSearchTime;
        private float nextRenderTargetCheckTime;
        private int renderWidth;
        private int renderHeight;
        private int lastSceneHandle = -1;
        private float sceneTransitionPauseUntil;
        private bool submitSuccessLogged;
        private bool projectionModeLogged;
        private bool reflectionCameraDiagnosticsLogged;
        private bool sceneTransitionPauseLogged;
        private string[] reflectionCameraKeywords = Array.Empty<string>();
        private readonly List<DisabledCameraState> disabledReflectionCameras = new List<DisabledCameraState>();
        private readonly List<DisabledCameraState> persistentlyDisabledReflectionCameras = new List<DisabledCameraState>();

        public VrRuntimeManager()
            : this(NullVrRuntimeLogger.Instance)
        {
        }

        public VrRuntimeManager(IVrRuntimeLogger logger)
        {
            this.logger = logger ?? NullVrRuntimeLogger.Instance;
            limitedLog = new RateLimitedLogger(this.logger, 5.0f);
            settings = new VrRuntimeSettings();
            lastPose = RuntimePose.Identity;
        }

        public bool IsInitialized
        {
            get { return initialized; }
        }

        public bool IsVrReady
        {
            get { return vrReady; }
        }

        public Camera SourceCamera
        {
            get { return sourceCamera; }
        }

        public RenderTexture LeftEyeTexture
        {
            get { return rig == null ? null : rig.LeftTexture; }
        }

        public RenderTexture RightEyeTexture
        {
            get { return rig == null ? null : rig.RightTexture; }
        }

        public void Initialize(IOpenVRBridge openVrBridge, VrRuntimeSettings runtimeSettings)
        {
            Shutdown();

            settings = runtimeSettings == null ? new VrRuntimeSettings() : runtimeSettings.Clone();
            settings.Sanitize();
            reflectionCameraKeywords = Array.Empty<string>();
            reflectionCameraDiagnosticsLogged = false;
            initialized = true;
            vrReady = false;

            if (!settings.EnableVR)
            {
                logger.Info("VR runtime disabled by config.");
                return;
            }

            if (openVrBridge == null)
            {
                logger.Warning("VR runtime disabled: OpenVR bridge was not provided.");
                return;
            }

            bridge = openVrBridge;

            TryInitializeOpenVR(true);
        }

        public void Tick()
        {
            if (!vrReady)
            {
                if (initialized && settings.EnableVR && bridge != null && Time.unscaledTime >= nextOpenVRRetryTime)
                {
                    TryInitializeOpenVR(false);
                }

                return;
            }

            HandleSceneChange();
            DisableReflectionCamerasEarly();

            if (settings.RecenteringKey != KeyCode.None && Input.GetKeyDown(settings.RecenteringKey))
            {
                Recenter();
            }
        }

        public void LateTick()
        {
            if (!vrReady)
            {
                return;
            }

            if (bridge == null || !bridge.IsInitialized)
            {
                limitedLog.Warning("openvr-lost", "OpenVR bridge is not ready; skipping VR frame.");
                return;
            }

            HandleSceneChange();
            if (IsInSceneTransitionPause())
            {
                DisableReflectionCamerasEarly();
                if (!sceneTransitionPauseLogged)
                {
                    sceneTransitionPauseLogged = true;
                    logger.Info("Pausing VR eye rendering during scene transition for " + settings.SceneTransitionVrPauseSeconds.ToString("0.00") + " seconds.");
                }

                return;
            }

            if (!FindSourceCamera(false))
            {
                return;
            }

            RefreshRenderTargetSize(false);
            bridge.BeginFrame();

            if (!TryGetHmdPose(out lastPose))
            {
                limitedLog.Warning("pose-missing", "OpenVR pose unavailable; rendering with last valid pose.");
            }

            if (!lastPose.IsValid)
            {
                lastPose = RuntimePose.Identity;
            }
            else if (settings.AutoRecenterOnStart && !recenterSet)
            {
                ApplyRecenter(lastPose, "VR view auto-recentered at first valid HMD pose.");
            }

            rig.EnsureCreated();
            rig.EnsureRenderTextures(renderWidth, renderHeight, settings.AntiAliasing);
            DisableReflectionCamerasEarly();
            rig.CopyFromSource(sourceCamera);
            ApplyPoseToRig();
            ApplyProjectionOrCameraFallback();
            bool restoreFrameDisabledCameras = DisableReflectionCamerasBeforeVrRender();
            try
            {
                rig.Render();
            }
            finally
            {
                if (restoreFrameDisabledCameras)
                {
                    RestoreReflectionCameras();
                }
            }

            bool leftSubmitted = SubmitEye(RuntimeEye.Left, rig.LeftSubmitTexturePtr, rig.LeftSubmitTextureType, out var leftError);
            bool rightSubmitted = SubmitEye(RuntimeEye.Right, rig.RightSubmitTexturePtr, rig.RightSubmitTextureType, out var rightError);
            if (!leftSubmitted || !rightSubmitted)
            {
                string suffix = " Left=" + SubmitStatus(leftSubmitted, leftError, rig.LeftSubmitTexturePtr) +
                    " Right=" + SubmitStatus(rightSubmitted, rightError, rig.RightSubmitTexturePtr);
                limitedLog.Warning("submit-failed", "OpenVR texture submit failed; normal game window remains playable." + suffix);
            }
            else if (!submitSuccessLogged)
            {
                submitSuccessLogged = true;
                logger.Info("OpenVR texture submit succeeded for both eyes.");
            }

            if (settings.MirrorMode == VrMirrorMode.LeftEye || settings.MirrorMode == VrMirrorMode.RightEye)
            {
                rig.Mirror(settings.MirrorMode);
            }

            if (settings.LogPoseDebug)
            {
                limitedLog.Info("pose-debug", "HMD pose position=" + lastPose.Position + " rotation=" + lastPose.Rotation.eulerAngles);
            }
        }

        private void HandleSceneChange()
        {
            int activeSceneHandle = SceneManager.GetActiveScene().handle;
            if (activeSceneHandle == lastSceneHandle)
            {
                return;
            }

            RestorePersistentReflectionCameras();
            lastSceneHandle = activeSceneHandle;
            sourceCamera = null;
            nextCameraSearchTime = 0.0f;
            reflectionCameraDiagnosticsLogged = false;
            sceneTransitionPauseLogged = false;
            sceneTransitionPauseUntil = settings.SceneTransitionVrPauseSeconds <= 0.0f
                ? 0.0f
                : Time.unscaledTime + settings.SceneTransitionVrPauseSeconds;
            logger.Info("Scene changed; VR runtime will reacquire the game camera and sanitize reflection cameras early.");
        }

        private bool IsInSceneTransitionPause()
        {
            return sceneTransitionPauseUntil > 0.0f && Time.unscaledTime < sceneTransitionPauseUntil;
        }

        private void DisableReflectionCamerasEarly()
        {
            if (settings.DisableReflectionCameras && settings.KeepReflectionCamerasDisabledWhileVrActive)
            {
                DisableReflectionCamerasBeforeVrRender();
            }
        }

        public void Shutdown()
        {
            RestoreReflectionCameras();
            RestorePersistentReflectionCameras();

            if (rig != null)
            {
                rig.Shutdown();
                rig = null;
            }

            if (bridge != null)
            {
                bridge.Shutdown();
                bridge = null;
            }

            sourceCamera = null;
            initialized = false;
            vrReady = false;
            VrRuntimeState.IsVrReady = false;
            recenterYaw = Quaternion.identity;
            recenterPosition = Vector3.zero;
            recenterSourceYaw = Quaternion.identity;
            recenterSet = false;
            renderWidth = 0;
            renderHeight = 0;
            submitSuccessLogged = false;
            projectionModeLogged = false;
            nextCameraSearchTime = 0.0f;
            nextRenderTargetCheckTime = 0.0f;
            nextOpenVRRetryTime = 0.0f;
            sceneTransitionPauseUntil = 0.0f;
            sceneTransitionPauseLogged = false;
        }

        private bool TryInitializeOpenVR(bool firstAttempt)
        {
            if (bridge == null)
            {
                return false;
            }

            OpenVRInitResult initResult = bridge.Initialize(settings.AutoStartSteamVR);
            if (!initResult.IsSuccess)
            {
                string suffix = string.IsNullOrEmpty(initResult.Error) ? string.Empty : " " + initResult.Error;
                string prefix = firstAttempt ? "OpenVR initialization failed." : "OpenVR initialization retry failed.";
                limitedLog.Warning("openvr-init-retry", prefix + suffix + " Will retry in " + OpenVRRetrySeconds + " seconds.");
                vrReady = false;
                nextOpenVRRetryTime = Time.unscaledTime + OpenVRRetrySeconds;
                return false;
            }

            if (rig == null)
            {
                rig = new VrCameraRig(logger);
            }

            vrReady = true;
            VrRuntimeState.IsVrReady = true;
            nextOpenVRRetryTime = 0.0f;
            lastSceneHandle = SceneManager.GetActiveScene().handle;
            RefreshRenderTargetSize(true);
            FindSourceCamera(true);
            logger.Info("VR runtime initialized.");
            return true;
        }

        public void Recenter()
        {
            RuntimePose pose;
            if (bridge != null && TryGetHmdPose(out pose) && pose.IsValid)
            {
                lastPose = pose;
            }
            else
            {
                pose = lastPose.IsValid ? lastPose : RuntimePose.Identity;
            }

            ApplyRecenter(pose, "VR view recentered.");
        }

        private void ApplyRecenter(RuntimePose pose, string message)
        {
            recenterYaw = Quaternion.Inverse(Quaternion.Euler(0.0f, pose.Rotation.eulerAngles.y, 0.0f));
            recenterPosition = pose.Position * settings.WorldScale;
            recenterSourceYaw = sourceCamera == null ? Quaternion.identity : ExtractYaw(sourceCamera.transform.rotation);
            recenterSet = true;
            logger.Info(message);
        }

        private bool FindSourceCamera(bool force)
        {
            if (!force && sourceCamera != null && IsUsableSourceCamera(sourceCamera))
            {
                return true;
            }

            if (!force && Time.unscaledTime < nextCameraSearchTime)
            {
                return sourceCamera != null;
            }

            nextCameraSearchTime = Time.unscaledTime + settings.MissingCameraRetrySeconds;
            Camera candidate = Camera.main;
            if (!IsUsableSourceCamera(candidate))
            {
                candidate = FindBestEnabledCamera();
            }

            if (candidate == null)
            {
                sourceCamera = null;
                limitedLog.Warning("camera-missing", "No usable game camera found for VR yet.");
                return false;
            }

            if (sourceCamera != candidate)
            {
                sourceCamera = candidate;
                logger.Info("VR source camera set to " + GetCameraName(sourceCamera) + ".");
            }

            return true;
        }

        private Camera FindBestEnabledCamera()
        {
            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            Camera best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (!IsUsableSourceCamera(camera))
                {
                    continue;
                }

                float score = camera.depth;
                if (camera.targetTexture == null)
                {
                    score += 1000.0f;
                }

                if (camera.tag == "MainCamera")
                {
                    score += 500.0f;
                }

                if (camera.pixelWidth > 1 && camera.pixelHeight > 1)
                {
                    score += 100.0f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = camera;
                }
            }

            return best;
        }

        private bool IsUsableSourceCamera(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }

            if (rig != null && rig.IsOurCamera(camera))
            {
                return false;
            }

            GameObject cameraObject = camera.gameObject;
            return camera.enabled && cameraObject != null && cameraObject.activeInHierarchy;
        }

        private bool DisableReflectionCamerasBeforeVrRender()
        {
            RestoreReflectionCameras();

            if (!settings.DisableReflectionCameras)
            {
                return false;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            if (settings.LogReflectionCameraDiagnostics && !reflectionCameraDiagnosticsLogged)
            {
                LogReflectionCameraDiagnostics(cameras);
                reflectionCameraDiagnosticsLogged = true;
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (!IsReflectionCameraCandidate(camera))
                {
                    continue;
                }

                if (settings.KeepReflectionCamerasDisabledWhileVrActive)
                {
                    PersistentlyDisableReflectionCamera(camera);
                }
                else
                {
                    disabledReflectionCameras.Add(new DisabledCameraState(camera, camera.enabled));
                    camera.enabled = false;
                    limitedLog.Info("reflection-camera-disabled", "Temporarily disabling reflection camera for VR render: " + GetCameraName(camera));
                }
            }

            return !settings.KeepReflectionCamerasDisabledWhileVrActive;
        }

        private void RestoreReflectionCameras()
        {
            for (int i = 0; i < disabledReflectionCameras.Count; i++)
            {
                DisabledCameraState state = disabledReflectionCameras[i];
                if (state.Camera != null)
                {
                    state.Camera.enabled = state.WasEnabled;
                }
            }

            disabledReflectionCameras.Clear();
        }

        private void PersistentlyDisableReflectionCamera(Camera camera)
        {
            if (camera == null || IsPersistentlyDisabled(camera))
            {
                return;
            }

            persistentlyDisabledReflectionCameras.Add(new DisabledCameraState(camera, camera.enabled));
            camera.enabled = false;
            logger.Info("Persistently disabling reflection camera while VR is active: " + GetCameraName(camera));
        }

        private bool IsPersistentlyDisabled(Camera camera)
        {
            for (int i = 0; i < persistentlyDisabledReflectionCameras.Count; i++)
            {
                DisabledCameraState state = persistentlyDisabledReflectionCameras[i];
                if (state.Camera == camera)
                {
                    return true;
                }
            }

            return false;
        }

        private void RestorePersistentReflectionCameras()
        {
            for (int i = 0; i < persistentlyDisabledReflectionCameras.Count; i++)
            {
                DisabledCameraState state = persistentlyDisabledReflectionCameras[i];
                if (state.Camera != null)
                {
                    state.Camera.enabled = state.WasEnabled;
                }
            }

            persistentlyDisabledReflectionCameras.Clear();
        }

        private bool IsReflectionCameraCandidate(Camera camera)
        {
            if (camera == null || camera == sourceCamera)
            {
                return false;
            }

            if (rig != null && rig.IsOurCamera(camera))
            {
                return false;
            }

            if (!camera.enabled || camera.gameObject == null || !camera.gameObject.activeInHierarchy)
            {
                return false;
            }

            string cameraName = GetCameraName(camera);
            bool nameLooksReflective = NameContainsReflectionKeyword(cameraName);
            bool targetTextureLooksReflective = camera.targetTexture != null && NameContainsReflectionKeyword(camera.targetTexture.name);
            bool targetTextureCamera = settings.DisableTargetTextureCameras && camera.targetTexture != null;

            return nameLooksReflective || targetTextureLooksReflective || targetTextureCamera;
        }

        private void LogReflectionCameraDiagnostics(Camera[] cameras)
        {
            var entries = new List<string>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                {
                    continue;
                }

                string role = camera == sourceCamera ? "source" : "other";
                if (rig != null && rig.IsOurCamera(camera))
                {
                    role = "vr";
                }

                RenderTexture target = camera.targetTexture;
                string targetDescription = target == null
                    ? "none"
                    : (string.IsNullOrEmpty(target.name) ? "<unnamed>" : target.name) + " " + target.width + "x" + target.height;

                bool active = camera.gameObject != null && camera.gameObject.activeInHierarchy;
                bool candidate = IsReflectionCameraCandidate(camera);
                entries.Add(GetCameraName(camera) +
                    " role=" + role +
                    " enabled=" + camera.enabled +
                    " active=" + active +
                    " targetTexture=" + targetDescription +
                    " depth=" + camera.depth +
                    " candidate=" + candidate);

                if (entries.Count >= 16)
                {
                    entries.Add("...");
                    break;
                }
            }

            logger.Info("VR camera/reflection diagnostics: " + string.Join(" | ", entries.ToArray()));
        }

        private bool NameContainsReflectionKeyword(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] keywords = GetReflectionCameraKeywords();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (value.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private string[] GetReflectionCameraKeywords()
        {
            if (reflectionCameraKeywords.Length > 0)
            {
                return reflectionCameraKeywords;
            }

            string rawKeywords = settings.ReflectionCameraNameKeywords ?? string.Empty;
            string[] parts = rawKeywords.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var keywords = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string keyword = parts[i].Trim();
                if (keyword.Length > 0)
                {
                    keywords.Add(keyword);
                }
            }

            if (keywords.Count == 0)
            {
                keywords.Add("mirror");
                keywords.Add("reflect");
                keywords.Add("reflection");
                keywords.Add("planar");
                keywords.Add("water");
            }

            reflectionCameraKeywords = keywords.ToArray();
            return reflectionCameraKeywords;
        }

        private void RefreshRenderTargetSize(bool force)
        {
            if (!force && Time.unscaledTime < nextRenderTargetCheckTime && renderWidth > 0 && renderHeight > 0)
            {
                return;
            }

            nextRenderTargetCheckTime = Time.unscaledTime + settings.RenderTargetCheckSeconds;

            int width;
            int height;
            if (bridge != null && bridge.TryGetRecommendedRenderTargetSize(out var recommendedWidth, out var recommendedHeight, out _))
            {
                width = (int)recommendedWidth;
                height = (int)recommendedHeight;
                renderWidth = Mathf.RoundToInt(width * settings.RenderScale);
                renderHeight = Mathf.RoundToInt(height * settings.RenderScale);
            }
            else
            {
                renderWidth = Mathf.RoundToInt(settings.FallbackRenderWidth * settings.RenderScale);
                renderHeight = Mathf.RoundToInt(settings.FallbackRenderHeight * settings.RenderScale);
            }

            renderWidth = Mathf.Clamp(renderWidth, 256, 8192);
            renderHeight = Mathf.Clamp(renderHeight, 256, 8192);
        }

        private void ApplyPoseToRig()
        {
            RuntimePose pose = lastPose;
            Vector3 rawPosition = pose.Position * settings.WorldScale;
            Quaternion rawRotation = pose.Rotation;

            if (recenterSet)
            {
                rawPosition = recenterYaw * (rawPosition - recenterPosition);
                rawRotation = recenterYaw * rawRotation;
            }

            Vector3 basePosition = sourceCamera.transform.position + sourceCamera.transform.up * settings.CameraHeightOffset;
            Quaternion baseRotation = GetSourceBaseRotation();
            Vector3 headPosition = basePosition + baseRotation * rawPosition;
            Quaternion headRotation = baseRotation * rawRotation;
            float ipdMeters = bridge == null ? DefaultIpdMeters : GetIpdMeters(DefaultIpdMeters);
            rig.ApplyPose(headPosition, headRotation, ipdMeters, settings.IPDScale, settings.WorldScale);
        }

        private Quaternion GetSourceBaseRotation()
        {
            if (sourceCamera == null)
            {
                return Quaternion.identity;
            }

            switch (settings.SourceRotationMode)
            {
                case VrSourceRotationMode.FullSourceCamera:
                    return sourceCamera.transform.rotation;
                case VrSourceRotationMode.RecenterYaw:
                    return recenterSourceYaw;
                case VrSourceRotationMode.HmdOnly:
                    return Quaternion.identity;
                default:
                    return ExtractYaw(sourceCamera.transform.rotation);
            }
        }

        private static Quaternion ExtractYaw(Quaternion rotation)
        {
            return Quaternion.Euler(0.0f, rotation.eulerAngles.y, 0.0f);
        }

        private void ApplyProjectionOrCameraFallback()
        {
            if (!settings.UseOpenVRProjection || settings.OpenVRProjectionMode == OpenVRProjectionMode.SourceCamera)
            {
                if (!projectionModeLogged)
                {
                    projectionModeLogged = true;
                    logger.Info("Using source camera projection for VR eyes.");
                }

                if (rig.LeftEyeCamera != null)
                {
                    rig.LeftEyeCamera.ResetProjectionMatrix();
                }

                if (rig.RightEyeCamera != null)
                {
                    rig.RightEyeCamera.ResetProjectionMatrix();
                }

                rig.ResetCullingMatrices();
                return;
            }

            if (!projectionModeLogged)
            {
                projectionModeLogged = true;
                logger.Info("Using OpenVR projection mode: " + settings.OpenVRProjectionMode + ".");
            }

            Matrix4x4 projection;
            if (bridge != null && bridge.TryGetEyeProjection(OpenVREye.Left, sourceCamera.nearClipPlane, sourceCamera.farClipPlane, settings.OpenVRProjectionMode, out projection, out _))
            {
                rig.ApplyProjection(RuntimeEye.Left, projection);
            }
            else if (rig.LeftEyeCamera != null)
            {
                rig.LeftEyeCamera.ResetProjectionMatrix();
            }

            if (bridge != null && bridge.TryGetEyeProjection(OpenVREye.Right, sourceCamera.nearClipPlane, sourceCamera.farClipPlane, settings.OpenVRProjectionMode, out projection, out _))
            {
                rig.ApplyProjection(RuntimeEye.Right, projection);
            }
            else if (rig.RightEyeCamera != null)
            {
                rig.RightEyeCamera.ResetProjectionMatrix();
            }

            if (settings.UseSourceProjectionForCulling)
            {
                rig.ApplyCullingFromSourceProjection(sourceCamera);
            }
            else
            {
                rig.ResetCullingMatrices();
            }
        }

        private string GetCameraName(Camera camera)
        {
            if (camera == null || camera.gameObject == null)
            {
                return "<null>";
            }

            return camera.gameObject.name;
        }

        private readonly struct DisabledCameraState
        {
            public DisabledCameraState(Camera camera, bool wasEnabled)
            {
                Camera = camera;
                WasEnabled = wasEnabled;
            }

            public Camera Camera { get; }

            public bool WasEnabled { get; }
        }

        private static string SubmitStatus(bool submitted, string error, IntPtr nativeTexturePtr)
        {
            string pointer = nativeTexturePtr == IntPtr.Zero ? "null" : "0x" + nativeTexturePtr.ToInt64().ToString("X");
            return submitted ? "ok ptr=" + pointer : "failed ptr=" + pointer + " error=" + error;
        }

        private bool TryGetHmdPose(out RuntimePose pose)
        {
            pose = RuntimePose.Identity;
            if (bridge == null || !bridge.TryGetHmdPose(out var openVrPose, out _))
            {
                return false;
            }

            pose = new RuntimePose
            {
                Position = openVrPose.Position,
                Rotation = openVrPose.Rotation,
                IsValid = openVrPose.IsValid
            };
            return pose.IsValid;
        }

        private float GetIpdMeters(float fallback)
        {
            if (bridge == null ||
                !bridge.TryGetEyeToHeadTransform(OpenVREye.Left, out var leftPosition, out _, out _, out _) ||
                !bridge.TryGetEyeToHeadTransform(OpenVREye.Right, out var rightPosition, out _, out _, out _))
            {
                return fallback;
            }

            return Mathf.Abs(rightPosition.x - leftPosition.x);
        }

        private bool SubmitEye(RuntimeEye eye, IntPtr nativeTexturePtr, OpenVRTextureSubmitType submitType, out string error)
        {
            error = string.Empty;
            if (bridge == null || nativeTexturePtr == IntPtr.Zero)
            {
                error = "Bridge or native texture pointer is null.";
                return false;
            }

            return bridge.Submit(
                eye == RuntimeEye.Right ? OpenVREye.Right : OpenVREye.Left,
                nativeTexturePtr,
                submitType,
                QualitySettings.activeColorSpace == ColorSpace.Linear,
                settings.FlipSubmitV,
                out error);
        }
    }
}
