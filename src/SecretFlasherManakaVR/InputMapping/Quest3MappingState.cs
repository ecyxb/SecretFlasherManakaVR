using System.Collections.Generic;
using ExposureUnnoticed2.Scripts.Base;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal enum Quest3ControllerMode
{
    Mode0,
    Mode1,
    Mode2
}

internal enum Quest3UiContextKind
{
    None,
    Pop,
    Circle
}

internal sealed class Quest3UiContext
{
    private Quest3UiContext(Quest3UiContextKind kind)
    {
        Kind = kind;
    }

    public Quest3UiContextKind Kind { get; }

    public static Quest3UiContext None { get; } = new Quest3UiContext(Quest3UiContextKind.None);

    public static Quest3UiContext Pop { get; } = new Quest3UiContext(Quest3UiContextKind.Pop);

    public static Quest3UiContext Circle { get; } = new Quest3UiContext(Quest3UiContextKind.Circle);
}

internal sealed class Quest3ButtonTracker
{
    private bool previous;
    private bool current;
    private float pressedAt;

    public bool IsDownFrame => current && !previous;

    public bool IsUpFrame => !current && previous;

    public bool IsDown => current;

    public float HeldSeconds => current ? Mathf.Max(0.0f, Time.unscaledTime - pressedAt) : 0.0f;

    public bool WasLongPress(float thresholdSeconds)
    {
        return IsUpFrame && Time.unscaledTime - pressedAt >= thresholdSeconds;
    }

    public bool WasShortPress(float thresholdSeconds)
    {
        return IsUpFrame && Time.unscaledTime - pressedAt < thresholdSeconds;
    }

    public void Update(bool pressed)
    {
        previous = current;
        current = pressed;
        if (current && !previous)
        {
            pressedAt = Time.unscaledTime;
        }
    }
}

internal sealed class Quest3ButtonTrackerSet
{
    private readonly Quest3ButtonTracker[] trackers;

    public Quest3ButtonTrackerSet()
    {
        trackers = new Quest3ButtonTracker[(int)Quest3Button.RightStickClick + 1];
        for (int i = 0; i < trackers.Length; i++)
        {
            trackers[i] = new Quest3ButtonTracker();
        }
    }

    public Quest3ButtonTracker this[Quest3Button button] => trackers[(int)button];

    public void Update(Quest3InputSnapshot snapshot)
    {
        for (int i = 0; i < trackers.Length; i++)
        {
            trackers[i].Update(snapshot.IsPressed((Quest3Button)i));
        }
    }
}

internal sealed class Quest3VirtualInputState
{
    private readonly HashSet<InputManager.InputType> previousInputButtons = new HashSet<InputManager.InputType>();
    private readonly HashSet<InputManager.InputType> currentInputButtons = new HashSet<InputManager.InputType>();
    private readonly HashSet<int> previousMouseButtons = new HashSet<int>();
    private readonly HashSet<int> currentMouseButtons = new HashSet<int>();

    public Quest3ControllerMode ControllerMode { get; set; }

    public bool IsCursorMode { get; set; }

    public Vector2 LeftStick { get; set; }

    public Vector2 RightStick { get; set; }

    public bool SwapMoveAndCameraSticks { get; set; }

    public Quest3StickDirection CursorStickDirection { get; set; }

    public Quest3UiContext UiContext { get; set; } = Quest3UiContext.None;

    public Quest3InputSnapshot Snapshot { get; set; }

    public Quest3VirtualGamepadState Gamepad { get; } = new Quest3VirtualGamepadState();

    public bool HasVirtualMousePosition { get; set; }

    public Vector3 VirtualMousePosition { get; set; }

    public Vector2 MouseScrollDelta { get; set; }

    public void BeginFrame()
    {
        CopySet(currentInputButtons, previousInputButtons);
        CopySet(currentMouseButtons, previousMouseButtons);
        currentInputButtons.Clear();
        currentMouseButtons.Clear();
        LeftStick = Vector2.zero;
        RightStick = Vector2.zero;
        SwapMoveAndCameraSticks = false;
        CursorStickDirection = Quest3StickDirection.None;
        UiContext = Quest3UiContext.None;
        Gamepad.Clear();
        HasVirtualMousePosition = false;
        VirtualMousePosition = Vector3.zero;
        MouseScrollDelta = Vector2.zero;
    }

    public void Press(InputManager.InputType type)
    {
        if (type == InputManager.InputType.None)
        {
            return;
        }

        currentInputButtons.Add(type);
    }

    public void PressMouseButton(int button)
    {
        currentMouseButtons.Add(button);
    }

    public void AddMouseScroll(float deltaY)
    {
        MouseScrollDelta += new Vector2(0.0f, deltaY);
    }

    public bool IsInputDown(InputManager.InputType type)
    {
        return currentInputButtons.Contains(type);
    }

    public bool IsInputDownFrame(InputManager.InputType type)
    {
        return currentInputButtons.Contains(type) && !previousInputButtons.Contains(type);
    }

    public bool IsInputUpFrame(InputManager.InputType type)
    {
        return !currentInputButtons.Contains(type) && previousInputButtons.Contains(type);
    }

    public bool IsMouseButtonDown(int button)
    {
        return currentMouseButtons.Contains(button);
    }

    public bool IsMouseButtonDownFrame(int button)
    {
        return currentMouseButtons.Contains(button) && !previousMouseButtons.Contains(button);
    }

    public bool IsMouseButtonUpFrame(int button)
    {
        return !currentMouseButtons.Contains(button) && previousMouseButtons.Contains(button);
    }

    private static void CopySet<T>(HashSet<T> source, HashSet<T> destination)
    {
        destination.Clear();
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }
}
