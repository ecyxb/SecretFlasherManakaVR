using ExposureUnnoticed2.Scripts.Base;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal enum Quest3SemanticInput
{
    Left,
    Right,
    Up,
    Down,
    Confirm,
    Cancel,
    EyeMask,
    DrinkWater
}

internal sealed class Quest3InputMapper
{
    private bool trackingAbxyPress;
    private bool circleOpenedSinceAbxyDown;
    private bool l3PressOverlappedR3;
    private bool r3PressOverlappedL3;
    private bool stickClickComboConsumed;

    public Quest3ControllerMode ControllerMode { get; private set; }

    public bool IsCursorMode { get; private set; }

    public Quest3VirtualInputState Map(
        Quest3InputSnapshot snapshot,
        Quest3ButtonTrackerSet buttons,
        Quest3UiContext uiContext,
        ModConfig settings,
        Quest3VirtualInputState state)
    {
        state.BeginFrame();
        state.Snapshot = snapshot;
        state.UiContext = uiContext;
        state.LeftStick = snapshot.Left.Stick;
        state.RightStick = snapshot.Right.Stick;

        if (!snapshot.IsFresh || !snapshot.IsAnyControllerConnected)
        {
            state.ControllerMode = ControllerMode;
            state.IsCursorMode = IsCursorMode;
            return state;
        }

        bool suppressRightStickClick = UpdateModes(buttons, settings.Quest3LongPressSeconds.Value, state, out bool suppressRightTrigger);
        state.ControllerMode = ControllerMode;
        state.IsCursorMode = IsCursorMode;

        if (!IsCursorMode)
        {
            MapModeButtons(snapshot, buttons, settings.Quest3LongPressSeconds.Value, suppressRightStickClick, suppressRightTrigger, state);
        }

        if (IsCursorMode)
        {
            MapCursorMode(snapshot, settings, suppressRightStickClick, state);
            return state;
        }

        MapControllerMode(snapshot, buttons, uiContext, settings, state);
        UpdateCircleUiAutoReturn(buttons, uiContext, state);
        return state;
    }

    private bool UpdateModes(Quest3ButtonTrackerSet buttons, float longPressSeconds, Quest3VirtualInputState state, out bool suppressRightTrigger)
    {
        suppressRightTrigger = false;
        var l3 = buttons[Quest3Button.LeftStickClick];
        var r3 = buttons[Quest3Button.RightStickClick];

        if (l3.IsDownFrame)
        {
            l3PressOverlappedR3 = r3.IsDown;
        }

        if (r3.IsDownFrame)
        {
            r3PressOverlappedL3 = l3.IsDown;
        }

        if (l3.IsDown && r3.IsDown)
        {
            l3PressOverlappedR3 = true;
            r3PressOverlappedL3 = true;
            if (!stickClickComboConsumed)
            {
                IsCursorMode = !IsCursorMode;
                if (!IsCursorMode)
                {
                    SetControllerMode(Quest3ControllerMode.Mode0);
                }

                stickClickComboConsumed = true;
            }
        }

        if (l3.IsUpFrame)
        {
            if (!l3PressOverlappedR3)
            {
                SecretFlasherManakaVR.VrRuntimeState.RequestRecenter();
            }

            l3PressOverlappedR3 = false;
        }

        if (r3.IsUpFrame)
        {
            r3PressOverlappedL3 = false;
        }

        if (!l3.IsDown && !r3.IsDown)
        {
            stickClickComboConsumed = false;
        }

        bool suppressRightStickClick = r3.IsDown && (r3PressOverlappedL3 || stickClickComboConsumed);
        if (IsCursorMode)
        {
            return suppressRightStickClick;
        }

        var l2 = buttons[Quest3Button.LeftTrigger];
        var r2 = buttons[Quest3Button.RightTrigger];

        if (ControllerMode == Quest3ControllerMode.Mode1 && r2.IsDownFrame)
        {
            PressSemanticInput(state, Quest3SemanticInput.DrinkWater);
            SetControllerMode(Quest3ControllerMode.Mode0);
            suppressRightTrigger = true;
        }
        else if (ControllerMode == Quest3ControllerMode.Mode2 && r2.IsDownFrame)
        {
            PressSemanticInput(state, Quest3SemanticInput.EyeMask);
            SetControllerMode(Quest3ControllerMode.Mode0);
            suppressRightTrigger = true;
        }

        if (l2.WasLongPress(longPressSeconds))
        {
            SetControllerMode(ControllerMode == Quest3ControllerMode.Mode0 ? Quest3ControllerMode.Mode2 : Quest3ControllerMode.Mode0);
        }
        else if (l2.WasShortPress(longPressSeconds))
        {
            SetControllerMode(ControllerMode == Quest3ControllerMode.Mode0 ? Quest3ControllerMode.Mode1 : Quest3ControllerMode.Mode0);
        }

        return suppressRightStickClick;
    }

    private static void MapModeButtons(
        Quest3InputSnapshot snapshot,
        Quest3ButtonTrackerSet buttons,
        float longPressSeconds,
        bool suppressRightStickClick,
        bool suppressRightTrigger,
        Quest3VirtualInputState state)
    {
        if (state.ControllerMode == Quest3ControllerMode.Mode0)
        {
            var l1 = buttons[Quest3Button.LeftGrip];
            if (l1.WasLongPress(longPressSeconds))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Start);
            }
            else if (l1.WasShortPress(longPressSeconds))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Select);
            }

            if (snapshot.IsPressed(Quest3Button.RightGrip))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R1);
            }

            if (!suppressRightTrigger && snapshot.IsPressed(Quest3Button.RightTrigger))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R2);
            }

            if (!suppressRightStickClick && snapshot.IsPressed(Quest3Button.RightStickClick))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.L1);
            }

            return;
        }

        if (snapshot.IsPressed(Quest3Button.LeftGrip))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.L1);
        }

        if (!suppressRightStickClick && snapshot.IsPressed(Quest3Button.RightStickClick))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.L1);
        }
    }

    private void MapControllerMode(
        Quest3InputSnapshot snapshot,
        Quest3ButtonTrackerSet buttons,
        Quest3UiContext uiContext,
        ModConfig settings,
        Quest3VirtualInputState state)
    {
        if (uiContext.Kind == Quest3UiContextKind.Pop)
        {
            if (snapshot.IsPressed(Quest3Button.Y))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.DPadUp);
            }

            if (snapshot.IsPressed(Quest3Button.X))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.DPadDown);
            }

            if (snapshot.IsPressed(Quest3Button.B))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Circle);
            }

            if (snapshot.IsPressed(Quest3Button.A))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Cross);
            }

            return;
        }

        if (ControllerMode == Quest3ControllerMode.Mode1)
        {
            MapAbxyToDPad(snapshot, state.Gamepad);
        }
        else
        {
            MapAbxyToPsFaceButtons(snapshot, state.Gamepad);
        }

        if (ControllerMode == Quest3ControllerMode.Mode2)
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.L2);

            if (snapshot.IsPressed(Quest3Button.RightGrip))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R1);
            }
        }
        else if (ControllerMode == Quest3ControllerMode.Mode1)
        {
            if (snapshot.IsPressed(Quest3Button.RightGrip))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R1);
            }
        }
        if (uiContext.Kind == Quest3UiContextKind.Circle &&
            ControllerMode != Quest3ControllerMode.Mode0 &&
            snapshot.PressedAbxyCount == 1 &&
            (snapshot.IsPressed(Quest3Button.A) || snapshot.IsPressed(Quest3Button.B)))
        {
            state.SwapMoveAndCameraSticks = true;
        }
    }

    private void UpdateCircleUiAutoReturn(Quest3ButtonTrackerSet buttons, Quest3UiContext uiContext, Quest3VirtualInputState state)
    {
        if (ControllerMode == Quest3ControllerMode.Mode0)
        {
            if (trackingAbxyPress)
            {
                ClearCircleUiAutoReturnState();
            }

            return;
        }

        if (IsAnyAbxyDownFrame(buttons))
        {
            trackingAbxyPress = true;
            circleOpenedSinceAbxyDown = false;
        }

        if (trackingAbxyPress && uiContext.Kind == Quest3UiContextKind.Circle)
        {
            if (!circleOpenedSinceAbxyDown)
            {
                circleOpenedSinceAbxyDown = true;
            }
        }

        if (trackingAbxyPress && IsAnyAbxyUpFrame(buttons))
        {
            if (!circleOpenedSinceAbxyDown)
            {
                SetControllerMode(Quest3ControllerMode.Mode0);
            }
            else
            {
                ClearCircleUiAutoReturnState();
            }

            state.ControllerMode = ControllerMode;
        }
    }

    private static bool IsAnyAbxyDownFrame(Quest3ButtonTrackerSet buttons)
    {
        return buttons[Quest3Button.A].IsDownFrame ||
            buttons[Quest3Button.B].IsDownFrame ||
            buttons[Quest3Button.X].IsDownFrame ||
            buttons[Quest3Button.Y].IsDownFrame;
    }

    private static bool IsAnyAbxyUpFrame(Quest3ButtonTrackerSet buttons)
    {
        return buttons[Quest3Button.A].IsUpFrame ||
            buttons[Quest3Button.B].IsUpFrame ||
            buttons[Quest3Button.X].IsUpFrame ||
            buttons[Quest3Button.Y].IsUpFrame;
    }

    private void SetControllerMode(Quest3ControllerMode mode)
    {
        if (ControllerMode == mode)
        {
            return;
        }

        ControllerMode = mode;
        ClearCircleUiAutoReturnState();
    }

    private void ClearCircleUiAutoReturnState()
    {
        trackingAbxyPress = false;
        circleOpenedSinceAbxyDown = false;
    }

    private static void MapCursorMode(
        Quest3InputSnapshot snapshot,
        ModConfig settings,
        bool suppressRightStickClick,
        Quest3VirtualInputState state)
    {
        if (snapshot.IsPressed(Quest3Button.RightTrigger))
        {
            state.PressMouseButton(0);
            state.Press(InputManager.InputType.LeftClick);
        }

        if (snapshot.IsPressed(Quest3Button.LeftTrigger))
        {
            state.PressMouseButton(1);
            state.Press(InputManager.InputType.RightClick);
        }

        if (snapshot.IsPressed(Quest3Button.LeftGrip))
        {
            PressSemanticInput(state, Quest3SemanticInput.Left);
        }

        if (snapshot.IsPressed(Quest3Button.RightGrip))
        {
            PressSemanticInput(state, Quest3SemanticInput.Right);
        }

        if (!suppressRightStickClick && snapshot.IsPressed(Quest3Button.RightStickClick))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.L1);
        }

        if (snapshot.IsPressed(Quest3Button.A))
        {
            PressSemanticInput(state, Quest3SemanticInput.Confirm);
        }

        if (snapshot.IsPressed(Quest3Button.B))
        {
            PressSemanticInput(state, Quest3SemanticInput.Cancel);
        }

        if (snapshot.IsPressed(Quest3Button.Y))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.Square);
        }

        if (snapshot.IsPressed(Quest3Button.X))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.Triangle);
        }

        state.CursorStickDirection = ResolveStickDirection(
            snapshot.Right.Stick,
            settings.Quest3RightStickDeadzone.Value,
            settings.Quest3RightStickDiagonalGuardDegrees.Value);

        switch (state.CursorStickDirection)
        {
            case Quest3StickDirection.Up:
                state.AddMouseScroll(1.0f);
                PressSemanticInput(state, Quest3SemanticInput.Up);
                break;
            case Quest3StickDirection.Down:
                state.AddMouseScroll(-1.0f);
                PressSemanticInput(state, Quest3SemanticInput.Down);
                break;
        }
    }

    private static Quest3StickDirection ResolveStickDirection(Vector2 stick, float deadzone, float diagonalGuardDegrees)
    {
        if (stick.magnitude < deadzone)
        {
            return Quest3StickDirection.None;
        }

        float absX = Mathf.Abs(stick.x);
        float absY = Mathf.Abs(stick.y);
        float angleFromHorizontal = Mathf.Atan2(absY, absX) * Mathf.Rad2Deg;
        if (Mathf.Abs(angleFromHorizontal - 45.0f) <= diagonalGuardDegrees)
        {
            return Quest3StickDirection.None;
        }

        if (absX > absY)
        {
            return stick.x < 0.0f ? Quest3StickDirection.Left : Quest3StickDirection.Right;
        }

        return stick.y > 0.0f ? Quest3StickDirection.Up : Quest3StickDirection.Down;
    }

    private static void PressSemanticInput(Quest3VirtualInputState state, Quest3SemanticInput input)
    {
        switch (input)
        {
            case Quest3SemanticInput.Left:
                PressAll(
                    state,
                    InputManager.InputType.UiRingLeft,
                    InputManager.InputType.TabLeft,
                    InputManager.InputType.Tab2Left);
                break;
            case Quest3SemanticInput.Right:
                PressAll(
                    state,
                    InputManager.InputType.UiRingRight,
                    InputManager.InputType.TabRight,
                    InputManager.InputType.Tab2Right);
                break;
            case Quest3SemanticInput.Up:
                state.Press(InputManager.InputType.UIUp);
                break;
            case Quest3SemanticInput.Down:
                state.Press(InputManager.InputType.UIDown);
                break;
            case Quest3SemanticInput.Confirm:
                PressAll(
                    state,
                    InputManager.InputType.Interact,
                    InputManager.InputType.Accept);
                break;
            case Quest3SemanticInput.Cancel:
                PressAll(
                    state,
                    InputManager.InputType.Cancel,
                    InputManager.InputType.SystemMenu);
                break;
            case Quest3SemanticInput.EyeMask:
                state.Press(InputManager.InputType.EyeMask);
                break;
            case Quest3SemanticInput.DrinkWater:
                state.Press(InputManager.InputType.DrinkWater);
                break;
        }
    }

    private static void PressAll(Quest3VirtualInputState state, params InputManager.InputType[] types)
    {
        for (int i = 0; i < types.Length; i++)
        {
            state.Press(types[i]);
        }
    }

    private static void MapAbxyToDPad(Quest3InputSnapshot snapshot, Quest3VirtualGamepadState gamepad)
    {
        if (snapshot.IsPressed(Quest3Button.X))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadUp);
        }

        if (snapshot.IsPressed(Quest3Button.A))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadDown);
        }

        if (snapshot.IsPressed(Quest3Button.Y))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadLeft);
        }

        if (snapshot.IsPressed(Quest3Button.B))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadRight);
        }
    }

    private static void MapAbxyToPsFaceButtons(Quest3InputSnapshot snapshot, Quest3VirtualGamepadState gamepad)
    {
        if (snapshot.IsPressed(Quest3Button.X))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Triangle);
        }

        if (snapshot.IsPressed(Quest3Button.A))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Cross);
        }

        if (snapshot.IsPressed(Quest3Button.Y))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Square);
        }

        if (snapshot.IsPressed(Quest3Button.B))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Circle);
        }
    }
}
