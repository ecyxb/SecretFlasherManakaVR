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
    private static Quest3ModeStatusOverlay? modeStatus;
    private static Quest3VirtualGamepadDriver? virtualGamepad;
    private static Quest3UiPointerDispatcher? uiPointer;
    private static readonly Quest3ButtonTrackerSet Buttons = new Quest3ButtonTrackerSet();
    private static readonly Quest3InputMapper Mapper = new Quest3InputMapper();
    private static readonly Quest3VirtualInputState State = new Quest3VirtualInputState();
    private static int lastTickFrame = -1;
    private static float nextConsumerLogTime;
    private static float nextPointerLogTime;
    private static bool previousR2;
    private static bool previousL3;
    private static bool previousR3;
    private static bool previousCursorMode;

    public static Quest3VirtualInputState Current => State;

    public static void Configure(ModConfig config, ManualLogSource? source)
    {
        settings = config;
        logger = source;
        inputSource = new Quest3OpenVrInputSource(config, source);
        uiProbe = new Quest3UiContextProbe(source);
        cursorRay = new Quest3CursorRayRenderer(source);
        modeStatus = new Quest3ModeStatusOverlay(source);
        virtualGamepad = new Quest3VirtualGamepadDriver(source);
        uiPointer = new Quest3UiPointerDispatcher(source);
        lastTickFrame = -1;
        logger?.LogInfo("Quest 3 input mapping configured.");
    }

    public static void Shutdown()
    {
        cursorRay?.Shutdown();
        modeStatus?.Shutdown();
        virtualGamepad?.Shutdown();
        uiPointer?.Shutdown();
        cursorRay = null;
        modeStatus = null;
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

        if (inputSource == null || uiProbe == null || cursorRay == null || modeStatus == null || virtualGamepad == null || uiPointer == null)
        {
            Configure(config, Plugin.Logger);
        }

        lastTickFrame = Time.frameCount;
        var snapshot = inputSource == null ? default : inputSource.Read();
        Buttons.Update(snapshot);
        var context = uiProbe == null ? Quest3UiContext.None : uiProbe.Detect(config, snapshot.Source);
        Mapper.Map(snapshot, Buttons, context, config, State);
        virtualGamepad?.Tick(State);
        UpdateVirtualMousePosition(State);
        LogPointerFrame(config, State);
        cursorRay?.Tick(State);
        modeStatus?.Tick(State);
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
        LogConsumer("IsDown", type.ToString());
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
        LogConsumer("IsDownFrame", type.ToString());
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
        LogConsumer("IsUpFrame", type.ToString());
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
        LogConsumer("mousePosition", result.ToString("F1"));
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
        LogConsumer("mouseScrollDelta", State.MouseScrollDelta.ToString("F2"));
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
        LogConsumer("Mouse ScrollWheel", State.MouseScrollDelta.y.ToString("F2"));
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

    private static void LogPointerFrame(ModConfig config, Quest3VirtualInputState state)
    {
        if (!config.LogInputConsumers.Value)
        {
            previousR2 = state.Snapshot.IsPressed(Quest3Button.RightTrigger);
            previousL3 = state.Snapshot.IsPressed(Quest3Button.LeftStickClick);
            previousR3 = state.Snapshot.IsPressed(Quest3Button.RightStickClick);
            previousCursorMode = state.IsCursorMode;
            return;
        }

        bool currentL3 = state.Snapshot.IsPressed(Quest3Button.LeftStickClick);
        bool currentR3 = state.Snapshot.IsPressed(Quest3Button.RightStickClick);
        bool currentR2 = state.Snapshot.IsPressed(Quest3Button.RightTrigger);
        bool l3Changed = currentL3 != previousL3;
        bool r3Changed = currentR3 != previousR3;
        bool r2Changed = currentR2 != previousR2;
        bool cursorChanged = state.IsCursorMode != previousCursorMode;
        bool periodic = Time.unscaledTime >= nextPointerLogTime;
        if (!l3Changed && !r3Changed && !r2Changed && !cursorChanged && !periodic)
        {
            return;
        }

        nextPointerLogTime = Time.unscaledTime + 1.0f;
        previousL3 = currentL3;
        previousR3 = currentR3;
        previousR2 = currentR2;
        previousCursorMode = state.IsCursorMode;

        string mouse = state.HasVirtualMousePosition
            ? state.VirtualMousePosition.ToString("F1")
            : "<no-vr-ui-hit>";
        string pose = state.Snapshot.Right.HasPose
            ? "pose=ok"
            : "pose=missing";
        logger?.LogInfo("[Quest3Input] pointer frame cursor=" + state.IsCursorMode +
            " mode=" + state.ControllerMode +
            " source=" + state.Snapshot.Source +
            " fresh=" + state.Snapshot.IsFresh +
            " connected=" + state.Snapshot.IsAnyControllerConnected +
            " l3=" + currentL3 +
            " r3=" + currentR3 +
            " r2=" + currentR2 +
            " r2Value=" + state.Snapshot.Right.TriggerValue.ToString("F2") +
            " r2Down=" + state.IsMouseButtonDownFrame(0) +
            " r2Up=" + state.IsMouseButtonUpFrame(0) +
            " leftStick=" + state.Snapshot.Left.Stick.ToString("F2") +
            " rightStick=" + state.Snapshot.Right.Stick.ToString("F2") +
            " mouse=" + mouse +
            " " + pose +
            " ui=" + state.UiContext.Kind);
    }

    private static void LogConsumer(string method, string input)
    {
        var config = settings ?? Plugin.Settings;
        if (config == null || !config.LogInputConsumers.Value || Time.unscaledTime < nextConsumerLogTime)
        {
            return;
        }

        nextConsumerLogTime = Time.unscaledTime + 0.5f;
        logger?.LogInfo("[Quest3Input] consumed " + method + "(" + input + ") mode=" + State.ControllerMode + " cursor=" + State.IsCursorMode + " ui=" + State.UiContext.Kind);
    }
}
