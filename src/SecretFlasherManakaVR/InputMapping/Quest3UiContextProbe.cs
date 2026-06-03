using System;
using Common.Scripts.UI;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.ObjectUI.ChooseDildoPanelView;
using ExposureUnnoticed2.ObjectUI.ChooseHandcuffTimer;
using ExposureUnnoticed2.ObjectUI.ChooseHandcuffsPanel;
using ExposureUnnoticed2.ObjectUI.InteractMenuPanel;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using Il2CppInterop.Runtime.InteropTypes;
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

        if (TryDetectChooseHandcuffsPanel())
        {
            return true;
        }

        if (TryDetectInteractMenuPanel())
        {
            return true;
        }

        return false;
    }

    private static bool TryDetectChooseHandcuffsPanel()
    {
        InGameUiManager manager = InGameUiManager.Instance;
        if (manager == null)
        {
            return false;
        }

        if (IsChooseHandcuffsPanelActive(manager.GetCurrentBasePanelView()))
        {
            return true;
        }

        var panelStack = manager.basePanelStack;
        if (panelStack == null)
        {
            return false;
        }

        for (int i = panelStack.Count - 1; i >= 0; i--)
        {
            if (IsChooseHandcuffsPanelActive(panelStack[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDetectChooseDildoPanel()
    {
        InGameUiManager manager = InGameUiManager.Instance;
        if (manager == null)
        {
            return false;
        }

        if (IsChooseDildoPanelActive(manager.GetCurrentBasePanelView()))
        {
            return true;
        }

        var panelStack = manager.basePanelStack;
        if (panelStack == null)
        {
            return false;
        }

        for (int i = panelStack.Count - 1; i >= 0; i--)
        {
            if (IsChooseDildoPanelActive(panelStack[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDetectCircle()
    {
        var instance = RingMenuParentView.Instance;
        if (IsOpenRing(instance))
        {
            return true;
        }

        InGameUiManager manager = InGameUiManager.Instance;
        return manager != null && IsOpenRing(manager.RingMenuParentView);
    }

    private static bool TryDetectInteractMenuPanel()
    {
        InteractMenuPanelView panel = InteractMenuPanelView.Instance;
        if (panel == null || !IsPanelActive(panel))
        {
            return false;
        }

        try
        {
            return !panel.isClosed;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsChooseDildoPanelActive(BasePanelView panel)
    {
        if (!IsPanelActive(panel))
        {
            return false;
        }

        try
        {
            return panel.Cast<ChooseDildoPanelView>() != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsChooseHandcuffsPanelActive(BasePanelView panel)
    {
        if (!IsPanelActive(panel))
        {
            return false;
        }

        if (TryCastPanel<ChooseHandcuffsPanelView>(panel))
        {
            return true;
        }

        return TryCastPanel<ChooseHandcuffsTimerPanelView>(panel);
    }

    private static bool TryCastPanel<TPanel>(BasePanelView panel)
        where TPanel : Il2CppObjectBase
    {
        try
        {
            return panel.Cast<TPanel>() != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPanelActive(BasePanelView panel)
    {
        if (panel == null || panel.gameObject == null || !panel.gameObject.activeInHierarchy)
        {
            return false;
        }

        try
        {
            return panel.IsActivePanel();
        }
        catch
        {
            return panel.enabled;
        }
    }

    private static bool IsOpenRing(RingMenuParentView view)
    {
        return view != null && view.gameObject != null && view.gameObject.activeInHierarchy && view.IsOpenRing;
    }
}
