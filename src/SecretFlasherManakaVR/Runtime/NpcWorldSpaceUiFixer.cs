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
        private const float SceneObjectScanMinIntervalSeconds = 0.5f;
        private const float SceneObjectScanMaxIntervalSeconds = 1.0f;
        private const string NpcUiRootPath = "InGameManager/InGameCanvas/BackLayer";
        private const string NpcDirectionArrowRootPath = "InGameManager/InGameCanvas/MiddleLayer/NpcDirectIconPanel";

        private static VrRuntimeSettings currentSettings = null!;
        private static Camera currentSourceCamera = null!;
        private static Vector3 currentHeadPosition;
        private static Quaternion currentHeadRotation = Quaternion.identity;
        private static bool currentContextValid;
        private static readonly Dictionary<int, WorldUiState> worldUiStates = new Dictionary<int, WorldUiState>();

        private NpcUiView[] cachedViews = Array.Empty<NpcUiView>();
        private NpcDirectionArrowView[] cachedArrows = Array.Empty<NpcDirectionArrowView>();
        private float nextSceneObjectScanTime;

        public NpcWorldSpaceUiFixer(IVrRuntimeLogger logger)
        {
        }

        public void Shutdown()
        {
            try
            {
                ClearWorldUiStates();
            }
            catch (Exception)
            {
            }
            finally
            {
                ClearCachedSceneObjects();
                ResetStaticContext();
            }
        }

        public void Tick(VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            if (settings == null || !settings.FixNpcWorldSpaceUi)
            {
                Shutdown();
                return;
            }

            try
            {
                currentSettings = settings;
                currentSourceCamera = sourceCamera;
                currentHeadPosition = headPosition;
                currentHeadRotation = headRotation;
                currentContextValid = settings.FixNpcWorldSpaceUi;

                RefreshCachedSceneObjectsIfNeeded();

                if (!settings.FixNpcWorldSpaceUi)
                {
                    ClearWorldUiStates();
                    currentContextValid = false;
                    return;
                }

                for (int i = 0; i < cachedArrows.Length; i++)
                {
                    TryFixArrow(cachedArrows[i], settings, sourceCamera, headPosition, headRotation);
                }

                for (int i = 0; i < cachedViews.Length; i++)
                {
                    TryFixNpcUi(cachedViews[i], settings, sourceCamera, headPosition, headRotation);
                }

                DeactivateUntouchedWorldUiStates(Time.frameCount);
            }
            catch (Exception)
            {
            }
        }

        private void RefreshCachedSceneObjectsIfNeeded()
        {
            if (Time.unscaledTime < nextSceneObjectScanTime && !HasInvalidCachedSceneObjects())
            {
                return;
            }

            cachedViews = FindComponentsUnderPath<NpcUiView>(NpcUiRootPath);
            cachedArrows = FindComponentsUnderPath<NpcDirectionArrowView>(NpcDirectionArrowRootPath);
            nextSceneObjectScanTime = Time.unscaledTime + UnityEngine.Random.Range(
                SceneObjectScanMinIntervalSeconds,
                SceneObjectScanMaxIntervalSeconds);
        }

        private static T[] FindComponentsUnderPath<T>(string rootPath)
            where T : Component
        {
            GameObject root = GameObject.Find(rootPath);
            if (root == null)
            {
                return Array.Empty<T>();
            }

            return root.GetComponentsInChildren<T>();
        }

        private bool HasInvalidCachedSceneObjects()
        {
            for (int i = 0; i < cachedViews.Length; i++)
            {
                if (cachedViews[i] == null)
                {
                    return true;
                }
            }

            for (int i = 0; i < cachedArrows.Length; i++)
            {
                if (cachedArrows[i] == null)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearCachedSceneObjects()
        {
            cachedViews = Array.Empty<NpcUiView>();
            cachedArrows = Array.Empty<NpcDirectionArrowView>();
            nextSceneObjectScanTime = 0.0f;
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
            catch (Exception)
            {
            }
        }

        private void TryFixNpcUi(NpcUiView view, VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            try
            {
                FixWorldSpaceNpcUi(view, settings, sourceCamera, headPosition, headRotation);
            }
            catch (Exception)
            {
            }
        }

        private void TryFixArrow(NpcDirectionArrowView arrow, VrRuntimeSettings settings, Camera sourceCamera, Vector3 headPosition, Quaternion headRotation)
        {
            try
            {
                FixScreenSpaceArrow(arrow, settings, sourceCamera, headPosition, headRotation);
            }
            catch (Exception)
            {
            }
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
            if (worldUiStates.TryGetValue(id, out var existing))
            {
                if (existing != null &&
                    existing.CanvasObject != null &&
                    existing.CanvasRect != null &&
                    existing.CloneRect != null &&
                    existing.SourceObject == rect.gameObject)
                {
                    return existing;
                }

                CleanupWorldUiState(existing);
                worldUiStates.Remove(id);
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
                    CleanupWorldUiState(state);
                    if (removedIds == null)
                    {
                        removedIds = new List<int>();
                    }

                    removedIds.Add(entry.Key);
                    continue;
                }

                if (state.SourceObject == null)
                {
                    CleanupWorldUiState(state);
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

        private static void ClearWorldUiStates()
        {
            foreach (KeyValuePair<int, WorldUiState> entry in worldUiStates)
            {
                CleanupWorldUiState(entry.Value);
            }

            worldUiStates.Clear();
        }

        private static void CleanupWorldUiState(WorldUiState? state)
        {
            RestoreSourceUi(state, true);
            DestroyCloneObjects(state);
        }

        private static void DestroyCloneObjects(WorldUiState? state)
        {
            if (state == null)
            {
                return;
            }

            if (state.CanvasObject != null)
            {
                UnityEngine.Object.Destroy(state.CanvasObject);
            }
            else if (state.CloneObject != null)
            {
                UnityEngine.Object.Destroy(state.CloneObject);
            }
        }

        private static void ResetStaticContext()
        {
            currentSettings = null!;
            currentSourceCamera = null!;
            currentHeadPosition = Vector3.zero;
            currentHeadRotation = Quaternion.identity;
            currentContextValid = false;
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

        private static void RestoreSourceUi(WorldUiState? state)
        {
            RestoreSourceUi(state, false);
        }

        private static void RestoreSourceUi(WorldUiState? state, bool removeAddedSourceGroup)
        {
            if (state == null || state.SourceGroup == null)
            {
                return;
            }

            state.SourceGroup.alpha = state.OriginalSourceAlpha;
            state.SourceGroup.interactable = state.OriginalSourceInteractable;
            state.SourceGroup.blocksRaycasts = state.OriginalSourceBlocksRaycasts;
            state.SourceGroup.ignoreParentGroups = state.OriginalSourceIgnoreParentGroups;

            if (removeAddedSourceGroup && state.AddedSourceGroup && state.SourceGroup != null)
            {
                UnityEngine.Object.Destroy(state.SourceGroup);
                state.SourceGroup = null!;
            }
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
