using System;
using BepInEx.Logging;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3UiPointerDispatcher
{
    private readonly ManualLogSource? logger;
    private readonly Il2CppSystem.Collections.Generic.List<RaycastResult> raycastResults = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
    private GameObject? hoveredObject;
    private GameObject? hoveredEnterHandler;
    private GameObject? pressedObject;
    private GameObject? rawPressedObject;
    private RaycastResult pressRaycast;
    private bool eligibleForClick;
    private bool warned;
    private float nextLogTime;
    private string lastTargetPath = string.Empty;

    public Quest3UiPointerDispatcher(ManualLogSource? logger)
    {
        this.logger = logger;
    }

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
                Log("no EventSystem.current at screen=" + screenPoint.ToString("F1"), true);
                return;
            }

            PointerEventData eventData = CreateEventData(eventSystem, screenPoint);
            RaycastResult raycast = Raycast(eventSystem, eventData);
            GameObject currentObject = raycast.gameObject;
            SafeSetCurrentRaycast(eventData, raycast);
            LogRaycast(screenPoint, currentObject, state);

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
        catch (Exception ex)
        {
            if (!warned)
            {
                warned = true;
                logger?.LogWarning("Quest 3 UI pointer dispatch failed: " + ex);
            }
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
            Log("hover enter raw=" + PathOf(currentObject == null ? null : currentObject.transform) +
                " handler=" + PathOf(hoveredEnterHandler.transform), true);
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
            Log("pointer down no target screen=" + eventData.position.ToString("F1"), true);
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
        Log("pointer down target=" + PathOf(currentObject.transform) + " pressTarget=" + PathOf(pressedObject == null ? null : pressedObject.transform), true);
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
            Log("pointer click target=" + PathOf(pressedObject.transform), true);
        }
        else
        {
            Log("pointer up no click pressed=" + PathOf(pressedObject == null ? null : pressedObject.transform) +
                " currentClick=" + PathOf(clickTarget == null ? null : clickTarget.transform) +
                " eligible=" + eligibleForClick, true);
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
        catch (Exception ex)
        {
            Log("prepare press event skipped: " + ex.GetType().Name + " " + ex.Message, true);
        }
    }

    private void SafeSetPointerPress(PointerEventData eventData, GameObject? target)
    {
        try
        {
            eventData.pointerPress = target;
        }
        catch (Exception ex)
        {
            Log("set pointerPress skipped: " + ex.GetType().Name + " " + ex.Message, true);
        }
    }

    private void LogRaycast(Vector2 screenPoint, GameObject? currentObject, Quest3VirtualInputState state)
    {
        if (Plugin.Settings == null || !Plugin.Settings.LogInputConsumers.Value)
        {
            return;
        }

        string path = PathOf(currentObject == null ? null : currentObject.transform);
        bool changed = path != lastTargetPath;
        if (!changed && Time.unscaledTime < nextLogTime && !state.IsMouseButtonDownFrame(0) && !state.IsMouseButtonUpFrame(0))
        {
            return;
        }

        lastTargetPath = path;
        nextLogTime = Time.unscaledTime + 1.0f;
        Log("raycast screen=" + screenPoint.ToString("F1") +
            " target=" + path +
            " components=" + ComponentSummary(currentObject) +
            " r2Down=" + state.IsMouseButtonDownFrame(0) +
            " r2Up=" + state.IsMouseButtonUpFrame(0), true);
    }

    private void Log(string message, bool force)
    {
        if (!force && Time.unscaledTime < nextLogTime)
        {
            return;
        }

        logger?.LogInfo("[Quest3Input] ui-pointer " + message);
    }

    private static string ComponentSummary(GameObject? gameObject)
    {
        if (gameObject == null)
        {
            return "<none>";
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

    private static string PathOf(Transform? transform)
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

    private void ClearPress()
    {
        pressedObject = null;
        rawPressedObject = null;
        eligibleForClick = false;
        pressRaycast = default;
    }
}
