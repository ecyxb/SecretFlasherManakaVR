using System;
using System.Collections.Generic;
using Common.Scripts.UI;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.ObjectUI.InteractMenuPanel;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.Scripts.UI;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3UiContextProbe
{
    private const float ScanIntervalSeconds = 0.12f;

    private static readonly HashSet<string> MenuPanelTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "AchievementPanelView",
        "ApplyGraphicsCountDownPopupView",
        "BarberMenuView",
        "BuffPanelView",
        "BuyPanelView",
        "ChooseDildoPanelView",
        "ChooseHandcuffsPanelView",
        "ChooseHandcuffsTimerPanelView",
        "ChooseLanguageView",
        "ClosetMenuView",
        "ColorSettingPanelView",
        "CommonPopupView",
        "DroneMissionPanelView",
        "DroneReinforcePanelView",
        "FastTravelPanelView",
        "GraphicsOptionPanelView",
        "InGameMenuView",
        "InteractMenuPanelView",
        "InventoryPanelView",
        "KillTimeWaitPanelView",
        "ManualSavePanelView",
        "MissionMenuPanelView",
        "NameEditPanelView",
        "OnlineShopPanelView",
        "OptionMenuView",
        "PcMenuPanelView",
        "RankConfirmPanelView",
        "ReinforcePanelView",
        "ResultPanelView",
        "SaveDataSelectPanelView",
        "SelectDifficultyPanelView",
        "SelectSexOptionPanelView",
        "SelectSexualityTypePanelView",
        "SexMenuPanelView",
        "SkillPanelView",
        "SkillSwitchPanelView",
        "SleepSelectPanelView",
        "SystemMenuView",
        "TryClothesPanelView",
        "TutorialPanelView",
        "VibeRemoconReinforcePanelView",
        "WaitPanelView"
    };

    private static readonly HashSet<string> ChildScrollRectPanelTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "ClosetMenuView",
        "MissionMenuPanelView"
    };

    private static readonly HashSet<string> ChildSliderPanelTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "ClosetMenuView"
    };

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
            cachedContext = TryDetectMenu(out Quest3UiContext menuContext)
                ? menuContext
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

    private static bool TryDetectMenu(out Quest3UiContext context)
    {
        context = Quest3UiContext.None;

        InGameUiManager manager = InGameUiManager.Instance;
        if (manager != null)
        {
            if (TryGetMenuPanelContext(manager.GetCurrentBasePanelView(), out context))
            {
                return true;
            }

            var panelStack = manager.basePanelStack;
            if (panelStack != null)
            {
                for (int i = panelStack.Count - 1; i >= 0; i--)
                {
                    if (TryGetMenuPanelContext(panelStack[i], out context))
                    {
                        return true;
                    }
                }
            }
        }

        if (TryDetectInteractMenuPanel(out context))
        {
            return true;
        }

        if (TryDetectTitleSceneMenu())
        {
            context = Quest3UiContext.Menu;
            return true;
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

    private static bool TryDetectInteractMenuPanel(out Quest3UiContext context)
    {
        context = Quest3UiContext.None;

        InteractMenuPanelView panel = InteractMenuPanelView.Instance;
        if (panel == null || !IsPanelActive(panel))
        {
            return false;
        }

        try
        {
            if (panel.isClosed)
            {
                return false;
            }
        }
        catch
        {
        }

        context = Quest3UiContext.ForMenu("InteractMenuPanelView", panel.gameObject, false, false);
        return true;
    }

    private static bool TryDetectTitleSceneMenu()
    {
        TitleSceneView view = TitleSceneView.Instance;
        if (view == null || view.gameObject == null || !view.gameObject.activeInHierarchy || !view.enabled)
        {
            return false;
        }

        try
        {
            return view.currentPhase == TitleSceneView.Phase.Title;
        }
        catch
        {
            return true;
        }
    }

    private static bool TryGetMenuPanelContext(BasePanelView panel, out Quest3UiContext context)
    {
        context = Quest3UiContext.None;

        if (!IsPanelActive(panel))
        {
            return false;
        }

        string typeName = GetPanelTypeName(panel);
        if (!IsMenuPanelTypeName(typeName))
        {
            return false;
        }

        string simpleTypeName = GetSimpleTypeName(typeName);
        context = Quest3UiContext.ForMenu(
            typeName,
            panel.gameObject,
            ChildScrollRectPanelTypeNames.Contains(simpleTypeName),
            ChildSliderPanelTypeNames.Contains(simpleTypeName));
        return true;
    }

    private static bool IsMenuPanelTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return false;
        }

        if (MenuPanelTypeNames.Contains(typeName))
        {
            return true;
        }

        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 &&
            lastDot < typeName.Length - 1 &&
            MenuPanelTypeNames.Contains(typeName.Substring(lastDot + 1));
    }

    private static string GetSimpleTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return string.Empty;
        }

        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < typeName.Length - 1
            ? typeName.Substring(lastDot + 1)
            : typeName;
    }

    private static string GetPanelTypeName(BasePanelView panel)
    {
        try
        {
            IntPtr klass = panel.ObjectClass;
            string className = IL2CPP.il2cpp_class_get_name_(klass) ?? string.Empty;
            string namespaceName = IL2CPP.il2cpp_class_get_namespace_(klass) ?? string.Empty;
            return string.IsNullOrEmpty(namespaceName) ? className : namespaceName + "." + className;
        }
        catch
        {
            return string.Empty;
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
