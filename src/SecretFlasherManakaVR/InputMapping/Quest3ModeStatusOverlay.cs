using BepInEx.Logging;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3ModeStatusOverlay
{
    private readonly ManualLogSource? logger;
    private GameObject? canvasObject;
    private RectTransform? canvasRect;
    private Text? label;
    private bool creationFailed;
    private Quest3ControllerMode displayedMode;
    private bool hasDisplayedMode;

    public Quest3ModeStatusOverlay(ManualLogSource? logger)
    {
        this.logger = logger;
    }

    public void Tick(Quest3VirtualInputState state)
    {
        if (state.IsCursorMode || !VrUiBridge.TryGetCapturedPanelPose(out var panelPosition, out var panelRotation, out var panelScale))
        {
            SetVisible(false);
            return;
        }

        EnsureCreated();
        if (canvasObject == null || canvasRect == null || label == null)
        {
            return;
        }

        if (!hasDisplayedMode || displayedMode != state.ControllerMode)
        {
            displayedMode = state.ControllerMode;
            hasDisplayedMode = true;
            label.text = ModeLabel(state.ControllerMode);
        }

        float width = Mathf.Max(0.01f, panelScale.x);
        float height = Mathf.Max(0.01f, panelScale.y);
        Vector3 normal = panelRotation * Vector3.forward;
        canvasObject.transform.SetPositionAndRotation(panelPosition - normal * 0.025f, panelRotation);

        const float canvasWidthUnits = 1000.0f;
        float worldToCanvasScale = width / canvasWidthUnits;
        float canvasHeightUnits = height / Mathf.Max(0.0001f, worldToCanvasScale);
        canvasObject.transform.localScale = Vector3.one * worldToCanvasScale;
        canvasRect.sizeDelta = new Vector2(canvasWidthUnits, canvasHeightUnits);

        float padding = canvasWidthUnits * 0.035f;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.zero;
        labelRect.pivot = Vector2.zero;
        labelRect.anchoredPosition = new Vector2(padding, padding);
        labelRect.sizeDelta = new Vector2(canvasWidthUnits * 0.45f, canvasHeightUnits * 0.08f);
        label.fontSize = Mathf.Clamp(Mathf.RoundToInt(canvasHeightUnits * 0.035f), 18, 64);
        SetVisible(true);
    }

    public void Shutdown()
    {
        if (canvasObject == null)
        {
            return;
        }

        Object.Destroy(canvasObject);
        canvasObject = null;
        canvasRect = null;
        label = null;
        hasDisplayedMode = false;
    }

    private void EnsureCreated()
    {
        if (label != null || creationFailed)
        {
            return;
        }

        try
        {
            canvasObject = new GameObject("SecretFlasherManakaVR.Quest3ModeStatusCanvas");
            Object.DontDestroyOnLoad(canvasObject);
            canvasObject.hideFlags = HideFlags.DontSave;
            canvasObject.layer = VrUiBridge.VrUiOverlayLayer;
            canvasRect = canvasObject.AddComponent<RectTransform>();

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1100;

            var labelObject = new GameObject("Mode Label");
            labelObject.hideFlags = HideFlags.DontSave;
            labelObject.layer = VrUiBridge.VrUiOverlayLayer;
            labelObject.transform.SetParent(canvasObject.transform, false);
            label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.alignment = TextAnchor.LowerLeft;
            label.color = Color.black;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = string.Empty;

            CanvasRenderer canvasRenderer = labelObject.GetComponent<CanvasRenderer>();
            if (canvasRenderer != null)
            {
                canvasRenderer.cullTransparentMesh = false;
            }

            SetVisible(false);
        }
        catch (System.Exception ex)
        {
            creationFailed = true;
            logger?.LogWarning("Quest 3 mode status overlay creation failed: " + ex.Message);
        }
    }

    private void SetVisible(bool visible)
    {
        if (canvasObject != null && canvasObject.activeSelf != visible)
        {
            canvasObject.SetActive(visible);
        }
    }

    private static string ModeLabel(Quest3ControllerMode mode)
    {
        return mode switch
        {
            Quest3ControllerMode.Mode1 => "action14 mode",
            Quest3ControllerMode.Mode2 => "action58 mode",
            _ => "normal mode"
        };
    }
}
