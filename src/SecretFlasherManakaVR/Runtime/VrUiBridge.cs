using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.IngameManager;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class VrUiBridge
    {
        internal const int VrUiOverlayLayer = 30;
        private const int FallbackTextureWidth = 1920;
        private const int FallbackTextureHeight = 1080;

        private readonly IVrRuntimeLogger logger;
        private readonly List<Canvas> activeCanvases = new List<Canvas>();
        private float nextScanTime;
        private int convertedLayerMask;
        private bool initializedLogged;
        private bool worldFixedPoseSet;
        private Vector3 worldFixedPosition;
        private Quaternion worldFixedRotation = Quaternion.identity;
        private Vector3 uiPosition;
        private Quaternion uiRotation = Quaternion.identity;
        private GameObject root;
        private Camera captureCamera;
        private RenderTexture uiTexture;
        private GameObject panelObject;
        private Material panelMaterial;
        private int textureWidth;
        private int textureHeight;

        public VrUiBridge(IVrRuntimeLogger logger)
        {
            this.logger = logger ?? NullVrRuntimeLogger.Instance;
        }

        public int ConvertedLayerMask
        {
            get { return convertedLayerMask; }
        }

        public int ConvertedCanvasCount
        {
            get { return activeCanvases.Count; }
        }

        public void Tick(VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            if (settings == null || !settings.EnableVrUiBridge || !settings.ConvertOverlayCanvasToWorldSpace)
            {
                SetPanelVisible(false);
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
            activeCanvases.Clear();
            nextScanTime = 0.0f;
            worldFixedPoseSet = false;
            SetPanelVisible(false);
            convertedLayerMask = 0;
        }

        public void Shutdown()
        {
            activeCanvases.Clear();
            convertedLayerMask = 0;
            ReleaseTexture();

            if (panelMaterial != null)
            {
                UnityEngine.Object.Destroy(panelMaterial);
                panelMaterial = null;
            }

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }

            captureCamera = null;
            panelObject = null;
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
            captureCamera.cullingMask = ~0;
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
            SetPanelVisible(false);
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
            return material;
        }

        private void UpdateAnchor(VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            Vector3 basePosition = headPosition;
            Quaternion baseRotation = headRotation;

            if (settings.VrUiFollowMode == VrUiFollowMode.SourceCameraLocked && sourceCamera != null)
            {
                basePosition = sourceCamera.transform.position;
                baseRotation = sourceCamera.transform.rotation;
            }
            else if (settings.VrUiFollowMode == VrUiFollowMode.WorldFixed)
            {
                if (!worldFixedPoseSet)
                {
                    worldFixedPosition = basePosition;
                    worldFixedRotation = baseRotation;
                    worldFixedPoseSet = true;
                }

                basePosition = worldFixedPosition;
                baseRotation = worldFixedRotation;
            }

            Vector3 offset = (Vector3.forward * settings.VrUiDistance) + (Vector3.up * settings.VrUiVerticalOffset);
            uiPosition = basePosition + baseRotation * offset;
            uiRotation = baseRotation;

            if (!initializedLogged && settings.LogVrUiDiagnostics)
            {
                initializedLogged = true;
                logger.Info("VR UI bridge initialized with RenderTexture capture panel.");
            }
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

            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            string[] whitelist = SplitKeywords(settings.VrUiCanvasNameWhitelist);
            string[] blacklist = SplitKeywords(settings.VrUiCanvasNameBlacklist);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (ShouldCapture(canvas, whitelist, blacklist))
                {
                    activeCanvases.Add(canvas);
                }
            }

            if (settings.LogVrUiDiagnostics)
            {
                logger.Info("VR UI capture canvases: " + activeCanvases.Count + ".");
            }
        }

        private void RemoveInactiveCanvases()
        {
            for (int i = activeCanvases.Count - 1; i >= 0; i--)
            {
                Canvas canvas = activeCanvases[i];
                if (canvas == null || canvas.gameObject == null || !canvas.gameObject.activeInHierarchy || !canvas.enabled)
                {
                    activeCanvases.RemoveAt(i);
                }
            }
        }

        private bool ShouldCapture(Canvas canvas, string[] whitelist, string[] blacklist)
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

            string name = canvas.name ?? string.Empty;
            if (MatchesAny(name, blacklist))
            {
                return false;
            }

            return whitelist.Length == 0 || MatchesAny(name, whitelist);
        }

        private void RenderUiToTexture(VrRuntimeSettings settings)
        {
            if (activeCanvases.Count == 0 || captureCamera == null)
            {
                SetPanelVisible(false);
                return;
            }

            EnsureTexture();
            if (uiTexture == null)
            {
                SetPanelVisible(false);
                return;
            }

            var states = new List<CanvasCaptureState>();
            var hudLayout = new HudLayoutScope(logger);
            try
            {
                for (int i = 0; i < activeCanvases.Count; i++)
                {
                    Canvas canvas = activeCanvases[i];
                    if (canvas == null)
                    {
                        continue;
                    }

                    states.Add(new CanvasCaptureState(canvas));
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = captureCamera;
                    canvas.planeDistance = 10.0f;
                    hudLayout.Apply(canvas);
                }

                captureCamera.targetTexture = uiTexture;
                captureCamera.aspect = textureWidth / (float)textureHeight;
                captureCamera.orthographicSize = textureHeight * 0.5f;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = Color.clear;
                captureCamera.Render();
                SetPanelVisible(true);
            }
            catch (Exception ex)
            {
                SetPanelVisible(false);
                logger.Warning("VR UI capture failed: " + ex.Message);
            }
            finally
            {
                hudLayout.Restore();
                for (int i = 0; i < states.Count; i++)
                {
                    states[i].Restore();
                }
            }
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

        private void ApplyPanelTransform(VrRuntimeSettings settings)
        {
            if (panelObject == null || uiTexture == null)
            {
                return;
            }

            float aspect = textureHeight <= 0 ? 16.0f / 9.0f : textureWidth / (float)textureHeight;
            float width = Mathf.Max(0.25f, settings.VrUiDistance * 1.35f);
            float height = width / aspect;
            panelObject.transform.SetPositionAndRotation(uiPosition, uiRotation);
            panelObject.transform.localScale = new Vector3(width, height, 1.0f);
            panelObject.layer = VrUiOverlayLayer;
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelObject != null && panelObject.activeSelf != visible)
            {
                panelObject.SetActive(visible);
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

        private sealed class HudLayoutScope
        {
            private const float HudReferenceWidth = 2560.0f;
            private const float HudReferenceHeight = 1440.0f;

            private readonly List<RectState> rectStates = new List<RectState>();
            private readonly List<ActiveState> activeStates = new List<ActiveState>();
            private readonly List<ParentState> parentStates = new List<ParentState>();
            private readonly HashSet<int> savedRects = new HashSet<int>();
            private readonly HashSet<int> savedObjects = new HashSet<int>();
            private readonly HashSet<int> savedParents = new HashSet<int>();

            public HudLayoutScope(IVrRuntimeLogger logger)
            {
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
                    HideObject(manager.shortcutSlotParent);
                    HideObject(manager.shortcutSlotParent2);
                    HideObject(manager.sexManualParent);
                    HideObject(manager.nomalManualParent);
                    HideComponent(manager.ingameUiManualView);
                    HideComponent(manager.ecstasyHeartIcon);

                    MovePathToRoot(root, "MiddleLayer/PlayerInfo/StaminaGauge", ReferencePointToRoot(root, 1135.0f, 1315.0f), null, 1.0f, 0.0f, true);
                    MovePathToRoot(root, "MiddleLayer/PlayerInfo/MoistureIcon", ReferencePointToRoot(root, 980.0f, 1327.0f), null, 0.72f, 0.0f, true);
                    MoveComponentToRoot(root, manager.ecstasyGauge, ReferencePointToRoot(root, 700.0f, 1300.0f), null, 1.65f, 0.0f);
                    MovePathToRoot(root, "MiddleLayer/HeartBeatPanel/HeartRateInfoPanel", ReferencePointToRoot(root, 1495.0f, 1326.0f), null, 0.46f, 0.0f, true);
                }

                HideIfNameContains(root, "Shortcut");
                HideIfNameContains(root, "Manual");
                HideIfNameContains(root, "EcstasyHeart");
            }

            private static Vector2 ReferencePointToRoot(RectTransform root, float referenceX, float referenceY)
            {
                Rect rect = root.rect;
                float width = rect.width > 0.0f ? rect.width : FallbackTextureWidth;
                float height = rect.height > 0.0f ? rect.height : FallbackTextureHeight;
                return new Vector2(referenceX * width / HudReferenceWidth, -referenceY * height / HudReferenceHeight);
            }

            public void Restore()
            {
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
                savedRects.Clear();
                savedObjects.Clear();
                savedParents.Clear();
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

            private void MoveComponentToRoot(RectTransform root, Component component, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation)
            {
                if (root == null || component == null)
                {
                    return;
                }

                RectTransform rect = component.GetComponent<RectTransform>();
                if (rect == null)
                {
                    rect = component.transform as RectTransform;
                }

                if (rect == null)
                {
                    return;
                }

                SaveRect(rect);
                SaveParent(rect);
                rect.SetParent(root, false);
                MoveRect(rect, anchoredPosition, sizeDelta, scale, zRotation);
            }

            private void MovePathToRoot(RectTransform root, string path, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation)
            {
                MovePathToRoot(root, path, anchoredPosition, sizeDelta, scale, zRotation, false);
            }

            private void MovePathToRoot(RectTransform root, string path, Vector2 anchoredPosition, Vector2? sizeDelta, float scale, float zRotation, bool preservePivot)
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
                SaveParent(rect);
                rect.SetParent(root, false);
                MoveRect(rect, anchoredPosition, sizeDelta, scale, zRotation, preservePivot);
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
        }

        private readonly struct CanvasCaptureState
        {
            private readonly Canvas canvas;
            private readonly RenderMode renderMode;
            private readonly Camera worldCamera;
            private readonly float planeDistance;

            public CanvasCaptureState(Canvas canvas)
            {
                this.canvas = canvas;
                renderMode = canvas.renderMode;
                worldCamera = canvas.worldCamera;
                planeDistance = canvas.planeDistance;
            }

            public void Restore()
            {
                if (canvas == null)
                {
                    return;
                }

                canvas.renderMode = renderMode;
                canvas.worldCamera = worldCamera;
                canvas.planeDistance = planeDistance;
            }
        }
    }
}
