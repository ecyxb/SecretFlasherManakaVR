using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SecretFlasherManakaVR.InputMapping;

internal static class SfmCustomMissionGalleryInputCompatibility
{
    private static bool locked;
    private static Vector3 lockedMousePosition;
    private static bool loggedFailure;

    public static Vector3 StabilizeVirtualMousePosition(Quest3VirtualInputState state, Vector3 candidate)
    {
        try
        {
            if (state == null || !state.IsCursorMode)
            {
                ClearLock();
                return candidate;
            }

            if (state.IsMouseButtonDownFrame(0))
            {
                if (IsGalleryImageOuterAt(candidate))
                {
                    locked = true;
                    lockedMousePosition = candidate;
                    return lockedMousePosition;
                }

                ClearLock();
                return candidate;
            }

            if (TryUseLockedPosition(state, out Vector3 lockedPosition))
            {
                return lockedPosition;
            }

            return candidate;
        }
        catch (Exception ex)
        {
            ClearLock();
            if (!loggedFailure)
            {
                loggedFailure = true;
                Plugin.Logger.LogWarning("[SFMVR Gallery] gallery click compatibility failed: " + ex.GetType().Name);
            }

            return candidate;
        }
    }

    public static bool TryUseLockedPosition(Quest3VirtualInputState state, out Vector3 mousePosition)
    {
        mousePosition = Vector3.zero;
        if (!locked)
        {
            return false;
        }

        if (state != null && (state.IsMouseButtonDown(0) || state.IsMouseButtonUpFrame(0)))
        {
            mousePosition = lockedMousePosition;
            if (state.IsMouseButtonUpFrame(0))
            {
                ClearLock();
            }

            return true;
        }

        ClearLock();
        return false;
    }

    private static bool IsGalleryImageOuterAt(Vector3 mousePosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        PointerEventData eventData = new PointerEventData(eventSystem);
        eventData.position = new Vector2(mousePosition.x, mousePosition.y);
        Il2CppSystem.Collections.Generic.List<RaycastResult> results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        eventSystem.RaycastAll(eventData, results);
        for (int i = 0; i < results.Count; i++)
        {
            GameObject gameObject = results[i].gameObject;
            if (IsGalleryImageOuter(gameObject))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGalleryImageOuter(GameObject gameObject)
    {
        if (gameObject == null ||
            !gameObject.activeInHierarchy ||
            !string.Equals(gameObject.name ?? string.Empty, "ImageOuter", StringComparison.Ordinal))
        {
            return false;
        }

        Transform transform = gameObject.transform;
        if (transform == null || transform.parent == null || !string.Equals(transform.parent.gameObject.name ?? string.Empty, "Pictures", StringComparison.Ordinal))
        {
            return false;
        }

        Transform current = transform.parent;
        while (current != null)
        {
            GameObject currentObject = current.gameObject;
            if (currentObject != null && string.Equals(currentObject.name ?? string.Empty, "GalleryApp", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void ClearLock()
    {
        locked = false;
        lockedMousePosition = Vector3.zero;
    }

}
