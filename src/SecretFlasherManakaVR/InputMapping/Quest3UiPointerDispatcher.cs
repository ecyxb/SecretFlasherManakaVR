using System;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3UiPointerDispatcher
{
    private const float ChildSliderStepNormalized = 0.005f;
    private const float ChildSliderInitialRepeatSeconds = 0.33f;
    private const float ChildSliderMinimumRepeatSeconds = 0.06f;
    private const float ChildSliderRepeatAcceleration = 0.75f;

    private readonly Il2CppSystem.Collections.Generic.List<RaycastResult> raycastResults = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
    private static Slider? lockedChildSlider;
    private static bool childSliderInputActive;
    private static float nextChildSliderInputTime;
    private static float childSliderRepeatSeconds = ChildSliderInitialRepeatSeconds;
    private static float childSliderDirectionSign;
    private GameObject? hoveredObject;
    private GameObject? hoveredEnterHandler;
    private GameObject? pressedObject;
    private GameObject? rawPressedObject;
    private RaycastResult pressRaycast;
    private bool eligibleForClick;

    public void Tick(Quest3VirtualInputState state)
    {
        if (!state.IsCursorMode)
        {
            ReleaseHover();
            ResetChildSliderLock();
            TryDispatchChildScrollRectScroll(state, Vector2.zero);
            return;
        }

        if (!state.UiContext.ShouldDriveChildSlider || Mathf.Abs(state.ChildSliderDelta) <= 0.001f)
        {
            ResetChildSliderLock();
        }

        try
        {
            Vector2 screenPoint = Vector2.zero;
            if (!Quest3CursorRay.TryCreate(state, out var ray) ||
                !VrUiBridge.TryRaycastCapturedScreen(ray.Origin, ray.Direction, out screenPoint))
            {
                var currentEventSystem = EventSystem.current;
                if (currentEventSystem != null)
                {
                    DispatchHover(null, CreateEventData(currentEventSystem, screenPoint));
                }
                else
                {
                    ReleaseHover();
                }

                if (state.IsMouseButtonUpFrame(0))
                {
                    DispatchPointerUp(null, screenPoint);
                }

                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            PointerEventData eventData = CreateEventData(eventSystem, screenPoint);
            RaycastResult raycast = Raycast(eventSystem, eventData);
            GameObject currentObject = raycast.gameObject;
            SafeSetCurrentRaycast(eventData, raycast);

            DispatchHover(currentObject, eventData);
            TryDriveChildSlider(state, currentObject);
            if (!TryDispatchChildScrollRectScroll(state, screenPoint))
            {
                DispatchScroll(currentObject, eventData, state.MouseScrollDelta);
            }

            if (state.IsMouseButtonDownFrame(0))
            {
                DispatchPointerDown(currentObject, eventData, raycast);
            }

            if (state.IsMouseButtonUpFrame(0))
            {
                DispatchPointerUp(currentObject, eventData);
            }
        }
        catch (Exception)
        {
        }
    }

    public void Shutdown()
    {
        ReleaseHover();
        ResetChildSliderLock();
        pressedObject = null;
        rawPressedObject = null;
        eligibleForClick = false;
        raycastResults.Clear();
    }

    private static PointerEventData CreateEventData(EventSystem eventSystem, Vector2 screenPoint)
    {
        var eventData = new PointerEventData(eventSystem);
        eventData.pointerId = -1;
        eventData.position = screenPoint;
        eventData.delta = Vector2.zero;
        eventData.button = PointerEventData.InputButton.Left;
        eventData.clickCount = 1;
        eventData.clickTime = Time.unscaledTime;
        eventData.useDragThreshold = true;
        return eventData;
    }

    private static void SafeSetCurrentRaycast(PointerEventData eventData, RaycastResult raycast)
    {
        try
        {
            eventData.pointerCurrentRaycast = raycast;
        }
        catch
        {
        }
    }

    private RaycastResult Raycast(EventSystem eventSystem, PointerEventData eventData)
    {
        raycastResults.Clear();
        eventSystem.RaycastAll(eventData, raycastResults);
        for (int i = 0; i < raycastResults.Count; i++)
        {
            RaycastResult result = raycastResults[i];
            if (result.gameObject != null && result.gameObject.activeInHierarchy)
            {
                return result;
            }
        }

        return default;
    }

    private void DispatchHover(GameObject? currentObject, PointerEventData eventData)
    {
        GameObject currentEnterHandler = currentObject == null
            ? null
            : ExecuteEvents.GetEventHandler<IPointerEnterHandler>(currentObject);
        if (hoveredObject == currentObject && hoveredEnterHandler == currentEnterHandler)
        {
            return;
        }

        if (hoveredEnterHandler != null)
        {
            ExecuteEvents.Execute(hoveredEnterHandler, eventData, ExecuteEvents.pointerExitHandler);
        }

        hoveredObject = currentObject;
        hoveredEnterHandler = currentEnterHandler;
        if (hoveredEnterHandler != null)
        {
            ExecuteEvents.Execute(hoveredEnterHandler, eventData, ExecuteEvents.pointerEnterHandler);
        }
    }

    private void ReleaseHover()
    {
        if (hoveredEnterHandler == null)
        {
            hoveredObject = null;
            return;
        }

        var eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            var eventData = CreateEventData(eventSystem, Vector2.zero);
            ExecuteEvents.Execute(hoveredEnterHandler, eventData, ExecuteEvents.pointerExitHandler);
        }

        hoveredObject = null;
        hoveredEnterHandler = null;
    }

    private static void DispatchScroll(GameObject? currentObject, PointerEventData eventData, Vector2 scrollDelta)
    {
        if (currentObject == null || scrollDelta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        eventData.scrollDelta = scrollDelta;
        ExecuteEvents.ExecuteHierarchy(currentObject, eventData, ExecuteEvents.scrollHandler);
    }

    private static bool TryDispatchChildScrollRectScroll(Quest3VirtualInputState state, Vector2 screenPoint)
    {
        if (!state.UiContext.ShouldScrollChildScrollRect || state.MouseScrollDelta.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        GameObject? root = state.UiContext.MenuPanelObject;
        if (root == null)
        {
            return false;
        }

        ScrollRect? scrollRect = FindActiveChildScrollRect(root);
        if (scrollRect == null || scrollRect.gameObject == null)
        {
            return false;
        }

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        PointerEventData eventData = CreateEventData(eventSystem, screenPoint);
        eventData.scrollDelta = state.MouseScrollDelta;
        ExecuteEvents.Execute(scrollRect.gameObject, eventData, ExecuteEvents.scrollHandler);
        return true;
    }

    private static bool TryDriveChildSlider(Quest3VirtualInputState state, GameObject? currentObject)
    {
        bool hasInput = state.UiContext.ShouldDriveChildSlider && Mathf.Abs(state.ChildSliderDelta) > 0.001f;
        if (!hasInput)
        {
            ResetChildSliderLock();
            return false;
        }

        float directionSign = Mathf.Sign(state.ChildSliderDelta);
        if (!childSliderInputActive)
        {
            GameObject? root = state.UiContext.MenuPanelObject;
            if (root == null)
            {
                ResetChildSliderLock();
                return false;
            }

            lockedChildSlider = FindFirstSliderNearRaycastObject(root, currentObject);
            if (lockedChildSlider == null)
            {
                lockedChildSlider = FindFirstChildSlider(root);
            }

            childSliderInputActive = true;
            childSliderDirectionSign = directionSign;
            childSliderRepeatSeconds = ChildSliderInitialRepeatSeconds;
            nextChildSliderInputTime = 0.0f;
        }

        if (lockedChildSlider == null)
        {
            return false;
        }

        if (!IsUsableSlider(lockedChildSlider))
        {
            ClearLockedChildSliderOnly();
            return false;
        }

        if (!Mathf.Approximately(directionSign, childSliderDirectionSign))
        {
            childSliderDirectionSign = directionSign;
            childSliderRepeatSeconds = ChildSliderInitialRepeatSeconds;
            nextChildSliderInputTime = 0.0f;
        }

        if (Time.unscaledTime < nextChildSliderInputTime)
        {
            return true;
        }

        if (!TryDriveSlider(lockedChildSlider, state.ChildSliderDelta))
        {
            ClearLockedChildSliderOnly();
            return false;
        }

        nextChildSliderInputTime = Time.unscaledTime + childSliderRepeatSeconds;
        childSliderRepeatSeconds = Mathf.Max(
            ChildSliderMinimumRepeatSeconds,
            childSliderRepeatSeconds * ChildSliderRepeatAcceleration);
        return true;
    }

    private static bool TryDriveSlider(Slider? slider, float direction)
    {
        if (!IsUsableSlider(slider))
        {
            return false;
        }

        float range = slider.maxValue - slider.minValue;
        if (range <= 0.0001f)
        {
            return false;
        }

        float step = slider.wholeNumbers
            ? Mathf.Max(1.0f, range * ChildSliderStepNormalized)
            : range * ChildSliderStepNormalized;
        slider.value = Mathf.Clamp(slider.value + direction * step, slider.minValue, slider.maxValue);
        return true;
    }

    private static Slider? FindFirstSliderNearRaycastObject(GameObject root, GameObject? currentObject)
    {
        if (currentObject == null || root.transform == null || currentObject.transform == null)
        {
            return null;
        }

        Transform rootTransform = root.transform;
        Transform transform = currentObject.transform;
        if (transform != rootTransform && !transform.IsChildOf(rootTransform))
        {
            return null;
        }

        while (transform != null && transform != rootTransform)
        {
            Slider slider = transform.GetComponent<Slider>();
            if (IsUsableSlider(slider))
            {
                return slider;
            }

            slider = FindFirstChildSlider(transform.gameObject);
            if (slider != null)
            {
                return slider;
            }

            transform = transform.parent;
        }

        return null;
    }

    private static Slider? FindFirstChildSlider(GameObject root)
    {
        var sliders = root.GetComponentsInChildren<Slider>(true);
        if (sliders == null)
        {
            return null;
        }

        for (int i = 0; i < sliders.Length; i++)
        {
            Slider slider = sliders[i];
            if (!IsUsableSlider(slider))
            {
                continue;
            }

            return slider;
        }

        return null;
    }

    private static bool IsUsableSlider(Slider? slider)
    {
        return slider != null && slider.gameObject != null && slider.gameObject.activeInHierarchy && slider.enabled && slider.interactable;
    }

    private static void ResetChildSliderLock()
    {
        lockedChildSlider = null;
        childSliderInputActive = false;
        nextChildSliderInputTime = 0.0f;
        childSliderRepeatSeconds = ChildSliderInitialRepeatSeconds;
        childSliderDirectionSign = 0.0f;
    }

    private static void ClearLockedChildSliderOnly()
    {
        lockedChildSlider = null;
        nextChildSliderInputTime = 0.0f;
        childSliderRepeatSeconds = ChildSliderInitialRepeatSeconds;
        childSliderDirectionSign = 0.0f;
    }

    private static ScrollRect? FindActiveChildScrollRect(GameObject root)
    {
        var scrollRects = root.GetComponentsInChildren<ScrollRect>(true);
        if (scrollRects == null)
        {
            return null;
        }

        ScrollRect? fallback = null;
        for (int i = 0; i < scrollRects.Length; i++)
        {
            ScrollRect scrollRect = scrollRects[i];
            if (scrollRect == null)
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = scrollRect;
            }

            if (scrollRect.gameObject != null && scrollRect.gameObject.activeInHierarchy && scrollRect.enabled)
            {
                return scrollRect;
            }
        }

        return fallback;
    }

    private void DispatchPointerDown(GameObject? currentObject, PointerEventData eventData, RaycastResult raycast)
    {
        pressedObject = null;
        rawPressedObject = currentObject;
        pressRaycast = raycast;
        eligibleForClick = currentObject != null;

        if (currentObject == null)
        {
            return;
        }

        SafePreparePressEvent(eventData, currentObject, raycast);

        GameObject pressTarget = ExecuteEvents.ExecuteHierarchy(currentObject, eventData, ExecuteEvents.pointerDownHandler);
        if (pressTarget == null)
        {
            pressTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentObject);
        }

        pressedObject = pressTarget;
        SafeSetPointerPress(eventData, pressedObject);
    }

    private void DispatchPointerUp(GameObject? currentObject, Vector2 screenPoint)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            ClearPress();
            return;
        }

        PointerEventData eventData = CreateEventData(eventSystem, screenPoint);
        DispatchPointerUp(currentObject, eventData);
    }

    private void DispatchPointerUp(GameObject? currentObject, PointerEventData eventData)
    {
        if (pressedObject != null)
        {
            SafeSetPointerPress(eventData, pressedObject);
            ExecuteEvents.Execute(pressedObject, eventData, ExecuteEvents.pointerUpHandler);
        }

        GameObject clickTarget = currentObject == null
            ? null
            : ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentObject);
        if (eligibleForClick && pressedObject != null && pressedObject == clickTarget)
        {
            ExecuteEvents.Execute(pressedObject, eventData, ExecuteEvents.pointerClickHandler);
        }

        ClearPress();
    }

    private void SafePreparePressEvent(PointerEventData eventData, GameObject currentObject, RaycastResult raycast)
    {
        try
        {
            eventData.eligibleForClick = true;
            eventData.pressPosition = eventData.position;
            eventData.pointerPressRaycast = raycast;
            eventData.rawPointerPress = currentObject;
        }
        catch (Exception)
        {
        }
    }

    private void SafeSetPointerPress(PointerEventData eventData, GameObject? target)
    {
        try
        {
            eventData.pointerPress = target;
        }
        catch (Exception)
        {
        }
    }

    private void ClearPress()
    {
        pressedObject = null;
        rawPressedObject = null;
        eligibleForClick = false;
        pressRaycast = default;
    }
}
