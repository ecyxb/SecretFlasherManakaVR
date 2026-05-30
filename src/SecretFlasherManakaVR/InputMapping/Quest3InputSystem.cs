using BepInEx.Logging;
using ExposureUnnoticed2.Scripts.Base;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal static class Quest3InputSystem
{
    private static ModConfig? settings;
    private static ManualLogSource? logger;
    private static Quest3OpenVrInputSource? inputSource;
    private static Quest3UiContextProbe? uiProbe;
    private static Quest3CursorRayRenderer? cursorRay;
    private static Quest3ModeStatusOverlay? modeStatusOverlay;
    private static Quest3VirtualGamepadDriver? virtualGamepad;
    private static Quest3UiPointerDispatcher? uiPointer;
    private static readonly Quest3ButtonTrackerSet Buttons = new Quest3ButtonTrackerSet();
    private static readonly Quest3InputMapper Mapper = new Quest3InputMapper();
    private static readonly Quest3VirtualInputState State = new Quest3VirtualInputState();
    private static int lastTickFrame = -1;

    public static Quest3VirtualInputState Current => State;

    public static void Configure(ModConfig config, ManualLogSource? source)
    {
        settings = config;
        logger = source;
        inputSource = new Quest3OpenVrInputSource(config, source);
        uiProbe = new Quest3UiContextProbe(source);
        cursorRay = new Quest3CursorRayRenderer(source);
        modeStatusOverlay = new Quest3ModeStatusOverlay(source);
        virtualGamepad = new Quest3VirtualGamepadDriver(source);
        uiPointer = new Quest3UiPointerDispatcher(source);
        lastTickFrame = -1;
        logger?.LogInfo("Quest 3 input mapping configured.");
    }

    public static void Shutdown()
    {
        cursorRay?.Shutdown();
        modeStatusOverlay?.Shutdown();
        virtualGamepad?.Shutdown();
        uiPointer?.Shutdown();
        cursorRay = null;
        modeStatusOverlay = null;
        virtualGamepad = null;
        uiPointer = null;
        inputSource = null;
        uiProbe = null;
        settings = null;
        logger = null;
        lastTickFrame = -1;
    }

    public static void Tick()
    {
        var config = settings ?? Plugin.Settings;
        if (config == null || !config.EnableQuest3InputMapping.Value)
        {
            State.BeginFrame();
            return;
        }

        if (lastTickFrame == Time.frameCount)
        {
            return;
        }

        if (inputSource == null || uiProbe == null || cursorRay == null || modeStatusOverlay == null || virtualGamepad == null || uiPointer == null)
        {
            Configure(config, Plugin.Logger);
        }

        lastTickFrame = Time.frameCount;
        var snapshot = inputSource == null ? default : inputSource.Read();
        Buttons.Update(snapshot);
        var context = uiProbe == null ? Quest3UiContext.None : uiProbe.Detect(snapshot);
        Mapper.Map(snapshot, Buttons, context, config, State);
        virtualGamepad?.Tick(State);
        UpdateVirtualMousePosition(State);
        cursorRay?.Tick(State);
        modeStatusOverlay?.Tick(State);
        uiPointer?.Tick(State);
    }

    public static bool TryApplyInputDown(InputManager.InputType type, ref bool result)
    {
        Tick();
        if (!State.IsInputDown(type))
        {
            return false;
        }

        result = true;
        return true;
    }

    public static bool TryApplyInputDownFrame(InputManager.InputType type, ref bool result)
    {
        Tick();
        if (!State.IsInputDownFrame(type))
        {
            return false;
        }

        result = true;
        return true;
    }

    public static bool TryApplyInputUpFrame(InputManager.InputType type, ref bool result)
    {
        Tick();
        if (!State.IsInputUpFrame(type))
        {
            return false;
        }

        result = true;
        return true;
    }

    public static bool TryApplyVector2(InputManager.InputType type, ref Vector2 result)
    {
        Tick();
        return false;
    }

    public static bool TryApplyVector3(InputManager.InputType type, ref Vector3 result)
    {
        Tick();
        return false;
    }

    public static bool IsMouseButtonDown(int button)
    {
        Tick();
        return State.IsMouseButtonDown(button);
    }

    public static bool IsMouseButtonDownFrame(int button)
    {
        Tick();
        return State.IsMouseButtonDownFrame(button);
    }

    public static bool IsMouseButtonUpFrame(int button)
    {
        Tick();
        return State.IsMouseButtonUpFrame(button);
    }

    public static bool TryGetMousePosition(ref Vector3 result)
    {
        Tick();
        if (!State.HasVirtualMousePosition)
        {
            return false;
        }

        result = State.VirtualMousePosition;
        return true;
    }

    public static bool TryGetMouseScrollDelta(ref Vector2 result)
    {
        Tick();
        if (State.MouseScrollDelta.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        result += State.MouseScrollDelta;
        return true;
    }

    public static bool TryGetMouseScrollWheelAxis(ref float result)
    {
        Tick();
        if (Mathf.Abs(State.MouseScrollDelta.y) <= 0.0001f)
        {
            return false;
        }

        result += State.MouseScrollDelta.y;
        return true;
    }

    private static void UpdateVirtualMousePosition(Quest3VirtualInputState state)
    {
        if (!Quest3CursorRay.TryCreate(state, out var ray) ||
            !VrUiBridge.TryRaycastCapturedScreen(ray.Origin, ray.Direction, out var screenPoint))
        {
            return;
        }

        state.HasVirtualMousePosition = true;
        state.VirtualMousePosition = new Vector3(screenPoint.x, screenPoint.y, 0.0f);
    }
}
