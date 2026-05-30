using System;
using BepInEx.Logging;
using Common.Scripts.UI;
using ExposureUnnoticed2.ObjectUI.ChooseDildoPanelView;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3UiContextProbe
{
    private readonly ManualLogSource? logger;
    private float nextDiagnosticTime;

    public Quest3UiContextProbe(ManualLogSource? logger)
    {
        this.logger = logger;
    }

    public Quest3UiContext Detect(ModConfig settings, string inputSource)
    {
        bool pop = TryDetectPop(out var popDiagnostic);
        bool circle = TryDetectCircle(out var circleDiagnostic);

        Quest3UiContext context;
        if (pop)
        {
            context = new Quest3UiContext(Quest3UiContextKind.Pop, popDiagnostic);
            if (circle && settings.LogActiveUiOnInput.Value)
            {
                LogDiagnostic(settings, inputSource, "POP and Circle are both active; POP has priority. POP=" + popDiagnostic + " Circle=" + circleDiagnostic);
            }
        }
        else if (circle)
        {
            context = new Quest3UiContext(Quest3UiContextKind.Circle, circleDiagnostic);
        }
        else
        {
            context = Quest3UiContext.None;
        }

        if (settings.LogActiveUiOnInput.Value && context.Kind != Quest3UiContextKind.None)
        {
            LogDiagnostic(settings, inputSource, context.Kind + " active: " + context.Diagnostic);
        }

        return context;
    }

    private static bool TryDetectPop(out string diagnostic)
    {
        diagnostic = string.Empty;

        if (TryDetectChooseDildoPanel(out diagnostic))
        {
            return true;
        }

        if (TryDetectInteractMenuPanel(out diagnostic))
        {
            return true;
        }

        return false;
    }

    private static bool TryDetectChooseDildoPanel(out string diagnostic)
    {
        diagnostic = string.Empty;

        try
        {
            var panels = Resources.FindObjectsOfTypeAll<ChooseDildoPanelView>();
            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];
                if (panel == null || panel.gameObject == null || !panel.gameObject.activeInHierarchy)
                {
                    continue;
                }

                bool isActive = false;
                try
                {
                    isActive = panel.IsActivePanel();
                }
                catch
                {
                    isActive = panel.enabled;
                }

                if (isActive)
                {
                    diagnostic = "ChooseDildoPanelView path=" + BuildPath(panel.transform) + " currentSelectIndex=" + SafeSelectIndex(panel);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            diagnostic = "ChooseDildoPanelView probe failed: " + ex.Message;
        }

        try
        {
            var basePanels = Resources.FindObjectsOfTypeAll<BasePanelView>();
            for (int i = 0; i < basePanels.Length; i++)
            {
                var panel = basePanels[i];
                if (panel != null &&
                    panel.gameObject != null &&
                    panel.gameObject.activeInHierarchy &&
                    BuildPath(panel.transform).IndexOf("ChooseDildoPanel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    diagnostic = "ChooseDildoPanel fallback path=" + BuildPath(panel.transform);
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryDetectInteractMenuPanel(out string diagnostic)
    {
        diagnostic = string.Empty;

        try
        {
            var basePanels = Resources.FindObjectsOfTypeAll<BasePanelView>();
            for (int i = 0; i < basePanels.Length; i++)
            {
                var panel = basePanels[i];
                if (panel == null || panel.gameObject == null || !panel.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string path = BuildPath(panel.transform);
                if (path.IndexOf("InteractMenuPanel", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                diagnostic = "InteractMenuPanel BasePanelView path=" + path;
                return true;
            }
        }
        catch (Exception ex)
        {
            diagnostic = "InteractMenuPanel BasePanelView probe failed: " + ex.Message;
        }

        try
        {
            var transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                var transform = transforms[i];
                if (transform == null ||
                    transform.gameObject == null ||
                    !transform.gameObject.activeInHierarchy ||
                    transform.name.IndexOf("InteractMenuPanel", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                diagnostic = "InteractMenuPanel path=" + BuildPath(transform);
                return true;
            }
        }
        catch (Exception ex)
        {
            diagnostic = "InteractMenuPanel transform probe failed: " + ex.Message;
        }

        return false;
    }

    private static bool TryDetectCircle(out string diagnostic)
    {
        diagnostic = string.Empty;

        try
        {
            var instance = RingMenuParentView.Instance;
            if (instance != null && instance.gameObject != null && instance.gameObject.activeInHierarchy && instance.IsOpenRing)
            {
                diagnostic = "RingMenuParentView.Instance path=" + BuildPath(instance.transform);
                return true;
            }
        }
        catch (Exception ex)
        {
            diagnostic = "RingMenuParentView.Instance probe failed: " + ex.Message;
        }

        try
        {
            var parents = Resources.FindObjectsOfTypeAll<RingMenuParentView>();
            for (int i = 0; i < parents.Length; i++)
            {
                var parent = parents[i];
                if (parent != null && parent.gameObject != null && parent.gameObject.activeInHierarchy && parent.IsOpenRing)
                {
                    diagnostic = "RingMenuParentView path=" + BuildPath(parent.transform);
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private void LogDiagnostic(ModConfig settings, string inputSource, string message)
    {
        if (Time.unscaledTime < nextDiagnosticTime)
        {
            return;
        }

        nextDiagnosticTime = Time.unscaledTime + 1.0f;
        string selected = string.Empty;
        if (settings.LogCurrentSelectedUi.Value)
        {
            selected = " selected=" + CurrentSelectedPath();
        }

        logger?.LogInfo("[Quest3Input] scene=" + SceneManager.GetActiveScene().name + " source=" + inputSource + selected + " " + message);
    }

    private static string CurrentSelectedPath()
    {
        try
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            return selected == null ? "<none>" : BuildPath(selected.transform);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static int SafeSelectIndex(ChooseDildoPanelView panel)
    {
        try
        {
            return panel.currentSelectIndex;
        }
        catch
        {
            return -1;
        }
    }

    private static string BuildPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
