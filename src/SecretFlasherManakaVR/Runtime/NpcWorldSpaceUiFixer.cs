using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.NPC.Script;
using ExposureUnnoticed2.ObjectUI.InGame.NpcDirectIconPanel;
using ExposureUnnoticed2.ObjectUI.NpcUi;
using UnityEngine;
using UnityEngine.UI;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class NpcWorldSpaceUiFixer
    {
        private const float FallbackHeadOffset = 1.75f;

        private readonly IVrRuntimeLogger logger;
        private bool warned;
        private bool itemWarned;
        private bool diagnosticsLogged;
        private float nextDiagnosticsTime;
        private int diagnosticsAttempts;

        private static IVrRuntimeLogger currentLogger = NullVrRuntimeLogger.Instance;
        private static VrRuntimeSettings currentSettings;
        private static Camera currentSourceCamera;
        private static Vector3 currentHeadPosition;
        private static Quaternion currentHeadRotation = Quaternion.identity;
        private static bool currentContextValid;
        private static bool postfixWarned;
        private static readonly Dictionary<int, WorldUiState> worldUiStates = new Dictionary<int, WorldUiState>();

        public NpcWorldSpaceUiFixer(IVrRuntimeLogger logger)
        {
            this.logger = logger ?? NullVrRuntimeLogger.Instance;
            currentLogger = this.logger;
        }

        public void Tick(VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            if (settings == null || (!settings.FixNpcWorldSpaceUi && !settings.LogNpcWorldSpaceUiDiagnostics))
            {
                currentContextValid = false;
                RestoreAndDisableWorldUiStates();
                return;
            }

            try
            {
                currentSettings = settings;
                currentSourceCamera = sourceCamera;
                currentHeadPosition = headPosition;
                currentHeadRotation = headRotation;
                currentContextValid = settings.FixNpcWorldSpaceUi;

                NpcUiView[] views = UnityEngine.Object.FindObjectsOfType<NpcUiView>();
                NpcDirectionArrowView[] arrows = UnityEngine.Object.FindObjectsOfType<NpcDirectionArrowView>();
                if (settings.LogNpcWorldSpaceUiDiagnostics && !diagnosticsLogged && Time.unscaledTime >= nextDiagnosticsTime)
                {
                    diagnosticsLogged = LogDiagnostics(views, arrows);
                    diagnosticsAttempts++;
                    nextDiagnosticsTime = Time.unscaledTime + 2.0f;
                }

                if (!settings.FixNpcWorldSpaceUi)
                {
                    RestoreAndDisableWorldUiStates();
                    return;
                }

                for (int i = 0; i < arrows.Length; i++)
                {
                    TryFixArrow(arrows[i], settings, sourceCamera, headPosition, headRotation);
                }

                for (int i = 0; i < views.Length; i++)
                {
                    TryFixNpcUi(views[i], settings, sourceCamera, headPosition, headRotation);
                }

                DeactivateUntouchedWorldUiStates(Time.frameCount);
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    logger.Warning("NPC world-space UI VR fix failed: " + ex);
                }
            }
        }

        public static void ApplyNpcUiPostLateUpdate(NpcUiView view)
        {
            if (!currentContextValid || currentSettings == null)
            {
                return;
            }

            try
            {
                FixWorldSpaceNpcUi(view, currentSettings, currentSourceCamera, currentHeadPosition, currentHeadRotation);
            }
            catch (Exception ex)
            {
                if (!postfixWarned)
                {
                    postfixWarned = true;
                    currentLogger.Warning("NPC UI postfix VR fix failed: " + ex);
                }
            }
        }

        private void TryFixNpcUi(NpcUiView view, VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            try
            {
                FixWorldSpaceNpcUi(view, settings, sourceCamera, headPosition, headRotation);
            }
            catch (Exception ex)
            {
                WarnItemFailure("NpcUi", view == null ? null : view.transform, ex);
            }
        }

        private void TryFixArrow(NpcDirectionArrowView arrow, VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            try
            {
                FixScreenSpaceArrow(arrow, settings, sourceCamera, headPosition, headRotation);
            }
            catch (Exception ex)
            {
                WarnItemFailure("NpcDirectionArrow", arrow == null ? null : arrow.transform, ex);
            }
        }

        private void WarnItemFailure(string kind, Transform transform, Exception ex)
        {
            if (itemWarned)
            {
                return;
            }

            itemWarned = true;
            logger.Warning("NPC world-space UI VR fix skipped one " + kind + " at " + PathOf(transform) + ": " + ex);
        }

        private bool LogDiagnostics(NpcUiView[] views, NpcDirectionArrowView[] arrows)
        {
            if ((views == null || views.Length == 0) && (arrows == null || arrows.Length == 0))
            {
                logger.Info("NPC world-space UI diagnostics attempt " + (diagnosticsAttempts + 1) + ": no NpcUiView or NpcDirectionArrowView instances found.");
                LogCanvasDiagnostics(true);
                return diagnosticsAttempts >= 9;
            }

            string viewEntries = BuildNpcUiViewDiagnostics(views);
            string arrowEntries = BuildNpcDirectionArrowDiagnostics(arrows);
            logger.Info("NPC world-space UI diagnostics: views=" + (views == null ? 0 : views.Length) +
                " arrows=" + (arrows == null ? 0 : arrows.Length) +
                " viewEntries=" + viewEntries +
                " arrowEntries=" + arrowEntries);
            LogCanvasDiagnostics(false);
            return true;
        }

        private string BuildNpcUiViewDiagnostics(NpcUiView[] views)
        {
            if (views == null || views.Length == 0)
            {
                return "<none>";
            }

            int count = Mathf.Min(views.Length, 6);
            string[] entries = new string[count];
            for (int i = 0; i < count; i++)
            {
                NpcUiView view = views[i];
                RectTransform rect = view == null ? null : view.rect;
                Canvas canvas = rect == null ? null : rect.GetComponentInParent<Canvas>();
                entries[i] =
                    "view=" + PathOf(view == null ? null : view.transform) +
                    " rect=" + PathOf(rect) +
                    " canvas=" + (canvas == null ? "<null>" : PathOf(canvas.transform)) +
                    " renderMode=" + (canvas == null ? "<null>" : canvas.renderMode.ToString()) +
                    " rootCanvas=" + (canvas == null || canvas.rootCanvas == null ? "<null>" : PathOf(canvas.rootCanvas.transform)) +
                    " rectWorld=" + (rect == null ? "<null>" : rect.position.ToString("F3")) +
                    " rectLocal=" + (rect == null ? "<null>" : rect.localPosition.ToString("F3"));
            }

            return string.Join(" | ", entries);
        }

        private string BuildNpcDirectionArrowDiagnostics(NpcDirectionArrowView[] arrows)
        {
            if (arrows == null || arrows.Length == 0)
            {
                return "<none>";
            }

            int count = Mathf.Min(arrows.Length, 8);
            string[] entries = new string[count];
            for (int i = 0; i < count; i++)
            {
                NpcDirectionArrowView arrow = arrows[i];
                RectTransform rect = arrow == null || arrow.arrowImage == null ? null : arrow.arrowImage.rectTransform;
                Canvas canvas = rect == null ? null : rect.GetComponentInParent<Canvas>();
                Transform targetTransform = arrow == null || arrow.target == null ? null : arrow.target.Transform;
                entries[i] =
                    "arrow=" + PathOf(arrow == null ? null : arrow.transform) +
                    " image=" + PathOf(rect) +
                    " target=" + PathOf(targetTransform) +
                    " canvas=" + (canvas == null ? "<null>" : PathOf(canvas.transform)) +
                    " renderMode=" + (canvas == null ? "<null>" : canvas.renderMode.ToString()) +
                    " rootCanvas=" + (canvas == null || canvas.rootCanvas == null ? "<null>" : PathOf(canvas.rootCanvas.transform)) +
                    " imageWorld=" + (rect == null ? "<null>" : rect.position.ToString("F3")) +
                    " imageLocal=" + (rect == null ? "<null>" : rect.localPosition.ToString("F3"));
            }

            return string.Join(" | ", entries);
        }

        private void LogCanvasDiagnostics(bool includeRectTree)
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            int count = Mathf.Min(canvases.Length, 16);
            string[] entries = new string[count];
            for (int i = 0; i < count; i++)
            {
                Canvas canvas = canvases[i];
                RectTransform rect = canvas == null ? null : canvas.GetComponent<RectTransform>();
                entries[i] =
                    "canvas=" + PathOf(canvas == null ? null : canvas.transform) +
                    " mode=" + (canvas == null ? "<null>" : canvas.renderMode.ToString()) +
                    " root=" + (canvas == null || canvas.rootCanvas == null ? "<null>" : PathOf(canvas.rootCanvas.transform)) +
                    " active=" + (canvas != null && canvas.gameObject != null && canvas.gameObject.activeInHierarchy) +
                    " enabled=" + (canvas != null && canvas.enabled) +
                    " pos=" + (rect == null ? "<null>" : rect.position.ToString("F3")) +
                    " size=" + (rect == null ? "<null>" : rect.rect.size.ToString("F1"));
            }

            logger.Info("NPC/world UI canvas diagnostics (" + canvases.Length + " canvases): " + string.Join(" | ", entries));
            if (includeRectTree)
            {
                LogCanvasRectTree(canvases);
            }
        }

        private void LogCanvasRectTree(Canvas[] canvases)
        {
            if (canvases == null || canvases.Length == 0)
            {
                return;
            }

            var entries = new List<string>();
            for (int i = 0; i < canvases.Length && entries.Count < 80; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.gameObject == null || !canvas.gameObject.activeInHierarchy)
                {
                    continue;
                }

                RectTransform root = canvas.GetComponent<RectTransform>();
                if (root == null)
                {
                    continue;
                }

                RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
                for (int j = 0; j < rects.Length && entries.Count < 80; j++)
                {
                    RectTransform rect = rects[j];
                    if (rect == null || rect.gameObject == null || !rect.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    string name = rect.gameObject.name ?? string.Empty;
                    string components = ComponentSummary(rect.gameObject);
                    if (LooksRelevant(name) || LooksRelevant(components) || entries.Count < 24)
                    {
                        entries.Add(PathOf(rect) +
                            " pos=" + rect.position.ToString("F1") +
                            " local=" + rect.localPosition.ToString("F1") +
                            " size=" + rect.rect.size.ToString("F1") +
                            " comps=" + components);
                    }
                }
            }

            logger.Info("NPC/world UI rect diagnostics: " + (entries.Count == 0 ? "<none>" : string.Join(" | ", entries.ToArray())));
        }

        private static string ComponentSummary(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            Component[] components = gameObject.GetComponents<Component>();
            int count = Mathf.Min(components.Length, 8);
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                Component component = components[i];
                names[i] = component == null ? "<null>" : component.GetType().FullName;
            }

            return string.Join(",", names);
        }

        private static bool LooksRelevant(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf("npc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("direct", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("interact", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("gauge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("marker", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void FixWorldSpaceNpcUi(NpcUiView view, VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            if (view == null || settings == null)
            {
                return;
            }

            RectTransform rect = null;
            try
            {
                rect = view.rect;
            }
            catch (Exception)
            {
                return;
            }

            if (rect == null || rect.gameObject == null || !rect.gameObject.activeInHierarchy)
            {
                return;
            }

            Canvas sourceCanvas = rect.GetComponentInParent<Canvas>();
            if (sourceCanvas != null && sourceCanvas.renderMode == RenderMode.WorldSpace)
            {
                return;
            }

            if (!TryGetAnchorPosition(view, out var anchorPosition))
            {
                return;
            }

            WorldUiState state = GetOrCreateWorldUiState(view, rect);
            if (state == null || state.CanvasObject == null || state.CanvasRect == null || state.CloneRect == null)
            {
                return;
            }

            state.LastSeenFrame = Time.frameCount;
            if (sourceCamera != null)
            {
                state.Canvas.worldCamera = sourceCamera;
            }

            HideSourceUi(state);
            SyncCloneFromSource(rect, state.CloneRect);
            ForceCloneGraphicsVisible(state.CloneObject);
            ApplyNpcUiSemanticVisibility(view, state);
            state.CanvasObject.SetActive(true);
            state.Canvas.renderMode = RenderMode.WorldSpace;
            state.Canvas.overrideSorting = true;
            state.Canvas.sortingOrder = 500;

            Vector3 uiPosition = anchorPosition + Vector3.up * settings.NpcWorldSpaceUiVerticalOffset;
            float distance = Vector3.Distance(headPosition, uiPosition);
            int renderLayer = VrUiBridge.VrUiOverlayLayer;
            state.CanvasRect.sizeDelta = SafeSize(state.CloneRect);
            state.CanvasRect.localScale = Vector3.one * CompensatedWorldScale(settings, distance);
            state.CanvasRect.position = uiPosition;
            state.CanvasRect.rotation = WorldBillboardRotation(uiPosition, headPosition, headRotation);
            state.CanvasRect.gameObject.layer = renderLayer;

            state.CloneRect.anchorMin = new Vector2(0.5f, 0.5f);
            state.CloneRect.anchorMax = new Vector2(0.5f, 0.5f);
            state.CloneRect.pivot = new Vector2(0.5f, 0.5f);
            state.CloneRect.anchoredPosition = Vector2.zero;
            state.CloneRect.localPosition = Vector3.zero;
            state.CloneRect.localRotation = Quaternion.identity;
            state.CloneRect.localScale = Vector3.one;
            SetLayerRecursively(state.CanvasObject, renderLayer);
        }

        private static float CompensatedWorldScale(VrRuntimeSettings settings, float distance)
        {
            float minDistance = Mathf.Max(0.01f, settings.NpcWorldSpaceUiMinScaleDistance);
            float maxDistance = Mathf.Max(minDistance, settings.NpcWorldSpaceUiMaxScaleDistance);
            float effectiveDistance = Mathf.Clamp(distance, minDistance, maxDistance);
            const float scaleBoost = 1.15f;
            const float referenceDistance = 5.0f;
            return settings.NpcWorldSpaceUiScale * scaleBoost * (effectiveDistance / referenceDistance);
        }

        private static WorldUiState GetOrCreateWorldUiState(NpcUiView view, RectTransform rect)
        {
            int id = view.GetInstanceID();
            if (worldUiStates.TryGetValue(id, out var existing) &&
                existing != null &&
                existing.CanvasObject != null &&
                existing.CanvasRect != null &&
                existing.CloneRect != null)
            {
                return existing;
            }

            GameObject canvasObject = new GameObject("SecretFlasherManakaVR Npc World UI");
            canvasObject.hideFlags = HideFlags.DontSave;
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = SafeSize(rect);

            GameObject cloneObject = UnityEngine.Object.Instantiate(rect.gameObject);
            cloneObject.name = rect.gameObject.name + " VR World Clone";
            cloneObject.hideFlags = HideFlags.DontSave;
            cloneObject.transform.SetParent(canvasRect, false);
            NpcUiView cloneView = cloneObject.GetComponent<NpcUiView>();
            if (cloneView != null)
            {
                cloneView.enabled = false;
            }

            RectTransform cloneRect = cloneObject.GetComponent<RectTransform>();
            cloneRect.anchorMin = new Vector2(0.5f, 0.5f);
            cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRect.pivot = new Vector2(0.5f, 0.5f);
            cloneRect.anchoredPosition = Vector2.zero;
            cloneRect.localPosition = Vector3.zero;
            cloneRect.localRotation = Quaternion.identity;
            cloneRect.localScale = Vector3.one;

            CanvasGroup sourceGroup = rect.GetComponent<CanvasGroup>();
            bool addedSourceGroup = sourceGroup == null;
            if (sourceGroup == null)
            {
                sourceGroup = rect.gameObject.AddComponent<CanvasGroup>();
            }

            CanvasGroup cloneGroup = cloneObject.GetComponent<CanvasGroup>();
            if (cloneGroup == null)
            {
                cloneGroup = cloneObject.AddComponent<CanvasGroup>();
            }

            cloneGroup.alpha = 1.0f;
            cloneGroup.interactable = false;
            cloneGroup.blocksRaycasts = false;

            var state = new WorldUiState
            {
                CanvasObject = canvasObject,
                Canvas = canvas,
                CanvasRect = canvasRect,
                SourceObject = rect.gameObject,
                CloneObject = cloneObject,
                CloneView = cloneView,
                CloneRect = cloneRect,
                SourceGroup = sourceGroup,
                AddedSourceGroup = addedSourceGroup,
                OriginalSourceAlpha = sourceGroup.alpha,
                OriginalSourceInteractable = sourceGroup.interactable,
                OriginalSourceBlocksRaycasts = sourceGroup.blocksRaycasts,
                OriginalSourceIgnoreParentGroups = sourceGroup.ignoreParentGroups,
                OriginalParent = rect.parent,
                OriginalSiblingIndex = rect.GetSiblingIndex(),
                LastSeenFrame = Time.frameCount
            };
            worldUiStates[id] = state;
            return state;
        }

        private static void DeactivateUntouchedWorldUiStates(int frame)
        {
            List<int> removedIds = null;
            foreach (KeyValuePair<int, WorldUiState> entry in worldUiStates)
            {
                WorldUiState state = entry.Value;
                if (state == null || state.CanvasObject == null)
                {
                    if (removedIds == null)
                    {
                        removedIds = new List<int>();
                    }

                    removedIds.Add(entry.Key);
                    continue;
                }

                if (state.SourceObject == null)
                {
                    RestoreSourceUi(state);
                    UnityEngine.Object.Destroy(state.CanvasObject);
                    if (removedIds == null)
                    {
                        removedIds = new List<int>();
                    }

                    removedIds.Add(entry.Key);
                    continue;
                }

                if (state.LastSeenFrame != frame)
                {
                    RestoreSourceUi(state);
                    state.CanvasObject.SetActive(false);
                }
            }

            if (removedIds == null)
            {
                return;
            }

            for (int i = 0; i < removedIds.Count; i++)
            {
                worldUiStates.Remove(removedIds[i]);
            }
        }

        private static void RestoreAndDisableWorldUiStates()
        {
            foreach (KeyValuePair<int, WorldUiState> entry in worldUiStates)
            {
                WorldUiState state = entry.Value;
                RestoreSourceUi(state);
                if (state != null && state.CanvasObject != null)
                {
                    state.CanvasObject.SetActive(false);
                }
            }
        }

        private static void HideSourceUi(WorldUiState state)
        {
            if (state == null || state.SourceGroup == null)
            {
                return;
            }

            state.SourceGroup.alpha = 0.0f;
            state.SourceGroup.interactable = false;
            state.SourceGroup.blocksRaycasts = false;
        }

        private static void RestoreSourceUi(WorldUiState state)
        {
            if (state == null || state.SourceGroup == null)
            {
                return;
            }

            state.SourceGroup.alpha = state.OriginalSourceAlpha;
            state.SourceGroup.interactable = state.OriginalSourceInteractable;
            state.SourceGroup.blocksRaycasts = state.OriginalSourceBlocksRaycasts;
            state.SourceGroup.ignoreParentGroups = state.OriginalSourceIgnoreParentGroups;
        }

        private static void SyncCloneFromSource(RectTransform sourceRoot, RectTransform cloneRoot)
        {
            if (sourceRoot == null || cloneRoot == null)
            {
                return;
            }

            RectTransform[] sourceRects = sourceRoot.GetComponentsInChildren<RectTransform>(true);
            RectTransform[] cloneRects = cloneRoot.GetComponentsInChildren<RectTransform>(true);
            int count = Mathf.Min(sourceRects.Length, cloneRects.Length);
            for (int i = 0; i < count; i++)
            {
                RectTransform source = sourceRects[i];
                RectTransform clone = cloneRects[i];
                if (source == null || clone == null || source.gameObject == null || clone.gameObject == null)
                {
                    continue;
                }

                clone.gameObject.SetActive(true);
                clone.anchorMin = source.anchorMin;
                clone.anchorMax = source.anchorMax;
                clone.pivot = source.pivot;
                clone.sizeDelta = source.sizeDelta;
                clone.localRotation = source.localRotation;
                clone.localScale = source.localScale;
                if (i > 0)
                {
                    clone.anchoredPosition = source.anchoredPosition;
                    clone.localPosition = source.localPosition;
                }

                SyncCanvasGroup(source.GetComponent<CanvasGroup>(), clone.GetComponent<CanvasGroup>(), i == 0);
                SyncImage(source.GetComponent<Image>(), clone.GetComponent<Image>());
            }
        }

        private static void SyncCanvasGroup(CanvasGroup source, CanvasGroup clone, bool isRoot)
        {
            if (source == null || clone == null)
            {
                return;
            }

            clone.alpha = isRoot ? 1.0f : source.alpha;
            clone.interactable = false;
            clone.blocksRaycasts = false;
            clone.ignoreParentGroups = source.ignoreParentGroups;
        }

        private static void SyncImage(Image source, Image clone)
        {
            if (source == null || clone == null)
            {
                return;
            }

            bool hasSprite = source.sprite != null || source.overrideSprite != null;
            bool hasAlpha = source.color.a > 0.001f;
            bool hasFill = source.type != Image.Type.Filled || source.fillAmount > 0.001f;
            clone.enabled = source.enabled && hasSprite && hasAlpha && hasFill;
            clone.sprite = source.sprite;
            clone.overrideSprite = source.overrideSprite;
            clone.color = source.color;
            clone.type = source.type;
            clone.fillAmount = source.fillAmount;
            clone.fillClockwise = source.fillClockwise;
            clone.fillOrigin = source.fillOrigin;
            clone.fillMethod = source.fillMethod;
            clone.preserveAspect = source.preserveAspect;
            clone.raycastTarget = false;
            clone.material = null;
            clone.canvasRenderer.cull = false;
        }

        private static void ApplyNpcUiSemanticVisibility(NpcUiView sourceView, WorldUiState state)
        {
            if (sourceView == null || state == null || state.CloneObject == null)
            {
                return;
            }

            Image sourceCircle = SafeGetCircleGauge(sourceView);
            Image cloneCircle = SafeGetCircleGauge(state.CloneView);
            if (cloneCircle == null)
            {
                cloneCircle = FindImageByName(state.CloneObject.transform, "CircleGauge");
            }

            bool circleVisible = IsGaugeVisible(sourceCircle);
            SetImageVisible(cloneCircle, circleVisible);

            GameObject cloneQuestion = SafeGetQuestionObject(state.CloneView);
            if (cloneQuestion == null)
            {
                Transform question = FindChildByName(state.CloneObject.transform, "Question");
                cloneQuestion = question == null ? null : question.gameObject;
            }

            if (cloneQuestion != null)
            {
                cloneQuestion.SetActive(circleVisible);
            }

            Image sourceSubCircle = SafeGetSubCircleGauge(sourceView);
            Image cloneSubCircle = SafeGetSubCircleGauge(state.CloneView);
            if (cloneSubCircle == null)
            {
                cloneSubCircle = FindImageByName(state.CloneObject.transform, "DebugSubGauge");
            }

            SetImageVisible(cloneSubCircle, IsGaugeVisible(sourceSubCircle) && IsActiveSelfInRoot(sourceSubCircle == null ? null : sourceSubCircle.transform, sourceView.rect));
        }

        private static bool IsGaugeVisible(Image image)
        {
            if (image == null || !image.enabled)
            {
                return false;
            }

            bool hasSprite = image.sprite != null || image.overrideSprite != null;
            bool hasAlpha = image.color.a > 0.001f;
            return hasSprite && hasAlpha && image.fillAmount > 0.001f;
        }

        private static void SetImageVisible(Image image, bool visible)
        {
            if (image != null)
            {
                image.enabled = visible;
            }
        }

        private static bool IsActiveSelfInRoot(Transform transform, Transform root)
        {
            if (transform == null || root == null)
            {
                return false;
            }

            Transform current = transform;
            while (current != null)
            {
                if (current.gameObject != null && !current.gameObject.activeSelf)
                {
                    return false;
                }

                if (current == root)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static Image SafeGetCircleGauge(NpcUiView view)
        {
            if (view == null)
            {
                return null;
            }

            try
            {
                return view.circleGauge;
            }
            catch
            {
                return null;
            }
        }

        private static Image SafeGetSubCircleGauge(NpcUiView view)
        {
            if (view == null)
            {
                return null;
            }

            try
            {
                return view.subCircleGauge;
            }
            catch
            {
                return null;
            }
        }

        private static GameObject SafeGetQuestionObject(NpcUiView view)
        {
            if (view == null)
            {
                return null;
            }

            try
            {
                return view.questionObject;
            }
            catch
            {
                return null;
            }
        }

        private static Image FindImageByName(Transform root, string name)
        {
            Transform transform = FindChildByName(root, name);
            return transform == null ? null : transform.GetComponent<Image>();
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (root.gameObject != null && root.gameObject.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                Transform match = FindChildByName(child, name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static void ForceCloneGraphicsVisible(GameObject cloneObject)
        {
            if (cloneObject == null)
            {
                return;
            }

            CanvasGroup[] groups = cloneObject.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                group.interactable = false;
                group.blocksRaycasts = false;
                if (group.gameObject == cloneObject)
                {
                    group.alpha = 1.0f;
                }
            }

            Mask[] masks = cloneObject.GetComponentsInChildren<Mask>(true);
            for (int i = 0; i < masks.Length; i++)
            {
                if (masks[i] != null)
                {
                    masks[i].enabled = false;
                }
            }

            RectMask2D[] rectMasks = cloneObject.GetComponentsInChildren<RectMask2D>(true);
            for (int i = 0; i < rectMasks.Length; i++)
            {
                if (rectMasks[i] != null)
                {
                    rectMasks[i].enabled = false;
                }
            }

            Graphic[] graphics = cloneObject.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                ActivatePathToCloneRoot(graphic.transform, cloneObject.transform);
                graphic.raycastTarget = false;
                graphic.material = null;
                graphic.canvasRenderer.cull = false;
            }
        }

        private static void ActivatePathToCloneRoot(Transform transform, Transform cloneRoot)
        {
            Transform current = transform;
            while (current != null)
            {
                if (current.gameObject != null)
                {
                    current.gameObject.SetActive(true);
                }

                if (current == cloneRoot)
                {
                    return;
                }

                current = current.parent;
            }
        }

        private static Vector2 SafeSize(RectTransform rect)
        {
            if (rect == null)
            {
                return new Vector2(128.0f, 128.0f);
            }

            Vector2 size = rect.rect.size;
            if (size.x <= 1.0f || size.y <= 1.0f)
            {
                size = rect.sizeDelta;
            }

            if (size.x <= 1.0f || size.y <= 1.0f)
            {
                size = new Vector2(128.0f, 128.0f);
            }

            return size;
        }

        private static Quaternion WorldBillboardRotation(Vector3 worldPosition, Vector3 headPosition, Quaternion fallbackRotation)
        {
            Vector3 awayFromViewer = worldPosition - headPosition;
            if (awayFromViewer.sqrMagnitude < 0.0001f)
            {
                return fallbackRotation;
            }

            return Quaternion.LookRotation(awayFromViewer.normalized, Vector3.up);
        }

        private static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            if (gameObject == null)
            {
                return;
            }

            gameObject.layer = layer;
            Transform transform = gameObject.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null)
                {
                    SetLayerRecursively(child.gameObject, layer);
                }
            }
        }

        private static void FixScreenSpaceArrow(NpcDirectionArrowView arrow, VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            if (arrow == null || arrow.arrowImage == null || arrow.target == null)
            {
                return;
            }

            RectTransform rect = arrow.arrowImage.rectTransform;
            if (rect == null || rect.gameObject == null || !rect.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!TryGetAnchorPosition(arrow.target, out var anchorPosition))
            {
                return;
            }

            if (TryProjectToScreen(anchorPosition + Vector3.up * settings.NpcWorldSpaceUiVerticalOffset, sourceCamera, headPosition, headRotation, true, out var screenPoint))
            {
                Canvas canvas = rect.GetComponentInParent<Canvas>();
                ApplyScreenPoint(rect, canvas, sourceCamera, screenPoint);
            }
        }

        private static bool TryProjectToScreen(Vector3 worldPosition, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation, bool clampToScreen, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            Vector3 local = Quaternion.Inverse(headRotation) * (worldPosition - headPosition);
            if (local.z <= 0.05f)
            {
                return false;
            }

            float fov = sourceCamera == null ? 60.0f : sourceCamera.fieldOfView;
            float aspect = sourceCamera == null || sourceCamera.aspect <= 0.0f
                ? Mathf.Max(0.1f, Screen.width / (float)Mathf.Max(1, Screen.height))
                : sourceCamera.aspect;
            float tanHalfFov = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
            if (tanHalfFov <= 0.001f)
            {
                return false;
            }

            float viewportX = 0.5f + local.x / (local.z * tanHalfFov * aspect) * 0.5f;
            float viewportY = 0.5f + local.y / (local.z * tanHalfFov) * 0.5f;
            if (clampToScreen)
            {
                viewportX = Mathf.Clamp(viewportX, 0.05f, 0.95f);
                viewportY = Mathf.Clamp(viewportY, 0.08f, 0.92f);
            }
            else if (viewportX < -0.5f || viewportX > 1.5f || viewportY < -0.5f || viewportY > 1.5f)
            {
                return false;
            }

            screenPoint = new Vector2(viewportX * Screen.width, viewportY * Screen.height);
            return true;
        }

        private static void ApplyScreenPoint(RectTransform rect, Canvas canvas, Camera sourceCamera, Vector2 screenPoint)
        {
            if (rect == null)
            {
                return;
            }

            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                rect.position = new Vector3(screenPoint.x, screenPoint.y, rect.position.z);
                return;
            }

            RectTransform rootRect = canvas.rootCanvas == null ? null : canvas.rootCanvas.GetComponent<RectTransform>();
            Camera eventCamera = canvas.worldCamera != null ? canvas.worldCamera : sourceCamera;
            if (rootRect != null &&
                RectTransformUtility.ScreenPointToWorldPointInRectangle(rootRect, screenPoint, eventCamera, out var worldPoint))
            {
                rect.position = worldPoint;
            }
        }

        private static bool TryGetAnchorPosition(NpcUiView view, out Vector3 position)
        {
            position = Vector3.zero;

            NpcComponentAccessor accessor = null;
            try
            {
                accessor = view.nca;
            }
            catch
            {
                accessor = null;
            }

            Transform head = null;
            try
            {
                var avatar = accessor == null ? null : accessor.AvatarComponent;
                head = avatar == null ? null : avatar.Head;
            }
            catch
            {
                head = null;
            }

            if (head != null)
            {
                position = head.position;
                return true;
            }

            NpcController target = null;
            try
            {
                target = view.target;
            }
            catch
            {
                target = null;
            }

            Transform eye = null;
            try
            {
                eye = target == null ? null : target.EyePosition;
            }
            catch
            {
                eye = null;
            }

            if (eye != null)
            {
                position = eye.position;
                return true;
            }

            Transform targetTransform = null;
            try
            {
                targetTransform = target == null ? null : target.Transform;
            }
            catch
            {
                targetTransform = null;
            }

            if (targetTransform != null)
            {
                position = targetTransform.position + Vector3.up * FallbackHeadOffset;
                return true;
            }

            Transform root = null;
            try
            {
                root = accessor == null ? null : accessor.Transform;
            }
            catch
            {
                root = null;
            }

            if (root != null)
            {
                position = root.position + Vector3.up * FallbackHeadOffset;
                return true;
            }

            return false;
        }

        private static bool TryGetAnchorPosition(NpcController target, out Vector3 position)
        {
            position = Vector3.zero;

            Transform eye = null;
            try
            {
                eye = target == null ? null : target.EyePosition;
            }
            catch
            {
                eye = null;
            }

            if (eye != null)
            {
                position = eye.position;
                return true;
            }

            Transform targetTransform = null;
            try
            {
                targetTransform = target == null ? null : target.Transform;
            }
            catch
            {
                targetTransform = null;
            }

            if (targetTransform != null)
            {
                position = targetTransform.position + Vector3.up * FallbackHeadOffset;
                return true;
            }

            return false;
        }

        private static string PathOf(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            string path = transform.gameObject == null ? transform.name : transform.gameObject.name;
            Transform current = transform.parent;
            while (current != null)
            {
                string name = current.gameObject == null ? current.name : current.gameObject.name;
                path = name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private sealed class WorldUiState
        {
            public GameObject CanvasObject;
            public Canvas Canvas;
            public RectTransform CanvasRect;
            public GameObject SourceObject;
            public GameObject CloneObject;
            public NpcUiView CloneView;
            public RectTransform CloneRect;
            public CanvasGroup SourceGroup;
            public bool AddedSourceGroup;
            public float OriginalSourceAlpha;
            public bool OriginalSourceInteractable;
            public bool OriginalSourceBlocksRaycasts;
            public bool OriginalSourceIgnoreParentGroups;
            public Transform OriginalParent;
            public int OriginalSiblingIndex;
            public int LastSeenFrame;
        }
    }
}
