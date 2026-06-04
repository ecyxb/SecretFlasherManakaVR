using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Scripts.InGame;
using UnityEngine;
using UnityEngine.UI;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class VrUiBridge
    {
        internal const int VrUiOverlayLayer = 30;
        private const int UnityUiLayer = 5;
        private const int FallbackTextureWidth = 1920;
        private const int FallbackTextureHeight = 1080;
        private const string DefaultCapturedCanvasName = "InGameCanvas";

        private readonly List<Canvas> activeCanvases = new List<Canvas>();
        private readonly List<CanvasCaptureState> capturedCanvasStates = new List<CanvasCaptureState>();
        private float nextScanTime;
        private int convertedLayerMask;
        private bool worldFixedPoseSet;
        private Vector3 worldFixedPosition;
        private Quaternion worldFixedRotation = Quaternion.identity;
        private Quaternion worldFixedFullscreenEffectRotation = Quaternion.identity;
        private Vector3 uiPosition;
        private Quaternion uiRotation = Quaternion.identity;
        private Vector3 fullscreenEffectPosition;
        private Quaternion fullscreenEffectRotation = Quaternion.identity;
        private GameObject root;
        private Camera captureCamera;
        private RenderTexture uiTexture;
        private RenderTexture fullscreenEffectTexture;
        private RenderTexture eyeMaskTexture;
        private GameObject panelObject;
        private GameObject fullscreenEffectPanelObject;
        private Material panelMaterial;
        private Material fullscreenEffectPanelMaterial;
        private Mesh fullscreenEffectMesh;
        private int textureWidth;
        private int textureHeight;
        private int fullscreenEffectMeshSignature;
        private Canvas fullscreenEffectCanvas;
        private RectTransform fullscreenEffectRoot;
        private FullscreenEffectCaptureSet fullscreenEffectCache;
        private bool fullscreenEffectCacheScanned;
        private bool eyeMaskOverlayVisible;

        private static bool currentPanelVisible;
        private static Vector3 currentPanelPosition;
        private static Quaternion currentPanelRotation = Quaternion.identity;
        private static Vector3 currentPanelScale = Vector3.one;
        private static int currentTextureWidth;
        private static int currentTextureHeight;

        public VrUiBridge(IVrRuntimeLogger logger)
        {
        }

        public int ConvertedLayerMask
        {
            get { return convertedLayerMask; }
        }

        public int ConvertedCanvasCount
        {
            get { return activeCanvases.Count; }
        }

        public Texture EyeMaskOverlayTexture
        {
            get { return eyeMaskOverlayVisible ? eyeMaskTexture : null; }
        }

        public static bool TryRaycastCapturedScreen(Vector3 origin, Vector3 direction, out Vector2 screenPoint)
        {
            return TryRaycastCapturedScreen(origin, direction, out screenPoint, out _);
        }

        public static bool TryRaycastCapturedScreen(Vector3 origin, Vector3 direction, out Vector2 screenPoint, out Vector3 hitPoint)
        {
            screenPoint = Vector2.zero;
            hitPoint = Vector3.zero;
            if (!currentPanelVisible || currentTextureWidth <= 0 || currentTextureHeight <= 0 || direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 normal = currentPanelRotation * Vector3.forward;
            float denominator = Vector3.Dot(direction.normalized, normal);
            if (Mathf.Abs(denominator) <= 0.0001f)
            {
                return false;
            }

            float distance = Vector3.Dot(currentPanelPosition - origin, normal) / denominator;
            if (distance < 0.0f)
            {
                return false;
            }

            Vector3 hit = origin + direction.normalized * distance;
            Matrix4x4 worldToPanel = Matrix4x4.TRS(currentPanelPosition, currentPanelRotation, currentPanelScale).inverse;
            Vector3 local = worldToPanel.MultiplyPoint3x4(hit);
            if (local.x < -0.5f || local.x > 0.5f || local.y < -0.5f || local.y > 0.5f)
            {
                return false;
            }

            screenPoint = new Vector2(
                (local.x + 0.5f) * currentTextureWidth,
                (local.y + 0.5f) * currentTextureHeight);
            hitPoint = hit;
            return true;
        }

        public static bool TryGetCapturedPanelPose(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = currentPanelPosition;
            rotation = currentPanelRotation;
            scale = currentPanelScale;
            return currentPanelVisible && currentTextureWidth > 0 && currentTextureHeight > 0;
        }

        public void Tick(VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            if (settings == null || !settings.EnableVrUiBridge || !settings.ConvertOverlayCanvasToWorldSpace)
            {
                RestoreCapturedCanvases();
                activeCanvases.Clear();
                SetPanelVisible(false);
                SetFullscreenEffectPanelVisible(false);
                eyeMaskOverlayVisible = false;
                convertedLayerMask = 0;
                return;
            }

            EnsureCreated();
            UpdateAnchor(settings, sourceCamera, headPosition, headRotation);
            ScanCanvasesIfNeeded(settings);
            RenderUiToTexture(settings);
            ApplyPanelTransform(settings);
            convertedLayerMask = activeCanvases.Count == 0 ? 0 : 1 << VrUiOverlayLayer;
        }

        public void OnSceneChanged(VrRuntimeSettings settings)
        {
            RestoreCapturedCanvases();
            activeCanvases.Clear();
            ClearFullscreenEffectCache();
            nextScanTime = 0.0f;
            worldFixedPoseSet = false;
            SetPanelVisible(false);
            SetFullscreenEffectPanelVisible(false);
            eyeMaskOverlayVisible = false;
            convertedLayerMask = 0;
        }

        public void Shutdown()
        {
            RestoreCapturedCanvases();
            activeCanvases.Clear();
            ClearFullscreenEffectCache();
            convertedLayerMask = 0;
            ReleaseTexture();
            ReleaseFullscreenEffectTexture();
            ReleaseEyeMaskTexture();

            if (panelMaterial != null)
            {
                UnityEngine.Object.Destroy(panelMaterial);
                panelMaterial = null;
            }

            if (fullscreenEffectPanelMaterial != null)
            {
                UnityEngine.Object.Destroy(fullscreenEffectPanelMaterial);
                fullscreenEffectPanelMaterial = null;
            }

            if (fullscreenEffectMesh != null)
            {
                UnityEngine.Object.Destroy(fullscreenEffectMesh);
                fullscreenEffectMesh = null;
            }

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }

            captureCamera = null;
            panelObject = null;
            fullscreenEffectPanelObject = null;
            worldFixedPoseSet = false;
        }

        private void EnsureCreated()
        {
            if (root != null)
            {
                return;
            }

            root = new GameObject("SecretFlasherManakaVR UI Bridge");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;

            GameObject cameraObject = new GameObject("SecretFlasherManakaVR UI Capture Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(root.transform, false);
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.cullingMask = 1 << UnityUiLayer;
            captureCamera.orthographic = true;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = 100.0f;
            captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
            captureCamera.transform.position = new Vector3(0.0f, 0.0f, -10.0f);
            captureCamera.transform.rotation = Quaternion.identity;

            panelObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panelObject.name = "SecretFlasherManakaVR UI Texture Panel";
            panelObject.hideFlags = HideFlags.HideAndDontSave;
            panelObject.transform.SetParent(root.transform, false);
            panelObject.layer = VrUiOverlayLayer;
            Collider collider = panelObject.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            Renderer renderer = panelObject.GetComponent<Renderer>();
            panelMaterial = CreatePanelMaterial();
            renderer.sharedMaterial = panelMaterial;

            fullscreenEffectPanelObject = new GameObject("SecretFlasherManakaVR Fullscreen Effect Curved Panel");
            fullscreenEffectPanelObject.hideFlags = HideFlags.HideAndDontSave;
            fullscreenEffectPanelObject.transform.SetParent(root.transform, false);
            fullscreenEffectPanelObject.layer = VrUiOverlayLayer;
            MeshFilter meshFilter = fullscreenEffectPanelObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fullscreenEffectPanelObject.AddComponent<MeshRenderer>();
            fullscreenEffectPanelMaterial = CreatePanelMaterial();
            fullscreenEffectPanelMaterial.name = "SecretFlasherManakaVR Fullscreen Effect Panel Material";
            fullscreenEffectPanelMaterial.renderQueue = 4001;
            meshRenderer.sharedMaterial = fullscreenEffectPanelMaterial;
            fullscreenEffectMesh = new Mesh();
            fullscreenEffectMesh.name = "SecretFlasherManakaVR Fullscreen Effect Curved Mesh";
            meshFilter.sharedMesh = fullscreenEffectMesh;
            SetPanelVisible(false);
            SetFullscreenEffectPanelVisible(false);
        }

        private Material CreatePanelMaterial()
        {
            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }

            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("No compatible shader found for VR UI panel material.");
            }

            Material material = new Material(shader);
            material.name = "SecretFlasherManakaVR UI Panel Material";
            material.renderQueue = 4000;
            material.SetInt("_Cull", 0);
            return material;
        }

        private void UpdateAnchor(VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            Vector3 basePosition = headPosition;
            Quaternion hudBaseRotation = settings.IgnoreHeadRollForVrUi ? RemoveRoll(headRotation) : headRotation;
            Quaternion effectBaseRotation = headRotation;

            if (settings.VrUiFollowMode == VrUiFollowMode.SourceCameraLocked && sourceCamera != null)
            {
                basePosition = sourceCamera.transform.position;
                hudBaseRotation = sourceCamera.transform.rotation;
                effectBaseRotation = sourceCamera.transform.rotation;
            }
            else if (settings.VrUiFollowMode == VrUiFollowMode.WorldFixed)
            {
                if (!worldFixedPoseSet)
                {
                    worldFixedPosition = basePosition;
                    worldFixedRotation = hudBaseRotation;
                    worldFixedFullscreenEffectRotation = effectBaseRotation;
                    worldFixedPoseSet = true;
                }

                basePosition = worldFixedPosition;
                hudBaseRotation = worldFixedRotation;
                effectBaseRotation = worldFixedFullscreenEffectRotation;
            }

            Vector3 offset = (Vector3.forward * settings.VrUiDistance) + (Vector3.up * settings.VrUiVerticalOffset);
            uiPosition = basePosition + hudBaseRotation * offset;
            uiRotation = hudBaseRotation;
            fullscreenEffectPosition = basePosition + effectBaseRotation * offset;
            fullscreenEffectRotation = effectBaseRotation;

        }

        private static Quaternion RemoveRoll(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            if (forward.sqrMagnitude <= 0.000001f)
            {
                return Quaternion.identity;
            }

            forward.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude <= 0.000001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            }

            return up.sqrMagnitude <= 0.000001f
                ? Quaternion.LookRotation(forward)
                : Quaternion.LookRotation(forward, up.normalized);
        }

        private void ScanCanvasesIfNeeded(VrRuntimeSettings settings)
        {
            if (Time.unscaledTime < nextScanTime)
            {
                RemoveInactiveCanvases();
                return;
            }

            nextScanTime = Time.unscaledTime + settings.VrUiMaxScanInterval;
            activeCanvases.Clear();

            string[] whitelist = SplitKeywords(settings.VrUiCanvasNameWhitelist);
            string[] blacklist = SplitKeywords(settings.VrUiCanvasNameBlacklist);
            if (whitelist.Length == 0 && TryAddDefaultCapturedCanvas(blacklist))
            {
                SyncCapturedCanvasStates();
                ClearFullscreenEffectCacheIfInactive();
                return;
            }

            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            bool hasDefaultCanvas = whitelist.Length == 0 && HasDefaultCapturedCanvas(canvases, blacklist);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (ShouldCapture(canvas, whitelist, blacklist, hasDefaultCanvas))
                {
                    activeCanvases.Add(canvas);
                }
            }

            SyncCapturedCanvasStates();
            ClearFullscreenEffectCacheIfInactive();
        }

        private bool TryAddDefaultCapturedCanvas(string[] blacklist)
        {
            Canvas canvas = FindDefaultCapturedCanvasFromInGameManager(blacklist);
            if (canvas == null)
            {
                return false;
            }

            activeCanvases.Add(canvas);
            return true;
        }

        private Canvas FindDefaultCapturedCanvasFromInGameManager(string[] blacklist)
        {
            InGameUiManager manager = InGameUiManager.Instance;
            if (manager == null)
            {
                return null;
            }

            Canvas canvas = GetDefaultCapturedCanvas(manager, blacklist);
            if (canvas != null)
            {
                return canvas;
            }

            canvas = GetDefaultCapturedCanvas(manager.middleCloneLayer, blacklist);
            if (canvas != null)
            {
                return canvas;
            }

            canvas = GetDefaultCapturedCanvas(manager.frontLayer, blacklist);
            if (canvas != null)
            {
                return canvas;
            }

            canvas = GetDefaultCapturedCanvas(manager.BackLayer, blacklist);
            if (canvas != null)
            {
                return canvas;
            }

            return GetDefaultCapturedCanvas(manager.BlindVignette, blacklist);
        }

        private Canvas GetDefaultCapturedCanvas(Component component, string[] blacklist)
        {
            if (component == null)
            {
                return null;
            }

            Canvas canvas = component.GetComponent<Canvas>();
            if (IsDefaultCapturedCanvas(canvas, blacklist))
            {
                return canvas;
            }

            canvas = component.GetComponentInParent<Canvas>();
            return IsDefaultCapturedCanvas(canvas, blacklist) ? canvas : null;
        }

        private bool IsDefaultCapturedCanvas(Canvas canvas, string[] blacklist)
        {
            return IsCaptureCandidate(canvas, blacklist)
                && string.Equals(canvas.name ?? string.Empty, DefaultCapturedCanvasName, StringComparison.Ordinal);
        }

        private bool HasDefaultCapturedCanvas(Canvas[] canvases, string[] blacklist)
        {
            if (canvases == null)
            {
                return false;
            }

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (IsCaptureCandidate(canvas, blacklist)
                    && string.Equals(canvas.name ?? string.Empty, DefaultCapturedCanvasName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveInactiveCanvases()
        {
            for (int i = activeCanvases.Count - 1; i >= 0; i--)
            {
                Canvas canvas = activeCanvases[i];
                if (canvas == null || canvas.gameObject == null || !canvas.gameObject.activeInHierarchy || !canvas.enabled)
                {
                    RemoveCapturedCanvasState(canvas);
                    if (canvas == fullscreenEffectCanvas)
                    {
                        ClearFullscreenEffectCache();
                    }
                    activeCanvases.RemoveAt(i);
                }
            }
        }

        private void SyncCapturedCanvasStates()
        {
            for (int i = capturedCanvasStates.Count - 1; i >= 0; i--)
            {
                Canvas canvas = capturedCanvasStates[i].Canvas;
                if (canvas == null || !activeCanvases.Contains(canvas))
                {
                    capturedCanvasStates[i].Restore();
                    capturedCanvasStates.RemoveAt(i);
                }
            }

            for (int i = 0; i < activeCanvases.Count; i++)
            {
                EnsureCanvasCaptured(activeCanvases[i]);
            }
        }

        private void EnsureCanvasCaptured(Canvas canvas)
        {
            if (canvas == null || captureCamera == null)
            {
                return;
            }

            CanvasCaptureState state = GetCapturedCanvasState(canvas);
            if (state == null)
            {
                state = new CanvasCaptureState(canvas);
                capturedCanvasStates.Add(state);
            }

            state.Apply(captureCamera);
        }

        private CanvasCaptureState GetCapturedCanvasState(Canvas canvas)
        {
            for (int i = 0; i < capturedCanvasStates.Count; i++)
            {
                if (capturedCanvasStates[i].Canvas == canvas)
                {
                    return capturedCanvasStates[i];
                }
            }

            return null;
        }

        private void RemoveCapturedCanvasState(Canvas canvas)
        {
            for (int i = capturedCanvasStates.Count - 1; i >= 0; i--)
            {
                if (capturedCanvasStates[i].Canvas == canvas)
                {
                    capturedCanvasStates[i].Restore();
                    capturedCanvasStates.RemoveAt(i);
                }
            }
        }

        private void RestoreCapturedCanvases()
        {
            for (int i = capturedCanvasStates.Count - 1; i >= 0; i--)
            {
                capturedCanvasStates[i].Restore();
            }

            capturedCanvasStates.Clear();
        }

        private bool ShouldCapture(Canvas canvas, string[] whitelist, string[] blacklist, bool hasDefaultCanvas)
        {
            if (!IsCaptureCandidate(canvas, blacklist))
            {
                return false;
            }

            string name = canvas.name ?? string.Empty;
            if (whitelist.Length > 0)
            {
                return MatchesAny(name, whitelist);
            }

            if (hasDefaultCanvas)
            {
                return string.Equals(name, DefaultCapturedCanvasName, StringComparison.Ordinal);
            }

            return !IsUnityDebugCanvas(name);
        }

        private bool IsCaptureCandidate(Canvas canvas, string[] blacklist)
        {
            if (canvas == null || canvas.gameObject == null || !canvas.gameObject.activeInHierarchy || !canvas.enabled)
            {
                return false;
            }

            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                return false;
            }

            if (canvas.rootCanvas != canvas)
            {
                return false;
            }

            if (root != null && canvas.transform.IsChildOf(root.transform))
            {
                return false;
            }

            return !MatchesAny(canvas.name ?? string.Empty, blacklist);
        }

        private static bool IsUnityDebugCanvas(string name)
        {
            return string.Equals(name, "DebugUICanvas", StringComparison.Ordinal)
                || string.Equals(name, "DebugUIPersistentCanvas", StringComparison.Ordinal);
        }

        private void RenderUiToTexture(VrRuntimeSettings settings)
        {
            if (activeCanvases.Count == 0 || captureCamera == null)
            {
                SetPanelVisible(false);
                SetFullscreenEffectPanelVisible(false);
                eyeMaskOverlayVisible = false;
                return;
            }

            EnsureTexture();
            if (uiTexture == null)
            {
                SetPanelVisible(false);
                SetFullscreenEffectPanelVisible(false);
                eyeMaskOverlayVisible = false;
                return;
            }

            var hudLayout = new HudLayoutScope(settings);
            FullscreenEffectCaptureSet fullscreenEffects = null;
            try
            {
                captureCamera.targetTexture = uiTexture;
                captureCamera.aspect = textureWidth / (float)textureHeight;
                captureCamera.orthographicSize = textureHeight * 0.5f;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = Color.clear;
                captureCamera.cullingMask = 1 << UnityUiLayer;

                for (int i = 0; i < activeCanvases.Count; i++)
                {
                    Canvas canvas = activeCanvases[i];
                    if (canvas == null)
                    {
                        continue;
                    }

                    EnsureCanvasCaptured(canvas);
                    hudLayout.Apply(canvas);
                    if (fullscreenEffects == null && canvas.name == "InGameCanvas")
                    {
                        fullscreenEffects = GetFullscreenEffectCaptureSet(canvas);
                    }
                }

                RenderMainHudPass(fullscreenEffects);
                RenderFullscreenEffectPass(settings, fullscreenEffects);
                RenderEyeMaskOverlayPass(settings, fullscreenEffects);
                SetPanelVisible(true);
            }
            catch (Exception)
            {
                SetPanelVisible(false);
                SetFullscreenEffectPanelVisible(false);
                eyeMaskOverlayVisible = false;
            }
            finally
            {
                hudLayout.Restore();
            }
        }

        private void RenderMainHudPass(FullscreenEffectCaptureSet fullscreenEffects)
        {
            CanvasGroupAlphaScope hiddenEffects = null;
            try
            {
                if (fullscreenEffects != null)
                {
                    hiddenEffects = CanvasGroupAlphaScope.Hide(fullscreenEffects.MainHudHiddenObjects);
                }

                captureCamera.targetTexture = uiTexture;
                captureCamera.Render();
            }
            finally
            {
                if (hiddenEffects != null)
                {
                    hiddenEffects.Restore();
                }
            }
        }

        private void RenderFullscreenEffectPass(VrRuntimeSettings settings, FullscreenEffectCaptureSet fullscreenEffects)
        {
            if (settings == null || !settings.EnableVrFullscreenEffectLayer || fullscreenEffects == null || !fullscreenEffects.HasRenderableEffect)
            {
                SetFullscreenEffectPanelVisible(false);
                return;
            }

            EnsureFullscreenEffectTexture();
            if (fullscreenEffectTexture == null)
            {
                SetFullscreenEffectPanelVisible(false);
                return;
            }

            CanvasGroupAlphaScope isolatedEffects = null;
            GraphicColorScope boostedHeartBeatVignette = null;
            try
            {
                isolatedEffects = CanvasGroupAlphaScope.Hide(fullscreenEffects.HiddenBranches);
                boostedHeartBeatVignette = fullscreenEffects.BoostHeartBeatVignetteAlpha(settings.VrFullscreenEffectHeartBeatAlphaBoost);
                captureCamera.targetTexture = fullscreenEffectTexture;
                captureCamera.Render();
                if (fullscreenEffectPanelMaterial != null)
                {
                    fullscreenEffectPanelMaterial.mainTexture = fullscreenEffectTexture;
                }

                SetFullscreenEffectPanelVisible(true);
            }
            finally
            {
                if (boostedHeartBeatVignette != null)
                {
                    boostedHeartBeatVignette.Restore();
                }

                if (isolatedEffects != null)
                {
                    isolatedEffects.Restore();
                }

                captureCamera.targetTexture = uiTexture;
            }
        }

        private void RenderEyeMaskOverlayPass(VrRuntimeSettings settings, FullscreenEffectCaptureSet fullscreenEffects)
        {
            if (settings == null || !settings.EnableVrFullscreenEffectLayer || fullscreenEffects == null || !fullscreenEffects.HasRenderableEyeMask)
            {
                eyeMaskOverlayVisible = false;
                return;
            }

            EnsureEyeMaskTexture();
            if (eyeMaskTexture == null)
            {
                eyeMaskOverlayVisible = false;
                return;
            }

            CanvasGroupAlphaScope isolatedEyeMask = null;
            try
            {
                isolatedEyeMask = CanvasGroupAlphaScope.Hide(fullscreenEffects.EyeMaskHiddenBranches);
                captureCamera.targetTexture = eyeMaskTexture;
                captureCamera.Render();
                eyeMaskOverlayVisible = true;
            }
            finally
            {
                if (isolatedEyeMask != null)
                {
                    isolatedEyeMask.Restore();
                }

                captureCamera.targetTexture = uiTexture;
            }
        }

        private FullscreenEffectCaptureSet GetFullscreenEffectCaptureSet(Canvas canvas)
        {
            if (canvas == null || canvas.name != "InGameCanvas")
            {
                return null;
            }

            RectTransform rootTransform = canvas.GetComponent<RectTransform>();
            bool cacheInvalid = fullscreenEffectCache != null && !fullscreenEffectCache.IsValid;
            if (!fullscreenEffectCacheScanned || fullscreenEffectCanvas != canvas || fullscreenEffectRoot != rootTransform || cacheInvalid)
            {
                if (fullscreenEffectCache != null)
                {
                    fullscreenEffectCache.RestorePersistentChanges();
                }

                fullscreenEffectCanvas = canvas;
                fullscreenEffectRoot = rootTransform;
                fullscreenEffectCache = FullscreenEffectCaptureSet.FromCanvas(canvas, rootTransform);
                fullscreenEffectCacheScanned = true;
            }

            return fullscreenEffectCache;
        }

        private void ClearFullscreenEffectCache()
        {
            if (fullscreenEffectCache != null)
            {
                fullscreenEffectCache.RestorePersistentChanges();
            }

            fullscreenEffectCanvas = null;
            fullscreenEffectRoot = null;
            fullscreenEffectCache = null;
            fullscreenEffectCacheScanned = false;
        }

        private void ClearFullscreenEffectCacheIfInactive()
        {
            if (fullscreenEffectCanvas == null)
            {
                ClearFullscreenEffectCache();
                return;
            }

            for (int i = 0; i < activeCanvases.Count; i++)
            {
                if (activeCanvases[i] == fullscreenEffectCanvas)
                {
                    return;
                }
            }

            ClearFullscreenEffectCache();
        }

        private void EnsureTexture()
        {
            int width = Mathf.Clamp(Screen.width > 0 ? Screen.width : FallbackTextureWidth, 640, 4096);
            int height = Mathf.Clamp(Screen.height > 0 ? Screen.height : FallbackTextureHeight, 360, 4096);
            if (uiTexture != null && textureWidth == width && textureHeight == height)
            {
                return;
            }

            ReleaseTexture();
            textureWidth = width;
            textureHeight = height;
            uiTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            uiTexture.name = "SecretFlasherManakaVR UI Capture Texture";
            uiTexture.useMipMap = false;
            uiTexture.autoGenerateMips = false;
            uiTexture.Create();

            if (panelMaterial != null)
            {
                panelMaterial.mainTexture = uiTexture;
            }
        }

        private void EnsureFullscreenEffectTexture()
        {
            if (uiTexture == null)
            {
                return;
            }

            if (fullscreenEffectTexture != null && fullscreenEffectTexture.width == textureWidth && fullscreenEffectTexture.height == textureHeight)
            {
                return;
            }

            ReleaseFullscreenEffectTexture();
            fullscreenEffectTexture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32);
            fullscreenEffectTexture.name = "SecretFlasherManakaVR Fullscreen Effect Capture Texture";
            fullscreenEffectTexture.useMipMap = false;
            fullscreenEffectTexture.autoGenerateMips = false;
            fullscreenEffectTexture.Create();

            if (fullscreenEffectPanelMaterial != null)
            {
                fullscreenEffectPanelMaterial.mainTexture = fullscreenEffectTexture;
            }
        }

        private void EnsureEyeMaskTexture()
        {
            if (uiTexture == null)
            {
                return;
            }

            if (eyeMaskTexture != null && eyeMaskTexture.width == textureWidth && eyeMaskTexture.height == textureHeight)
            {
                return;
            }

            ReleaseEyeMaskTexture();
            eyeMaskTexture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32);
            eyeMaskTexture.name = "SecretFlasherManakaVR Eye Mask Capture Texture";
            eyeMaskTexture.useMipMap = false;
            eyeMaskTexture.autoGenerateMips = false;
            eyeMaskTexture.Create();
        }

        private void ReleaseTexture()
        {
            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
            }

            if (uiTexture != null)
            {
                uiTexture.Release();
                UnityEngine.Object.Destroy(uiTexture);
                uiTexture = null;
            }

            textureWidth = 0;
            textureHeight = 0;
        }

        private void ReleaseFullscreenEffectTexture()
        {
            if (captureCamera != null && captureCamera.targetTexture == fullscreenEffectTexture)
            {
                captureCamera.targetTexture = null;
            }

            if (fullscreenEffectTexture != null)
            {
                fullscreenEffectTexture.Release();
                UnityEngine.Object.Destroy(fullscreenEffectTexture);
                fullscreenEffectTexture = null;
            }
        }

        private void ReleaseEyeMaskTexture()
        {
            if (captureCamera != null && captureCamera.targetTexture == eyeMaskTexture)
            {
                captureCamera.targetTexture = null;
            }

            if (eyeMaskTexture != null)
            {
                eyeMaskTexture.Release();
                UnityEngine.Object.Destroy(eyeMaskTexture);
                eyeMaskTexture = null;
            }

            eyeMaskOverlayVisible = false;
        }

        private void ApplyPanelTransform(VrRuntimeSettings settings)
        {
            if (panelObject == null || uiTexture == null)
            {
                currentPanelVisible = false;
                return;
            }

            float aspect = textureHeight <= 0 ? 16.0f / 9.0f : textureWidth / (float)textureHeight;
            float panelScale = Mathf.Max(0.01f, settings.VrUiPanelScale);
            float width = Mathf.Max(0.25f, settings.VrUiDistance * 1.35f) * panelScale;
            float height = width / aspect;
            float pixelOffsetY = textureHeight <= 0 ? 0.0f : settings.VrUiPanelPixelOffsetY / textureHeight * height;
            Vector3 panelPosition = uiPosition + uiRotation * (Vector3.up * pixelOffsetY);
            panelObject.transform.SetPositionAndRotation(panelPosition, uiRotation);
            panelObject.transform.localScale = new Vector3(width, height, 1.0f);
            panelObject.layer = VrUiOverlayLayer;

            currentPanelPosition = panelPosition;
            currentPanelRotation = uiRotation;
            currentPanelScale = panelObject.transform.localScale;
            currentTextureWidth = textureWidth;
            currentTextureHeight = textureHeight;

            ApplyFullscreenEffectPanelTransform(settings, width, height, pixelOffsetY);
        }

        private void ApplyFullscreenEffectPanelTransform(VrRuntimeSettings settings, float baseWidth, float baseHeight, float pixelOffsetY)
        {
            if (fullscreenEffectPanelObject == null || fullscreenEffectTexture == null || settings == null)
            {
                return;
            }

            float scale = Mathf.Max(0.01f, settings.VrFullscreenEffectPanelScale);
            float width = baseWidth * scale;
            float height = baseHeight * scale;
            float depthOffset = settings.VrFullscreenEffectDepthOffset;
            Vector3 basePosition = fullscreenEffectPosition + fullscreenEffectRotation * (Vector3.up * pixelOffsetY);
            Vector3 position = basePosition + fullscreenEffectRotation * (Vector3.back * depthOffset);
            fullscreenEffectPanelObject.transform.SetPositionAndRotation(position, fullscreenEffectRotation);
            fullscreenEffectPanelObject.transform.localScale = new Vector3(width, height, 1.0f);
            fullscreenEffectPanelObject.layer = VrUiOverlayLayer;
            UpdateFullscreenEffectMesh(settings.VrFullscreenEffectCurveDegrees);
        }

        private void UpdateFullscreenEffectMesh(float curveDegrees)
        {
            if (fullscreenEffectMesh == null)
            {
                return;
            }

            int signature = Mathf.RoundToInt(curveDegrees * 100.0f);
            if (fullscreenEffectMeshSignature == signature && fullscreenEffectMesh.vertexCount > 0)
            {
                return;
            }

            fullscreenEffectMeshSignature = signature;
            const int columns = 32;
            const int rows = 1;
            Vector3[] vertices = new Vector3[(columns + 1) * (rows + 1)];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[columns * rows * 6];
            float halfAngle = Mathf.Deg2Rad * Mathf.Max(0.0f, curveDegrees) * 0.5f;
            float sinHalfAngle = Mathf.Sin(Mathf.Max(0.0001f, halfAngle));

            for (int y = 0; y <= rows; y++)
            {
                float v = y / (float)rows;
                float localY = v - 0.5f;
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns;
                    float normalizedX = u * 2.0f - 1.0f;
                    float localX;
                    float localZ;
                    if (halfAngle <= 0.0001f)
                    {
                        localX = normalizedX * 0.5f;
                        localZ = 0.0f;
                    }
                    else
                    {
                        float angle = normalizedX * halfAngle;
                        localX = Mathf.Sin(angle) / sinHalfAngle * 0.5f;
                        localZ = -(1.0f - Mathf.Cos(angle)) / Mathf.Max(0.0001f, 1.0f - Mathf.Cos(halfAngle)) * 0.12f;
                    }

                    int index = y * (columns + 1) + x;
                    vertices[index] = new Vector3(localX, localY, localZ);
                    uvs[index] = new Vector2(u, v);
                }
            }

            int triangleIndex = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int a = y * (columns + 1) + x;
                    int b = a + 1;
                    int c = a + columns + 1;
                    int d = c + 1;
                    triangles[triangleIndex++] = a;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = d;
                }
            }

            fullscreenEffectMesh.Clear();
            fullscreenEffectMesh.vertices = vertices;
            fullscreenEffectMesh.uv = uvs;
            fullscreenEffectMesh.triangles = triangles;
            fullscreenEffectMesh.RecalculateBounds();
            fullscreenEffectMesh.RecalculateNormals();
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelObject != null && panelObject.activeSelf != visible)
            {
                panelObject.SetActive(visible);
            }

            currentPanelVisible = visible && panelObject != null && textureWidth > 0 && textureHeight > 0;
        }

        private void SetFullscreenEffectPanelVisible(bool visible)
        {
            if (fullscreenEffectPanelObject != null && fullscreenEffectPanelObject.activeSelf != visible)
            {
                fullscreenEffectPanelObject.SetActive(visible);
            }
        }

        private static string[] SplitKeywords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            string[] raw = value.Split(',');
            var result = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string keyword = raw[i].Trim();
                if (keyword.Length > 0)
                {
                    result.Add(keyword);
                }
            }

            return result.ToArray();
        }

        private static bool MatchesAny(string name, string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (name.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private enum HudRtKind
        {
            Face,
            Body
        }

        private sealed class FullscreenEffectCaptureSet
        {
            private const float HeartBeatPanelScaleX = 0.7f;
            private const float HeartBeatPanelScaleY = 0.9f;

            private readonly List<GameObject> effectObjects = new List<GameObject>();
            private readonly List<GameObject> eyeMaskObjects = new List<GameObject>();
            private readonly List<GameObject> mainHudHiddenObjects = new List<GameObject>();
            private readonly List<GameObject> hiddenBranches = new List<GameObject>();
            private readonly List<GameObject> eyeMaskHiddenBranches = new List<GameObject>();
            private readonly HashSet<int> effectObjectIds = new HashSet<int>();
            private readonly HashSet<int> eyeMaskObjectIds = new HashSet<int>();
            private readonly HashSet<int> mainHudHiddenObjectIds = new HashSet<int>();
            private readonly HashSet<int> hiddenBranchIds = new HashSet<int>();
            private readonly HashSet<int> eyeMaskHiddenBranchIds = new HashSet<int>();
            private readonly HashSet<int> keptTransformIds = new HashSet<int>();
            private TransformScaleState heartBeatPanelScaleState;

            private FullscreenEffectCaptureSet(RectTransform root)
            {
                Root = root;
            }

            public RectTransform Root { get; }

            public List<GameObject> MainHudHiddenObjects
            {
                get { return mainHudHiddenObjects; }
            }

            public List<GameObject> HiddenBranches
            {
                get { return hiddenBranches; }
            }

            public List<GameObject> EyeMaskHiddenBranches
            {
                get { return eyeMaskHiddenBranches; }
            }

            public bool IsValid
            {
                get
                {
                    if (Root == null || Root.gameObject == null)
                    {
                        return false;
                    }

                    for (int i = effectObjects.Count - 1; i >= 0; i--)
                    {
                        GameObject effect = effectObjects[i];
                        if (effect == null || effect.transform == null || !effect.transform.IsChildOf(Root.transform))
                        {
                            return false;
                        }
                    }

                    for (int i = eyeMaskObjects.Count - 1; i >= 0; i--)
                    {
                        GameObject eyeMask = eyeMaskObjects[i];
                        if (eyeMask == null || eyeMask.transform == null || !eyeMask.transform.IsChildOf(Root.transform))
                        {
                            return false;
                        }
                    }

                    for (int i = mainHudHiddenObjects.Count - 1; i >= 0; i--)
                    {
                        GameObject hidden = mainHudHiddenObjects[i];
                        if (hidden == null || hidden.transform == null || !hidden.transform.IsChildOf(Root.transform))
                        {
                            return false;
                        }
                    }

                    for (int i = hiddenBranches.Count - 1; i >= 0; i--)
                    {
                        GameObject branch = hiddenBranches[i];
                        if (branch == null || branch.transform == null || !branch.transform.IsChildOf(Root.transform))
                        {
                            return false;
                        }
                    }

                    for (int i = eyeMaskHiddenBranches.Count - 1; i >= 0; i--)
                    {
                        GameObject branch = eyeMaskHiddenBranches[i];
                        if (branch == null || branch.transform == null || !branch.transform.IsChildOf(Root.transform))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            public bool HasRenderableEffect
            {
                get
                {
                    for (int i = 0; i < effectObjects.Count; i++)
                    {
                        GameObject effect = effectObjects[i];
                        if (effect != null && effect.activeInHierarchy && !IsCanvasGroupChainFullyTransparent(effect.transform))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            public bool HasRenderableEyeMask
            {
                get
                {
                    for (int i = 0; i < eyeMaskObjects.Count; i++)
                    {
                        GameObject eyeMask = eyeMaskObjects[i];
                        if (eyeMask != null && eyeMask.activeInHierarchy && !IsCanvasGroupChainFullyTransparent(eyeMask.transform))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            public static FullscreenEffectCaptureSet FromCanvas(Canvas canvas, RectTransform root)
            {
                if (canvas == null || canvas.name != "InGameCanvas")
                {
                    return null;
                }

                if (root == null)
                {
                    return null;
                }

                var set = new FullscreenEffectCaptureSet(root);
                InGameUiManager manager = canvas.GetComponentInParent<InGameUiManager>();
                if (manager != null)
                {
                    set.AddEyeMaskComponent(manager.BlindVignette);
                    set.AddComponent(manager.invisibleVignette);
                    if (manager.heartRatePanelView != null && manager.heartRatePanelView.vignetteCanvas != null)
                    {
                        set.Add(manager.heartRatePanelView.vignetteCanvas.gameObject);
                    }
                }

                set.AddEyeMaskPath("BackLayer/BlindVignette");
                set.AddPath("MiddleLayer/InvisibleVignette");
                set.AddPath("MiddleLayer/InvisibleVignette/InvisibleVignette");
                set.AddPath("MiddleLayer/HeartBeatPanel/Vignette");
                set.AddPath("MiddleLayer/SlowAssistPanel/MainContents/Vignette");
                if (set.effectObjects.Count == 0 && set.eyeMaskObjects.Count == 0 && set.mainHudHiddenObjects.Count == 0)
                {
                    return null;
                }

                set.ApplyHeartBeatPanelScale();
                set.CacheHiddenBranches();
                set.CacheEyeMaskHiddenBranches();
                return set;
            }

            public void RestorePersistentChanges()
            {
                if (heartBeatPanelScaleState != null)
                {
                    heartBeatPanelScaleState.Restore();
                    heartBeatPanelScaleState = null;
                }
            }

            private void AddPath(string path)
            {
                if (Root == null)
                {
                    return;
                }

                Transform target = Root.Find(path);
                if (target != null)
                {
                    Add(target.gameObject);
                }
            }

            private void Add(GameObject gameObject)
            {
                if (gameObject == null || Root == null || !gameObject.transform.IsChildOf(Root.transform))
                {
                    return;
                }

                int id = gameObject.GetInstanceID();
                if (effectObjectIds.Contains(id))
                {
                    return;
                }

                effectObjectIds.Add(id);
                effectObjects.Add(gameObject);
                AddMainHudHidden(gameObject);
            }

            private void AddEyeMaskPath(string path)
            {
                if (Root == null)
                {
                    return;
                }

                Transform target = Root.Find(path);
                if (target != null)
                {
                    AddEyeMask(target.gameObject);
                }
            }

            private void AddEyeMask(GameObject gameObject)
            {
                if (gameObject == null || Root == null || !gameObject.transform.IsChildOf(Root.transform))
                {
                    return;
                }

                int id = gameObject.GetInstanceID();
                if (eyeMaskObjectIds.Contains(id))
                {
                    return;
                }

                eyeMaskObjectIds.Add(id);
                eyeMaskObjects.Add(gameObject);
                AddMainHudHidden(gameObject);
            }

            private void AddEyeMaskComponent(Component component)
            {
                if (component != null)
                {
                    AddEyeMask(component.gameObject);
                }
            }

            private void AddComponent(Component component)
            {
                if (component != null)
                {
                    Add(component.gameObject);
                }
            }

            private void AddMainHudHidden(GameObject gameObject)
            {
                if (gameObject == null || Root == null || !gameObject.transform.IsChildOf(Root.transform))
                {
                    return;
                }

                int id = gameObject.GetInstanceID();
                if (mainHudHiddenObjectIds.Contains(id))
                {
                    return;
                }

                mainHudHiddenObjectIds.Add(id);
                mainHudHiddenObjects.Add(gameObject);
            }

            private void ApplyHeartBeatPanelScale()
            {
                if (Root == null || heartBeatPanelScaleState != null)
                {
                    return;
                }

                Transform target = Root.Find("MiddleLayer/HeartBeatPanel");
                if (target == null)
                {
                    return;
                }

                heartBeatPanelScaleState = new TransformScaleState(target);
                Vector3 scale = target.localScale;
                target.localScale = new Vector3(HeartBeatPanelScaleX, HeartBeatPanelScaleY, scale.z);
            }

            private void CacheHiddenBranches()
            {
                CacheHiddenBranchesFor(effectObjects, hiddenBranches, hiddenBranchIds);
            }

            private void CacheEyeMaskHiddenBranches()
            {
                CacheHiddenBranchesFor(eyeMaskObjects, eyeMaskHiddenBranches, eyeMaskHiddenBranchIds);
            }

            private void CacheHiddenBranchesFor(List<GameObject> keptObjects, List<GameObject> targetBranches, HashSet<int> targetBranchIds)
            {
                keptTransformIds.Clear();
                targetBranches.Clear();
                targetBranchIds.Clear();

                if (Root == null || keptObjects == null)
                {
                    return;
                }

                for (int i = 0; i < keptObjects.Count; i++)
                {
                    GameObject keptObject = keptObjects[i];
                    if (keptObject == null)
                    {
                        continue;
                    }

                    Transform current = keptObject.transform;
                    while (current != null && current != Root.transform)
                    {
                        keptTransformIds.Add(current.GetInstanceID());
                        current = current.parent;
                    }
                }

                var keptAncestors = new List<Transform>();
                for (int i = 0; i < keptObjects.Count; i++)
                {
                    AddKeptAncestors(keptObjects[i], keptAncestors);
                }

                for (int i = 0; i < keptAncestors.Count; i++)
                {
                    AddHiddenSiblings(keptAncestors[i], targetBranches, targetBranchIds);
                }
            }

            private void AddKeptAncestors(GameObject effect, List<Transform> keptAncestors)
            {
                if (effect == null || Root == null)
                {
                    return;
                }

                Transform current = effect.transform.parent;
                while (current != null && current != Root.transform)
                {
                    AddUniqueTransform(keptAncestors, current);
                    current = current.parent;
                }

                AddUniqueTransform(keptAncestors, Root.transform);
            }

            private static void AddUniqueTransform(List<Transform> transforms, Transform transform)
            {
                if (transform == null)
                {
                    return;
                }

                for (int i = 0; i < transforms.Count; i++)
                {
                    if (transforms[i] == transform)
                    {
                        return;
                    }
                }

                transforms.Add(transform);
            }

            private void AddHiddenSiblings(Transform parent, List<GameObject> targetBranches, HashSet<int> targetBranchIds)
            {
                if (parent == null)
                {
                    return;
                }

                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child == null || keptTransformIds.Contains(child.GetInstanceID()))
                    {
                        continue;
                    }

                    AddHiddenBranch(child.gameObject, targetBranches, targetBranchIds);
                }
            }

            private static void AddHiddenBranch(GameObject gameObject, List<GameObject> targetBranches, HashSet<int> targetBranchIds)
            {
                if (gameObject == null)
                {
                    return;
                }

                int id = gameObject.GetInstanceID();
                if (targetBranchIds.Contains(id))
                {
                    return;
                }

                targetBranchIds.Add(id);
                targetBranches.Add(gameObject);
            }

            public GraphicColorScope BoostHeartBeatVignetteAlpha(float boost)
            {
                if (boost <= 1.001f || Root == null)
                {
                    return null;
                }

                Transform target = Root.Find("MiddleLayer/HeartBeatPanel/Vignette");
                if (target == null || !target.gameObject.activeInHierarchy)
                {
                    return null;
                }

                Graphic graphic = target.GetComponent<Graphic>();
                if (graphic == null || !graphic.enabled)
                {
                    return null;
                }

                var scope = new GraphicColorScope();
                scope.BoostAlpha(graphic, boost);
                return scope;
            }

            private static bool IsCanvasGroupChainFullyTransparent(Transform transform)
            {
                Transform current = transform;
                while (current != null)
                {
                    CanvasGroup group = current.GetComponent<CanvasGroup>();
                    if (group != null && group.alpha <= 0.001f)
                    {
                        return true;
                    }

                    current = current.parent;
                }

                return false;
            }

            private sealed class TransformScaleState
            {
                private readonly Transform transform;
                private readonly Vector3 localScale;

                public TransformScaleState(Transform transform)
                {
                    this.transform = transform;
                    localScale = transform.localScale;
                }

                public void Restore()
                {
                    if (transform != null)
                    {
                        transform.localScale = localScale;
                    }
                }
            }
        }

        private sealed class CanvasGroupAlphaScope
        {
            private readonly List<CanvasGroupState> states = new List<CanvasGroupState>();
            private readonly HashSet<int> savedGroups = new HashSet<int>();

            public static CanvasGroupAlphaScope Hide(List<GameObject> objects)
            {
                var scope = new CanvasGroupAlphaScope();
                if (objects == null)
                {
                    return scope;
                }

                for (int i = 0; i < objects.Count; i++)
                {
                    scope.SetAlpha(objects[i], 0.0f);
                }

                return scope;
            }

            public void Restore()
            {
                for (int i = states.Count - 1; i >= 0; i--)
                {
                    states[i].Restore();
                }

                states.Clear();
                savedGroups.Clear();
            }

            public void SetAlpha(GameObject gameObject, float alpha)
            {
                if (gameObject == null)
                {
                    return;
                }

                CanvasGroup group = GetOrAddCanvasGroup(gameObject);
                if (group == null)
                {
                    return;
                }

                int id = group.GetInstanceID();
                if (!savedGroups.Contains(id))
                {
                    savedGroups.Add(id);
                    states.Add(new CanvasGroupState(group));
                }

                group.alpha = alpha;
            }

            internal static CanvasGroup GetOrAddCanvasGroup(GameObject gameObject)
            {
                if (gameObject == null)
                {
                    return null;
                }

                CanvasGroup group = gameObject.GetComponent<CanvasGroup>();
                return group != null ? group : gameObject.AddComponent<CanvasGroup>();
            }

            private readonly struct CanvasGroupState
            {
                private readonly CanvasGroup group;
                private readonly float alpha;

                public CanvasGroupState(CanvasGroup group)
                {
                    this.group = group;
                    alpha = group.alpha;
                }

                public void Restore()
                {
                    if (group != null)
                    {
                        group.alpha = alpha;
                    }
                }
            }
        }

        private sealed class GraphicColorScope
        {
            private readonly List<GraphicColorState> states = new List<GraphicColorState>();
            private readonly HashSet<int> savedGraphics = new HashSet<int>();

            public void BoostAlpha(Graphic graphic, float boost)
            {
                if (graphic == null || boost <= 1.001f)
                {
                    return;
                }

                int id = graphic.GetInstanceID();
                if (!savedGraphics.Contains(id))
                {
                    savedGraphics.Add(id);
                    states.Add(new GraphicColorState(graphic));
                }

                Color color = graphic.color;
                color.a = Mathf.Clamp01(color.a * boost);
                graphic.color = color;
            }

            public void Restore()
            {
                for (int i = states.Count - 1; i >= 0; i--)
                {
                    states[i].Restore();
                }

                states.Clear();
                savedGraphics.Clear();
            }

            private readonly struct GraphicColorState
            {
                private readonly Graphic graphic;
                private readonly Color color;

                public GraphicColorState(Graphic graphic)
                {
                    this.graphic = graphic;
                    color = graphic.color;
                }

                public void Restore()
                {
                    if (graphic != null)
                    {
                        graphic.color = color;
                    }
                }
            }
        }

        private sealed class HudLayoutScope
        {
            private const float HudReferenceWidth = 2560.0f;
            private const float HudReferenceHeight = 1440.0f;

            private readonly VrRuntimeSettings settings;
            private readonly List<RectState> rectStates = new List<RectState>();
            private readonly List<ActiveState> activeStates = new List<ActiveState>();
            private readonly List<ParentState> parentStates = new List<ParentState>();
            private readonly List<CanvasGroupState> canvasGroupStates = new List<CanvasGroupState>();
            private readonly List<GraphicEnabledState> graphicEnabledStates = new List<GraphicEnabledState>();
            private readonly HashSet<int> savedRects = new HashSet<int>();
            private readonly HashSet<int> savedObjects = new HashSet<int>();
            private readonly HashSet<int> savedParents = new HashSet<int>();
            private readonly HashSet<int> savedCanvasGroups = new HashSet<int>();
            private readonly HashSet<int> savedGraphics = new HashSet<int>();

            public HudLayoutScope(VrRuntimeSettings settings)
            {
                this.settings = settings;
            }

            public void Apply(Canvas canvas)
            {
                if (canvas == null || canvas.name != "InGameCanvas")
                {
                    return;
                }

                RectTransform root = canvas.GetComponent<RectTransform>();
                if (root == null)
                {
                    return;
                }

                InGameUiManager manager = canvas.GetComponentInParent<InGameUiManager>();
                if (manager != null)
                {
                    UiOptionApplyer optionApplyer = canvas.GetComponentInParent<UiOptionApplyer>();
                    bool? playerInfoVisible = GetActiveInHierarchy(optionApplyer == null ? null : optionApplyer.playerInfoPanel);
                    bool? heartRateVisible = GetRenderableInHierarchy(root, "MiddleLayer/HeartBeatPanel/HeartRateInfoPanel")
                        ?? GetActiveInHierarchy(optionApplyer == null ? null : optionApplyer.heartRatePanel)
                        ?? playerInfoVisible;
                    bool? ecstasyGaugeVisible = GetActiveInHierarchy(optionApplyer == null ? null : optionApplyer.ecstasyGauge);

                    HideObject(manager.shortcutSlotParent);
                    HideObject(manager.shortcutSlotParent2);
                    HideObject(manager.sexManualParent);
                    HideObject(manager.nomalManualParent);
                    HideComponent(manager.ingameUiManualView);
                    HideComponent(manager.ecstasyHeartIcon);

                    RectTransform? staminaGauge = MovePathToRoot(root, "MiddleLayer/PlayerInfo/StaminaGauge", ReferencePointToRoot(root, 1135.0f, 1315.0f), null, 1.0f, 0.0f, true);
                    RectTransform? moistureIcon = MovePathToRoot(root, "MiddleLayer/PlayerInfo/MoistureIcon", ReferencePointToRoot(root, 980.0f, 1327.0f), null, 0.72f, 0.0f, true);
                    RectTransform? ecstasyGauge = MoveComponentToRoot(root, manager.ecstasyGauge, ReferencePointToRoot(root, 700.0f, 1300.0f), null, 1.65f, 0.0f);
                    RectTransform? heartRateInfoPanel = MovePathToRoot(root, "MiddleLayer/HeartBeatPanel/HeartRateInfoPanel", ReferencePointToRoot(root, 1495.0f, 1326.0f), null, 0.46f, 0.0f, true);
                    SetTemporaryRenderable(staminaGauge, heartRateVisible);
                    SetTemporaryRenderable(moistureIcon, heartRateVisible);
                    SetTemporaryRenderable(heartRateInfoPanel, heartRateVisible);
                    SetTemporaryRenderable(ecstasyGauge, ecstasyGaugeVisible);
                    ApplySelfCameraRt(manager.faceCameraImage, HudRtKind.Face);
                    ApplySelfCameraRt(manager.bodyCameraImage, HudRtKind.Body);
                    ShiftPath(root, "MiddleLayer/Right/StatusInfo", GetStatusInfoOffset());
                }
                else
                {
                    HideIfNameContains(root, "Shortcut");
                    HideIfNameContains(root, "Manual");
                    HideIfNameContains(root, "EcstasyHeart");
                }
            }

            private static Vector2 ReferencePointToRoot(RectTransform root, float referenceX, float referenceY)
            {
                Rect rect = root.rect;
                float width = rect.width > 0.0f ? rect.width : FallbackTextureWidth;
                float height = rect.height > 0.0f ? rect.height : FallbackTextureHeight;
                return new Vector2(referenceX * width / HudReferenceWidth, -referenceY * height / HudReferenceHeight);
            }

            private void ApplySelfCameraRt(RawImage image, HudRtKind kind)
            {
                if (image == null || image.gameObject == null || !image.gameObject.activeInHierarchy || !image.enabled)
                {
                    return;
                }

                RectTransform rect = image.rectTransform;
                if (rect == null)
                {
                    return;
                }

                SaveRect(rect);
                Vector2 configOffset = GetConfiguredOffset(kind);
                float configScale = GetConfiguredScale(kind);
                rect.anchoredPosition += configOffset;
                rect.localScale = ScaleVector(rect.localScale, Mathf.Max(0.1f, configScale));
            }

            private void ShiftPath(RectTransform root, string path, Vector2 offset)
            {
                if (root == null)
                {
                    return;
                }

                Transform target = root.Find(path);
                RectTransform rect = target == null ? null : target.GetComponent<RectTransform>();
                if (rect == null)
                {
                    return;
                }

                SaveRect(rect);
                rect.anchoredPosition += offset;
            }

            private Vector2 GetStatusInfoOffset()
            {
                if (settings == null)
                {
                    return new Vector2(-150.0f, 0.0f);
                }

                return new Vector2(settings.VrUiStatusInfoOffsetX, settings.VrUiStatusInfoOffsetY);
            }

            private Vector2 GetConfiguredOffset(HudRtKind kind)
            {
                if (settings == null)
                {
                    return Vector2.zero;
                }

                return kind == HudRtKind.Face
                    ? new Vector2(settings.VrUiFaceRtOffsetX, settings.VrUiFaceRtOffsetY)
                    : new Vector2(settings.VrUiBodyRtOffsetX, settings.VrUiBodyRtOffsetY);
            }

            private float GetConfiguredScale(HudRtKind kind)
            {
                if (settings == null)
                {
                    return 1.0f;
                }

                return kind == HudRtKind.Face ? settings.VrUiFaceRtScale : settings.VrUiBodyRtScale;
            }

            private static Vector3 ScaleVector(Vector3 value, float scale)
            {
                return new Vector3(value.x * scale, value.y * scale, value.z);
            }

            public void Restore()
            {
                for (int i = graphicEnabledStates.Count - 1; i >= 0; i--)
                {
                    graphicEnabledStates[i].Restore();
                }

                for (int i = canvasGroupStates.Count - 1; i >= 0; i--)
                {
                    canvasGroupStates[i].Restore();
                }

                for (int i = parentStates.Count - 1; i >= 0; i--)
                {
                    parentStates[i].Restore();
                }

                for (int i = rectStates.Count - 1; i >= 0; i--)
                {
                    rectStates[i].Restore();
                }

                for (int i = activeStates.Count - 1; i >= 0; i--)
                {
                    activeStates[i].Restore();
                }

                rectStates.Clear();
                activeStates.Clear();
                parentStates.Clear();
                canvasGroupStates.Clear();
                graphicEnabledStates.Clear();
                savedRects.Clear();
                savedObjects.Clear();
                savedParents.Clear();
                savedCanvasGroups.Clear();
                savedGraphics.Clear();
            }

            private void HidePath(RectTransform root, string path)
            {
                Transform target = root.Find(path);
                if (target != null)
                {
                    HideObject(target.gameObject);
                }
            }

            private void HideComponent(Component component)
            {
                if (component != null)
                {
                    HideObject(component.gameObject);
                }
            }

            private void HideObject(GameObject gameObject)
            {
                if (gameObject == null)
                {
                    return;
                }

                SaveActive(gameObject);
                gameObject.SetActive(false);
            }

            private static bool? GetActiveInHierarchy(GameObject? gameObject)
            {
                return gameObject == null ? (bool?)null : gameObject.activeInHierarchy;
            }

            private static bool? GetRenderableInHierarchy(RectTransform root, string path)
            {
                if (root == null)
                {
                    return null;
                }

                Transform target = root.Find(path);
                if (target == null || target.gameObject == null)
                {
                    return null;
                }

                return target.gameObject.activeInHierarchy && !IsCanvasGroupChainTransparent(target);
            }

            private static bool IsCanvasGroupChainTransparent(Transform transform)
            {
                Transform current = transform;
                while (current != null)
                {
                    CanvasGroup group = current.GetComponent<CanvasGroup>();
                    if (group != null && group.alpha <= 0.001f)
                    {
                        return true;
                    }

                    current = current.parent;
                }

                return false;
            }

            private void SetTemporaryRenderable(Component? component, bool? visible)
            {
                if (component == null || !visible.HasValue)
                {
                    return;
                }

                SetTemporaryActive(component.gameObject, visible.Value);
                if (!visible.Value)
                {
                    SetTemporaryAlpha(component.gameObject, 0.0f);
                    SetTemporaryGraphicsEnabled(component.gameObject, false);
                }
            }

            private void SetTemporaryActive(Component? component, bool? active)
            {
                if (component == null || !active.HasValue)
                {
                    return;
                }

                SetTemporaryActive(component.gameObject, active.Value);
            }

            private void SetTemporaryActive(GameObject? gameObject, bool active)
            {
                if (gameObject == null || gameObject.activeSelf == active)
                {
                    return;
                }

                SaveActive(gameObject);
                gameObject.SetActive(active);
            }

            private void SetTemporaryAlpha(GameObject? gameObject, float alpha)
            {
                if (gameObject == null)
                {
                    return;
                }

                CanvasGroup group = CanvasGroupAlphaScope.GetOrAddCanvasGroup(gameObject);
                if (group == null)
                {
                    return;
                }

                int id = group.GetInstanceID();
                if (!savedCanvasGroups.Contains(id))
                {
                    savedCanvasGroups.Add(id);
                    canvasGroupStates.Add(new CanvasGroupState(group));
                }

                group.alpha = alpha;
            }

            private void SetTemporaryGraphicsEnabled(GameObject? gameObject, bool enabled)
            {
                if (gameObject == null)
                {
                    return;
                }

                Graphic[] graphics = gameObject.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    Graphic graphic = graphics[i];
                    if (graphic == null)
                    {
                        continue;
                    }

                    int id = graphic.GetInstanceID();
                    if (!savedGraphics.Contains(id))
                    {
                        savedGraphics.Add(id);
                        graphicEnabledStates.Add(new GraphicEnabledState(graphic));
                    }

                    graphic.enabled = enabled;
                }
            }

            private RectTransform? MoveComponentToRoot(RectTransform root, Component component, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation)
            {
                if (root == null || component == null)
                {
                    return null;
                }

                RectTransform rect = component.GetComponent<RectTransform>();
                if (rect == null)
                {
                    rect = component.transform as RectTransform;
                }

                if (rect == null)
                {
                    return null;
                }

                SaveRect(rect);
                SaveParent(rect);
                rect.SetParent(root, false);
                MoveRect(rect, anchoredPosition, sizeDelta, scale, zRotation);
                return rect;
            }

            private RectTransform? MovePathToRoot(RectTransform root, string path, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation)
            {
                return MovePathToRoot(root, path, anchoredPosition, sizeDelta, scale, zRotation, false);
            }

            private RectTransform? MovePathToRoot(RectTransform root, string path, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation, bool preservePivot)
            {
                if (root == null)
                {
                    return null;
                }

                Transform target = root.Find(path);
                RectTransform rect = target == null ? null : target.GetComponent<RectTransform>();
                if (rect == null)
                {
                    return null;
                }

                SaveRect(rect);
                SaveParent(rect);
                rect.SetParent(root, false);
                MoveRect(rect, anchoredPosition, sizeDelta, scale, zRotation, preservePivot);
                return rect;
            }

            private void HideIfNameContains(RectTransform root, string keyword)
            {
                RectTransform[] children = root.GetComponentsInChildren<RectTransform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    RectTransform child = children[i];
                    if (child == null || child == root || child.gameObject == null)
                    {
                        continue;
                    }

                    string name = child.gameObject.name ?? string.Empty;
                    if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    HideObject(child.gameObject);
                }
            }

            private void MovePath(RectTransform root, string path, Vector2 anchoredPosition, Vector2? sizeDelta, float scale)
            {
                Transform target = root.Find(path);
                RectTransform rect = target == null ? null : target.GetComponent<RectTransform>();
                if (rect == null)
                {
                    return;
                }

                MoveRect(rect, anchoredPosition, sizeDelta, scale, 0.0f, false);
            }

            private void MoveRect(RectTransform rect, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation)
            {
                MoveRect(rect, anchoredPosition, sizeDelta, scale, zRotation, false);
            }

            private void MoveRect(RectTransform rect, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation, bool preservePivot)
            {
                if (rect == null)
                {
                    return;
                }

                SaveRect(rect);
                rect.anchorMin = new Vector2(0.0f, 1.0f);
                rect.anchorMax = new Vector2(0.0f, 1.0f);
                if (!preservePivot)
                {
                    rect.pivot = new Vector2(0.0f, 1.0f);
                }

                rect.anchoredPosition = anchoredPosition;
                rect.localRotation = Quaternion.Euler(0.0f, 0.0f, zRotation);
                if (scale > 0.0f)
                {
                    rect.localScale = Vector3.one * scale;
                }

                if (sizeDelta.HasValue)
                {
                    rect.sizeDelta = sizeDelta.Value;
                }
            }

            private void SaveRect(RectTransform rect)
            {
                int id = rect.GetInstanceID();
                if (savedRects.Contains(id))
                {
                    return;
                }

                savedRects.Add(id);
                rectStates.Add(new RectState(rect));
            }

            private void SaveParent(RectTransform rect)
            {
                int id = rect.GetInstanceID();
                if (savedParents.Contains(id))
                {
                    return;
                }

                savedParents.Add(id);
                parentStates.Add(new ParentState(rect));
            }

            private void SaveActive(GameObject gameObject)
            {
                if (gameObject == null)
                {
                    return;
                }

                int id = gameObject.GetInstanceID();
                if (savedObjects.Contains(id))
                {
                    return;
                }

                savedObjects.Add(id);
                activeStates.Add(new ActiveState(gameObject));
            }

            private readonly struct RectState
            {
                private readonly RectTransform rect;
                private readonly Vector2 anchorMin;
                private readonly Vector2 anchorMax;
                private readonly Vector2 pivot;
                private readonly Vector2 anchoredPosition;
                private readonly Vector2 sizeDelta;
                private readonly Vector3 localScale;
                private readonly Quaternion localRotation;

                public RectState(RectTransform rect)
                {
                    this.rect = rect;
                    anchorMin = rect.anchorMin;
                    anchorMax = rect.anchorMax;
                    pivot = rect.pivot;
                    anchoredPosition = rect.anchoredPosition;
                    sizeDelta = rect.sizeDelta;
                    localScale = rect.localScale;
                    localRotation = rect.localRotation;
                }

                public void Restore()
                {
                    if (rect == null)
                    {
                        return;
                    }

                    rect.anchorMin = anchorMin;
                    rect.anchorMax = anchorMax;
                    rect.pivot = pivot;
                    rect.anchoredPosition = anchoredPosition;
                    rect.sizeDelta = sizeDelta;
                    rect.localScale = localScale;
                    rect.localRotation = localRotation;
                }
            }

            private readonly struct ParentState
            {
                private readonly RectTransform rect;
                private readonly Transform parent;
                private readonly int siblingIndex;

                public ParentState(RectTransform rect)
                {
                    this.rect = rect;
                    parent = rect.parent;
                    siblingIndex = rect.GetSiblingIndex();
                }

                public void Restore()
                {
                    if (rect == null || parent == null)
                    {
                        return;
                    }

                    rect.SetParent(parent, false);
                    rect.SetSiblingIndex(siblingIndex);
                }
            }

            private readonly struct ActiveState
            {
                private readonly GameObject gameObject;
                private readonly bool activeSelf;

                public ActiveState(GameObject gameObject)
                {
                    this.gameObject = gameObject;
                    activeSelf = gameObject.activeSelf;
                }

                public void Restore()
                {
                    if (gameObject != null)
                    {
                        gameObject.SetActive(activeSelf);
                    }
                }
            }

            private readonly struct CanvasGroupState
            {
                private readonly CanvasGroup group;
                private readonly float alpha;

                public CanvasGroupState(CanvasGroup group)
                {
                    this.group = group;
                    alpha = group.alpha;
                }

                public void Restore()
                {
                    if (group != null)
                    {
                        group.alpha = alpha;
                    }
                }
            }

            private readonly struct GraphicEnabledState
            {
                private readonly Graphic graphic;
                private readonly bool enabled;

                public GraphicEnabledState(Graphic graphic)
                {
                    this.graphic = graphic;
                    enabled = graphic.enabled;
                }

                public void Restore()
                {
                    if (graphic != null)
                    {
                        graphic.enabled = enabled;
                    }
                }
            }
        }

        private sealed class CanvasCaptureState
        {
            private readonly Canvas canvas;
            private readonly RenderMode renderMode;
            private readonly Camera worldCamera;
            private readonly float planeDistance;
            private bool applied;

            public CanvasCaptureState(Canvas canvas)
            {
                this.canvas = canvas;
                renderMode = canvas.renderMode;
                worldCamera = canvas.worldCamera;
                planeDistance = canvas.planeDistance;
            }

            public Canvas Canvas
            {
                get { return canvas; }
            }

            public void Apply(Camera captureCamera)
            {
                if (canvas == null || captureCamera == null)
                {
                    return;
                }

                if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                }

                if (canvas.worldCamera != captureCamera)
                {
                    canvas.worldCamera = captureCamera;
                }

                if (!Mathf.Approximately(canvas.planeDistance, 10.0f))
                {
                    canvas.planeDistance = 10.0f;
                }

                applied = true;
            }

            public void Restore()
            {
                if (canvas == null || !applied)
                {
                    return;
                }

                canvas.renderMode = renderMode;
                canvas.worldCamera = worldCamera;
                canvas.planeDistance = planeDistance;
                applied = false;
            }
        }
    }
}
