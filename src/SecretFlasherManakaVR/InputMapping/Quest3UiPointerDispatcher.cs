using System;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3UiPointerDispatcher
{
    private readonly Il2CppSystem.Collections.Generic.List<RaycastResult> raycastResults = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
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
            return;
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
        pressedObject = null;
        rawPressedObject = null;
        eligibleForClick = false;
        raycastResults.Clear();
    }

    private PointerEventData CreateEventData(EventSystem eventSystem, Vector2 screenPoint)
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
