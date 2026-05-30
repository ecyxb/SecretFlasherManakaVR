using System;
using System.Collections.Generic;
using AkilliMum.Standard.Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using SecretFlasherManakaVR.OpenVR;

namespace SecretFlasherManakaVR.Runtime
{
    public sealed class VrRuntimeManager
    {
        private const float DefaultIpdMeters = 0.064f;
        private const float OpenVRRetrySeconds = 5.0f;

        private VrRuntimeSettings settings;
        private IOpenVRBridge bridge;
        private VrCameraRig rig;
        private VrUiBridge uiBridge;
        private NpcWorldSpaceUiFixer npcWorldSpaceUiFixer;
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
        private int renderWidth;
        private int renderHeight;
        private int lastSceneHandle = -1;
        private float sceneTransitionPauseUntil;
        private string[] reflectionCameraKeywords = Array.Empty<string>();
        private DisabledCameraState? disabledSourceCamera;
        private readonly List<DisabledCameraState> disabledReflectionCameras = new List<DisabledCameraState>();
        private readonly List<DisabledCameraState> persistentlyDisabledReflectionCameras = new List<DisabledCameraState>();
        private readonly List<DisabledProbeState> persistentlyDisabledReflectionProbes = new List<DisabledProbeState>();
        private readonly List<DisabledBehaviourState> persistentlyDisabledMirrorManagers = new List<DisabledBehaviourState>();

        public VrRuntimeManager()
            : this(NullVrRuntimeLogger.Instance)
        {
        }

        public VrRuntimeManager(IVrRuntimeLogger logger)
        {
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
            initialized = true;
            vrReady = false;

            if (!settings.EnableVR)
            {
                return;
            }

            if (openVrBridge == null)
            {
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

            if (VrRuntimeState.ConsumeRecenterRequest())
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
                return;
            }

            HandleSceneChange();
            if (IsInSceneTransitionPause())
            {
                DisableReflectionCamerasEarly();
                return;
            }

            if (!FindSourceCamera(false))
            {
                VrRuntimeState.SetSourceCamera(null);
                return;
            }
            VrRuntimeState.SetSourceCamera(sourceCamera);

            ApplySourceCameraRenderSuppression();
            RefreshRenderTargetSize(false);
            bridge.BeginFrame();

            if (!TryGetHmdPose(out lastPose))
            {
            }

            if (!lastPose.IsValid)
            {
                lastPose = RuntimePose.Identity;
            }
            else if (settings.AutoRecenterOnStart && !recenterSet)
            {
                ApplyRecenter(lastPose);
            }

            rig.EnsureCreated();
            rig.EnsureRenderTextures(renderWidth, renderHeight, settings.AntiAliasing);
            DisableReflectionCamerasEarly();
            rig.CopyFromSource(sourceCamera);
            ApplyPoseToRig();
            SecretFlasherManakaVR.PlayerHeadPoseController.Apply();
            ApplyProjectionOrCameraFallback();
            if (npcWorldSpaceUiFixer != null)
            {
                npcWorldSpaceUiFixer.Tick(settings, sourceCamera, rig.HeadPosition, rig.HeadRotation);
            }

            int uiOverlayLayerMask = settings.FixNpcWorldSpaceUi ? 1 << VrUiBridge.VrUiOverlayLayer : 0;
            if (uiBridge != null)
            {
                uiBridge.Tick(settings, sourceCamera, rig.HeadPosition, rig.HeadRotation);
                uiOverlayLayerMask |= uiBridge.ConvertedLayerMask;
            }

            bool restoreFrameDisabledCameras = DisableReflectionCamerasBeforeVrRender();
            try
            {
                rig.Render(uiOverlayLayerMask);
            }
            finally
            {
                if (restoreFrameDisabledCameras)
                {
                    RestoreReflectionCameras();
                }
            }

            SubmitEye(RuntimeEye.Left, rig.LeftSubmitTexturePtr, rig.LeftSubmitTextureType, out _);
            SubmitEye(RuntimeEye.Right, rig.RightSubmitTexturePtr, rig.RightSubmitTextureType, out _);

            if (settings.MirrorMode == VrMirrorMode.LeftEye || settings.MirrorMode == VrMirrorMode.RightEye)
            {
                rig.Mirror(settings.MirrorMode);
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
            RestorePersistentReflectionProbes();
            RestorePersistentMirrorManagers();
            RestoreSourceCameraRendering();
            if (uiBridge != null)
            {
                uiBridge.OnSceneChanged(settings);
            }

            if (npcWorldSpaceUiFixer != null)
            {
                npcWorldSpaceUiFixer.Shutdown();
            }

            lastSceneHandle = activeSceneHandle;
            sourceCamera = null;
            nextCameraSearchTime = 0.0f;
            DisableMirrorManagersForCurrentScene();
            sceneTransitionPauseUntil = settings.SceneTransitionVrPauseSeconds <= 0.0f
                ? 0.0f
                : Time.unscaledTime + settings.SceneTransitionVrPauseSeconds;
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

            if (settings.DisableReflectionProbes)
            {
                DisableReflectionProbesEarly();
            }
        }

        public void Shutdown()
        {
            RestoreReflectionCameras();
            RestorePersistentReflectionCameras();
            RestorePersistentReflectionProbes();
            RestorePersistentMirrorManagers();
            RestoreSourceCameraRendering();

            if (uiBridge != null)
            {
                uiBridge.Shutdown();
                uiBridge = null;
            }

            if (npcWorldSpaceUiFixer != null)
            {
                npcWorldSpaceUiFixer.Shutdown();
                npcWorldSpaceUiFixer = null;
            }

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
            VrRuntimeState.SetSourceCamera(null);
            VrRuntimeState.ClearHeadPose();
            recenterYaw = Quaternion.identity;
            recenterPosition = Vector3.zero;
            recenterSourceYaw = Quaternion.identity;
            recenterSet = false;
            renderWidth = 0;
            renderHeight = 0;
            nextCameraSearchTime = 0.0f;
            nextOpenVRRetryTime = 0.0f;
            sceneTransitionPauseUntil = 0.0f;
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
                vrReady = false;
                nextOpenVRRetryTime = Time.unscaledTime + OpenVRRetrySeconds;
                return false;
            }

            if (rig == null)
            {
                rig = new VrCameraRig(NullVrRuntimeLogger.Instance);
            }

            if (uiBridge == null)
            {
                uiBridge = new VrUiBridge(NullVrRuntimeLogger.Instance);
            }

            if (npcWorldSpaceUiFixer == null)
            {
                npcWorldSpaceUiFixer = new NpcWorldSpaceUiFixer(NullVrRuntimeLogger.Instance);
            }

            vrReady = true;
            VrRuntimeState.IsVrReady = true;
            nextOpenVRRetryTime = 0.0f;
            lastSceneHandle = SceneManager.GetActiveScene().handle;
            RefreshRenderTargetSize(true);
            FindSourceCamera(true);
            DisableMirrorManagersForCurrentScene();
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

            ApplyRecenter(pose);
        }

        private void ApplyRecenter(RuntimePose pose)
        {
            recenterYaw = Quaternion.Inverse(Quaternion.Euler(0.0f, pose.Rotation.eulerAngles.y, 0.0f));
            recenterPosition = pose.Position * settings.WorldScale;
            recenterSourceYaw = sourceCamera == null ? Quaternion.identity : ExtractYaw(sourceCamera.transform.rotation);
            recenterSet = true;
            VrRuntimeState.MarkRecentered();
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
                VrRuntimeState.SetSourceCamera(null);
                return false;
            }

            if (sourceCamera != candidate)
            {
                RestoreSourceCameraRendering();
                sourceCamera = candidate;
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
            bool sourceSuppressed = settings.DisableSourceCameraRendering &&
                sourceCamera == camera &&
                disabledSourceCamera.HasValue &&
                disabledSourceCamera.Value.Camera == camera;
            return (camera.enabled || sourceSuppressed) && cameraObject != null && cameraObject.activeInHierarchy;
        }

        private void ApplySourceCameraRenderSuppression()
        {
            if (!settings.DisableSourceCameraRendering)
            {
                RestoreSourceCameraRendering();
                return;
            }

            if (sourceCamera == null || rig != null && rig.IsOurCamera(sourceCamera))
            {
                return;
            }

            if (disabledSourceCamera.HasValue && disabledSourceCamera.Value.Camera == sourceCamera)
            {
                return;
            }

            RestoreSourceCameraRendering();
            disabledSourceCamera = new DisabledCameraState(sourceCamera, sourceCamera.enabled);
            if (sourceCamera.enabled)
            {
                sourceCamera.enabled = false;
            }
        }

        private void RestoreSourceCameraRendering()
        {
            if (!disabledSourceCamera.HasValue)
            {
                return;
            }

            DisabledCameraState state = disabledSourceCamera.Value;
            disabledSourceCamera = null;
            if (state.Camera != null)
            {
                state.Camera.enabled = state.WasEnabled;
            }
        }

        private bool DisableReflectionCamerasBeforeVrRender()
        {
            RestoreReflectionCameras();

            if (!settings.DisableReflectionCameras)
            {
                return false;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();

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
            if (camera == null)
            {
                return;
            }

            if (IsPersistentlyDisabled(camera))
            {
                if (camera.enabled)
                {
                    camera.enabled = false;
                }

                return;
            }

            persistentlyDisabledReflectionCameras.Add(new DisabledCameraState(camera, camera.enabled));
            camera.enabled = false;
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

        private void DisableReflectionProbesEarly()
        {
            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe probe = probes[i];
                if (!IsReflectionProbeCandidate(probe))
                {
                    continue;
                }

                PersistentlyDisableReflectionProbe(probe);
            }
        }

        private bool IsReflectionProbeCandidate(ReflectionProbe probe)
        {
            return probe != null &&
                probe.enabled &&
                probe.gameObject != null &&
                probe.gameObject.activeInHierarchy;
        }

        private void PersistentlyDisableReflectionProbe(ReflectionProbe probe)
        {
            if (probe == null)
            {
                return;
            }

            if (IsPersistentlyDisabled(probe))
            {
                if (probe.enabled)
                {
                    probe.enabled = false;
                }

                return;
            }

            persistentlyDisabledReflectionProbes.Add(new DisabledProbeState(probe, probe.enabled));
            probe.enabled = false;
        }

        private bool IsPersistentlyDisabled(ReflectionProbe probe)
        {
            for (int i = 0; i < persistentlyDisabledReflectionProbes.Count; i++)
            {
                DisabledProbeState state = persistentlyDisabledReflectionProbes[i];
                if (state.Probe == probe)
                {
                    return true;
                }
            }

            return false;
        }

        private void RestorePersistentReflectionProbes()
        {
            for (int i = 0; i < persistentlyDisabledReflectionProbes.Count; i++)
            {
                DisabledProbeState state = persistentlyDisabledReflectionProbes[i];
                if (state.Probe != null)
                {
                    state.Probe.enabled = state.WasEnabled;
                }
            }

            persistentlyDisabledReflectionProbes.Clear();
        }

        private void DisableMirrorManagersForCurrentScene()
        {
            if (!settings.DisableMirrorManagersWhileVrActive)
            {
                return;
            }

            MirrorManager[] managers = UnityEngine.Object.FindObjectsOfType<MirrorManager>();
            for (int i = 0; i < managers.Length; i++)
            {
                MirrorManager manager = managers[i];
                if (!IsMirrorManagerCandidate(manager))
                {
                    continue;
                }

                PersistentlyDisableMirrorManager(manager);
            }
        }

        private bool IsMirrorManagerCandidate(MirrorManager manager)
        {
            try
            {
                return manager != null &&
                    manager.enabled &&
                    manager.gameObject != null &&
                    manager.gameObject.activeInHierarchy;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private void PersistentlyDisableMirrorManager(MirrorManager manager)
        {
            if (manager == null)
            {
                return;
            }

            if (IsPersistentlyDisabled(manager))
            {
                if (manager.enabled)
                {
                    manager.enabled = false;
                }

                return;
            }

            persistentlyDisabledMirrorManagers.Add(new DisabledBehaviourState(manager, manager.enabled));
            manager.enabled = false;
        }

        private bool IsPersistentlyDisabled(Behaviour behaviour)
        {
            for (int i = 0; i < persistentlyDisabledMirrorManagers.Count; i++)
            {
                DisabledBehaviourState state = persistentlyDisabledMirrorManagers[i];
                if (state.Behaviour == behaviour)
                {
                    return true;
                }
            }

            return false;
        }

        private void RestorePersistentMirrorManagers()
        {
            VrRuntimeState.BeginSuppressedObjectRestore();
            try
            {
                for (int i = 0; i < persistentlyDisabledMirrorManagers.Count; i++)
                {
                    DisabledBehaviourState state = persistentlyDisabledMirrorManagers[i];
                    if (state.Behaviour != null)
                    {
                        state.Behaviour.enabled = state.WasEnabled;
                    }
                }
            }
            finally
            {
                VrRuntimeState.EndSuppressedObjectRestore();
            }

            persistentlyDisabledMirrorManagers.Clear();
        }

        private bool IsReflectionCameraCandidate(Camera camera)
        {
            if (camera == null || camera == sourceCamera || ReflectionBlocker.IsUiPreviewCamera(camera))
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
            if (!force && renderWidth > 0 && renderHeight > 0)
            {
                return;
            }

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

            if (settings.IgnoreHeadPositionForVrCamera)
            {
                rawPosition = new Vector3(
                    Mathf.Clamp(rawPosition.x, settings.HeadPositionCameraOffsetMinX, settings.HeadPositionCameraOffsetMaxX),
                    Mathf.Clamp(rawPosition.y, settings.HeadPositionCameraOffsetMinY, settings.HeadPositionCameraOffsetMaxY),
                    Mathf.Clamp(rawPosition.z, settings.HeadPositionCameraOffsetMinZ, settings.HeadPositionCameraOffsetMaxZ));
            }

            Vector3 basePosition = sourceCamera.transform.position + sourceCamera.transform.up * settings.CameraHeightOffset;
            Quaternion baseRotation = GetSourceBaseRotation();
            VrRuntimeState.SetTrackingToWorldTransform(
                basePosition,
                baseRotation,
                recenterPosition,
                recenterYaw,
                settings.WorldScale,
                recenterSet);
            Vector3 headPosition = basePosition + baseRotation * rawPosition;
            Quaternion headRotation = baseRotation * rawRotation;
            float ipdMeters = bridge == null ? DefaultIpdMeters : GetIpdMeters(DefaultIpdMeters);
            rig.ApplyPose(headPosition, headRotation, ipdMeters, settings.IPDScale, settings.WorldScale);
            VrRuntimeState.SetHeadPose(headPosition, headRotation, rawRotation);
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

        private readonly struct DisabledBehaviourState
        {
            public DisabledBehaviourState(Behaviour behaviour, bool wasEnabled)
            {
                Behaviour = behaviour;
                WasEnabled = wasEnabled;
            }

            public Behaviour Behaviour { get; }

            public bool WasEnabled { get; }
        }

        private readonly struct DisabledProbeState
        {
            public DisabledProbeState(ReflectionProbe probe, bool wasEnabled)
            {
                Probe = probe;
                WasEnabled = wasEnabled;
            }

            public ReflectionProbe Probe { get; }

            public bool WasEnabled { get; }
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
