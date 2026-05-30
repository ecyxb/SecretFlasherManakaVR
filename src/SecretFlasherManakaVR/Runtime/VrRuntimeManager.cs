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

        private readonly RateLimitedLogger limitedLog;
        private readonly IVrRuntimeLogger logger;

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
        private float nextRenderTargetCheckTime;
        private int renderWidth;
        private int renderHeight;
        private int lastSceneHandle = -1;
        private float sceneTransitionPauseUntil;
        private bool submitSuccessLogged;
        private bool projectionModeLogged;
        private bool reflectionCameraDiagnosticsLogged;
        private bool reflectionProbeDiagnosticsLogged;
        private bool sceneTransitionPauseLogged;
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
            reflectionProbeDiagnosticsLogged = false;
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

            ApplySourceCameraRenderSuppression();
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
            RestorePersistentReflectionProbes();
            RestorePersistentMirrorManagers();
            RestoreSourceCameraRendering();
            if (uiBridge != null)
            {
                uiBridge.OnSceneChanged(settings);
            }

            lastSceneHandle = activeSceneHandle;
            sourceCamera = null;
            nextCameraSearchTime = 0.0f;
            reflectionCameraDiagnosticsLogged = false;
            reflectionProbeDiagnosticsLogged = false;
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

            if (settings.DisableReflectionProbes)
            {
                DisableReflectionProbesEarly();
            }

            if (settings.DisableMirrorManagersWhileVrActive)
            {
                DisableMirrorManagersEarly();
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

            npcWorldSpaceUiFixer = null;

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
            VrRuntimeState.ClearHeadPose();
            recenterYaw = Quaternion.identity;
            recenterPosition = Vector3.zero;
            recenterSourceYaw = Quaternion.identity;
            recenterSet = false;
            renderWidth = 0;
            renderHeight = 0;
            submitSuccessLogged = false;
            projectionModeLogged = false;
            reflectionCameraDiagnosticsLogged = false;
            reflectionProbeDiagnosticsLogged = false;
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

            if (uiBridge == null)
            {
                uiBridge = new VrUiBridge(logger);
            }

            if (npcWorldSpaceUiFixer == null)
            {
                npcWorldSpaceUiFixer = new NpcWorldSpaceUiFixer(logger);
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
                RestoreSourceCameraRendering();
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
                logger.Info("Disabled source camera rendering while VR is active: " + GetCameraName(sourceCamera) + ".");
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

        private void DisableReflectionProbesEarly()
        {
            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
            if (settings.LogReflectionProbeDiagnostics && !reflectionProbeDiagnosticsLogged)
            {
                LogReflectionProbeDiagnostics(probes);
                reflectionProbeDiagnosticsLogged = true;
            }

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
            if (probe == null || IsPersistentlyDisabled(probe))
            {
                return;
            }

            persistentlyDisabledReflectionProbes.Add(new DisabledProbeState(probe, probe.enabled));
            probe.enabled = false;
            logger.Info("Persistently disabling ReflectionProbe while VR is active: " + ProbeDescription(probe));
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

        private void DisableMirrorManagersEarly()
        {
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
                limitedLog.Warning("mirror-manager-candidate-error", "Skipping MirrorManager candidate check after exception: " + ex.Message);
                return false;
            }
        }

        private void PersistentlyDisableMirrorManager(MirrorManager manager)
        {
            if (manager == null || IsPersistentlyDisabled(manager))
            {
                return;
            }

            persistentlyDisabledMirrorManagers.Add(new DisabledBehaviourState(manager, manager.enabled));
            manager.enabled = false;
            logger.Info("Persistently disabling MirrorManager while VR is active: " + SafeBehaviourDescription(manager));
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

        private void LogReflectionProbeDiagnostics(ReflectionProbe[] probes)
        {
            var entries = new List<string>();
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe probe = probes[i];
                if (probe == null || probe.gameObject == null)
                {
                    continue;
                }

                entries.Add(ProbeDescription(probe));
                if (entries.Count >= 16)
                {
                    entries.Add("...");
                    break;
                }
            }

            logger.Info("VR reflection probe diagnostics: " + string.Join(" | ", entries.ToArray()));
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
            VrRuntimeState.SetHeadPose(headPosition, headRotation);
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

        private static string ProbeDescription(ReflectionProbe probe)
        {
            if (probe == null || probe.gameObject == null)
            {
                return "<null>";
            }

            return GetPath(probe.gameObject) +
                " scene=" + probe.gameObject.scene.name +
                " enabled=" + probe.enabled +
                " mode=" + probe.mode +
                " refreshMode=" + probe.refreshMode +
                " timeSlicing=" + probe.timeSlicingMode +
                " resolution=" + probe.resolution +
                " cullingMask=0x" + probe.cullingMask.ToString("X");
        }

        private static string BehaviourDescription(Behaviour behaviour)
        {
            if (behaviour == null || behaviour.gameObject == null)
            {
                return "<null>";
            }

            return GetPath(behaviour.gameObject) +
                " scene=" + behaviour.gameObject.scene.name +
                " type=" + behaviour.GetType().FullName +
                " enabled=" + behaviour.enabled;
        }

        private static string SafeBehaviourDescription(Behaviour behaviour)
        {
            try
            {
                return BehaviourDescription(behaviour);
            }
            catch (Exception ex)
            {
                string typeName = behaviour == null ? "<null>" : behaviour.GetType().FullName;
                return typeName + " description failed: " + ex.Message;
            }
        }

        private static string GetPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            Transform current = gameObject.transform;
            string path = gameObject.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.gameObject.name + "/" + path;
            }

            return path;
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
