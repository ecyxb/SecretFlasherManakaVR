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

        bool consumedCursorToggle = UpdateModes(buttons, settings.Quest3LongPressSeconds.Value);
        state.ControllerMode = ControllerMode;
        state.IsCursorMode = IsCursorMode;

        if (!IsCursorMode)
        {
            MapModeButtons(snapshot, buttons, settings.Quest3LongPressSeconds.Value, consumedCursorToggle, state);
        }

        if (IsCursorMode)
        {
            MapCursorMode(snapshot, buttons, uiContext, settings, state);
            return state;
        }

        MapControllerMode(snapshot, buttons, uiContext, settings, state);
        return state;
    }

    private bool UpdateModes(Quest3ButtonTrackerSet buttons, float longPressSeconds)
    {
        bool l3 = buttons[Quest3Button.LeftStickClick].IsDownFrame;
        bool r3 = buttons[Quest3Button.RightStickClick].IsDownFrame;
        bool bothStickClicks = (l3 && buttons[Quest3Button.RightStickClick].IsDown) ||
            (r3 && buttons[Quest3Button.LeftStickClick].IsDown);

        if (bothStickClicks)
        {
            IsCursorMode = !IsCursorMode;
            if (!IsCursorMode)
            {
                ControllerMode = Quest3ControllerMode.Mode0;
            }

            return true;
        }

        if (IsCursorMode)
        {
            return false;
        }

        var l2 = buttons[Quest3Button.LeftTrigger];
        if (l2.WasLongPress(longPressSeconds))
        {
            ControllerMode = Quest3ControllerMode.Mode2;
        }
        else if (l2.WasShortPress(longPressSeconds))
        {
            ControllerMode = ControllerMode == Quest3ControllerMode.Mode0
                ? Quest3ControllerMode.Mode1
                : Quest3ControllerMode.Mode0;
        }

        return false;
    }

    private static void MapModeButtons(
        Quest3InputSnapshot snapshot,
        Quest3ButtonTrackerSet buttons,
        float longPressSeconds,
        bool suppressRightStickClick,
        Quest3VirtualInputState state)
    {
        if (state.ControllerMode == Quest3ControllerMode.Mode0)
        {
            var l1 = buttons[Quest3Button.LeftGrip];
            if (l1.WasLongPress(longPressSeconds))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Select);
            }
            else if (l1.WasShortPress(longPressSeconds))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Start);
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
            state.Gamepad.Press(Quest3VirtualGamepadButton.R3);
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
                state.Gamepad.Press(Quest3VirtualGamepadButton.Cross);
            }

            if (snapshot.IsPressed(Quest3Button.B))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.DPadDown);
            }

            if (snapshot.IsPressed(Quest3Button.A))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Circle);
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

            if (snapshot.IsPressed(Quest3Button.RightTrigger))
            {
                PressSemanticInput(state, Quest3SemanticInput.EyeMask);
            }
        }
        else if (ControllerMode == Quest3ControllerMode.Mode1)
        {
            if (snapshot.IsPressed(Quest3Button.RightGrip))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R1);
            }

            if (snapshot.IsPressed(Quest3Button.RightTrigger))
            {
                PressSemanticInput(state, Quest3SemanticInput.DrinkWater);
            }
        }
        else
        {
            MapNormalShouldersAndTriggers(snapshot, state.Gamepad, ControllerMode == Quest3ControllerMode.Mode0);
        }

        if (uiContext.Kind == Quest3UiContextKind.Circle &&
            ControllerMode != Quest3ControllerMode.Mode0 &&
            snapshot.PressedAbxyCount == 1 &&
            (snapshot.IsPressed(Quest3Button.A) || snapshot.IsPressed(Quest3Button.B)))
        {
            state.SwapMoveAndCameraSticks = true;
        }
    }

    private static void MapCursorMode(
        Quest3InputSnapshot snapshot,
        Quest3ButtonTrackerSet buttons,
        Quest3UiContext uiContext,
        ModConfig settings,
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

        if (snapshot.IsPressed(Quest3Button.A))
        {
            PressSemanticInput(state, Quest3SemanticInput.Cancel);
        }

        if (snapshot.IsPressed(Quest3Button.B))
        {
            PressSemanticInput(state, Quest3SemanticInput.Confirm);
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
        if (snapshot.IsPressed(Quest3Button.B))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadUp);
        }

        if (snapshot.IsPressed(Quest3Button.X))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadDown);
        }

        if (snapshot.IsPressed(Quest3Button.Y))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadLeft);
        }

        if (snapshot.IsPressed(Quest3Button.A))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadRight);
        }
    }

    private static void MapAbxyToPsFaceButtons(Quest3InputSnapshot snapshot, Quest3VirtualGamepadState gamepad)
    {
        if (snapshot.IsPressed(Quest3Button.X))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Cross);
        }

        if (snapshot.IsPressed(Quest3Button.A))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Circle);
        }

        if (snapshot.IsPressed(Quest3Button.Y))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Square);
        }

        if (snapshot.IsPressed(Quest3Button.B))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Triangle);
        }
    }

    private static void MapNormalShouldersAndTriggers(
        Quest3InputSnapshot snapshot,
        Quest3VirtualGamepadState gamepad,
        bool includeLeftTriggerShift)
    {
        if (includeLeftTriggerShift && snapshot.IsPressed(Quest3Button.LeftTrigger))
        {
            gamepad.Press(Quest3VirtualGamepadButton.L2);
        }

        if (snapshot.IsPressed(Quest3Button.RightGrip))
        {
            gamepad.Press(Quest3VirtualGamepadButton.R1);
        }

        if (snapshot.IsPressed(Quest3Button.RightTrigger))
        {
            gamepad.Press(Quest3VirtualGamepadButton.R2);
        }
    }
}
