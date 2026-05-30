using System;
using Common.Scripts.UI;
using ExposureUnnoticed2.ObjectUI.ChooseDildoPanelView;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3UiContextProbe
{
    private const float ScanIntervalSeconds = 0.12f;

    private Quest3UiContext cachedContext = Quest3UiContext.None;
    private float nextScanTime;

    public Quest3UiContext Detect(Quest3InputSnapshot snapshot)
    {
        if (!snapshot.IsAnyControllerConnected)
        {
            cachedContext = Quest3UiContext.None;
            nextScanTime = 0.0f;
            return cachedContext;
        }

        if (Time.unscaledTime < nextScanTime)
        {
            return cachedContext;
        }

        nextScanTime = Time.unscaledTime + ScanIntervalSeconds;

        try
        {
            cachedContext = TryDetectPop()
                ? Quest3UiContext.Pop
                : TryDetectCircle()
                    ? Quest3UiContext.Circle
                    : Quest3UiContext.None;
        }
        catch (Exception)
        {
            cachedContext = Quest3UiContext.None;
        }

        return cachedContext;
    }

    private static bool TryDetectPop()
    {
        if (TryDetectChooseDildoPanel())
        {
            return true;
        }

        if (TryDetectInteractMenuPanel())
        {
            return true;
        }

        return false;
    }

    private static bool TryDetectChooseDildoPanel()
    {
        var panels = Resources.FindObjectsOfTypeAll<ChooseDildoPanelView>();
        for (int i = 0; i < panels.Length; i++)
        {
            var panel = panels[i];
            if (panel == null || panel.gameObject == null || !panel.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (IsPanelActive(panel))
            {
                return true;
            }
        }

        var basePanels = Resources.FindObjectsOfTypeAll<BasePanelView>();
        for (int i = 0; i < basePanels.Length; i++)
        {
            var panel = basePanels[i];
            if (panel != null &&
                panel.gameObject != null &&
                panel.gameObject.activeInHierarchy &&
                HasNameInHierarchy(panel.transform, "ChooseDildoPanel"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPanelActive(ChooseDildoPanelView panel)
    {
        try
        {
            return panel.IsActivePanel();
        }
        catch
        {
            return panel.enabled;
        }
    }

    private static bool TryDetectInteractMenuPanel()
    {
        var basePanels = Resources.FindObjectsOfTypeAll<BasePanelView>();
        for (int i = 0; i < basePanels.Length; i++)
        {
            var panel = basePanels[i];
            if (panel != null &&
                panel.gameObject != null &&
                panel.gameObject.activeInHierarchy &&
                HasNameInHierarchy(panel.transform, "InteractMenuPanel"))
            {
                return true;
            }
        }

        var transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            var transform = transforms[i];
            if (transform != null &&
                transform.gameObject != null &&
                transform.gameObject.activeInHierarchy &&
                ContainsName(transform, "InteractMenuPanel"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDetectCircle()
    {
        var instance = RingMenuParentView.Instance;
        if (instance != null && instance.gameObject != null && instance.gameObject.activeInHierarchy && instance.IsOpenRing)
        {
            return true;
        }

        var parents = Resources.FindObjectsOfTypeAll<RingMenuParentView>();
        for (int i = 0; i < parents.Length; i++)
        {
            var parent = parents[i];
            if (parent != null && parent.gameObject != null && parent.gameObject.activeInHierarchy && parent.IsOpenRing)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNameInHierarchy(Transform transform, string keyword)
    {
        Transform current = transform;
        while (current != null)
        {
            if (ContainsName(current, keyword))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool ContainsName(Transform transform, string keyword)
    {
        string name = transform == null ? string.Empty : transform.name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
