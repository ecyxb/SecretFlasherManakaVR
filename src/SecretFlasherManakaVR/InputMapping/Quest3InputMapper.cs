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
    private const float VirtualMouseScrollStep = 60.0f;
    private const float ModeButtonShortPressSeconds = 1.5f;

    private bool l3PressOverlappedR3;
    private bool r3PressOverlappedL3;
    private bool stickClickComboConsumed;
    private bool leftGripRightGripComboConsumed;
    private Quest3Button? activeMomentaryModeButton;
    private readonly bool[] suppressedFaceButtonsUntilUp = new bool[4];
    private bool suppressRightTriggerUntilUp;
    private bool suppressRightStickUntilNeutral;

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

        bool suppressRightStickClick = UpdateModes(
            snapshot,
            buttons,
            uiContext,
            settings.Quest3RightStickDeadzone.Value,
            state);
        state.ControllerMode = ControllerMode;
        state.IsCursorMode = IsCursorMode;
        ApplySuppressedInputs(snapshot, settings.Quest3RightStickDeadzone.Value, state);

        if (!IsCursorMode)
        {
            MapModeButtons(snapshot, suppressRightStickClick, state);
        }

        if (IsCursorMode)
        {
            MapCursorMode(snapshot, uiContext, settings, suppressRightStickClick, state);
            return state;
        }

        MapControllerMode(snapshot, buttons, uiContext, settings, state);
        return state;
    }

    private bool UpdateModes(
        Quest3InputSnapshot snapshot,
        Quest3ButtonTrackerSet buttons,
        Quest3UiContext uiContext,
        float rightStickDeadzone,
        Quest3VirtualInputState state)
    {
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
                activeMomentaryModeButton = null;
                SetControllerMode(Quest3ControllerMode.Mode0);

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

        var l1 = buttons[Quest3Button.LeftGrip];
        var l2 = buttons[Quest3Button.LeftTrigger];
        var r1 = buttons[Quest3Button.RightGrip];
        var r2 = buttons[Quest3Button.RightTrigger];
        bool isMenuContext = uiContext.Kind == Quest3UiContextKind.Menu;
        bool chordInputDownFrame = IsAnyFaceButtonDownFrame(buttons) || r2.IsDownFrame;
        bool chordInputDown = snapshot.PressedAbxyCount > 0 || r2.IsDown;

        if (!isMenuContext && ControllerMode == Quest3ControllerMode.Mode0 && l1.IsDown && r1.IsDown)
        {
            leftGripRightGripComboConsumed = true;
        }

        if (ControllerMode == Quest3ControllerMode.Mode0)
        {
            if (!isMenuContext && l2.IsDown && (chordInputDownFrame || (l2.IsDownFrame && chordInputDown)))
            {
                BeginMomentaryMode(Quest3Button.LeftTrigger, Quest3ControllerMode.Mode1);
            }
            else if (!isMenuContext && l1.IsDown && (chordInputDownFrame || (l1.IsDownFrame && chordInputDown)))
            {
                BeginMomentaryMode(Quest3Button.LeftGrip, Quest3ControllerMode.Mode2);
            }
        }

        if (l2.IsUpFrame)
        {
            if (activeMomentaryModeButton == Quest3Button.LeftTrigger)
            {
                EndMomentaryMode(snapshot, rightStickDeadzone);
            }
            else if (!isMenuContext && l2.WasShortPress(ModeButtonShortPressSeconds))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Start);
            }
        }

        if (l1.IsUpFrame)
        {
            if (activeMomentaryModeButton == Quest3Button.LeftGrip)
            {
                EndMomentaryMode(snapshot, rightStickDeadzone);
            }
            else if (!isMenuContext && !leftGripRightGripComboConsumed && l1.WasShortPress(ModeButtonShortPressSeconds))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.Select);
            }

            leftGripRightGripComboConsumed = false;
        }

        if (!IsMappedRightTriggerPressed(snapshot))
        {
            return suppressRightStickClick;
        }

        if (ControllerMode == Quest3ControllerMode.Mode1 && r2.IsDownFrame)
        {
            PressSemanticInput(state, Quest3SemanticInput.DrinkWater);
        }
        else if (ControllerMode == Quest3ControllerMode.Mode2 && r2.IsDownFrame)
        {
            PressSemanticInput(state, Quest3SemanticInput.EyeMask);
        }

        return suppressRightStickClick;
    }

    private void MapModeButtons(
        Quest3InputSnapshot snapshot,
        bool suppressRightStickClick,
        Quest3VirtualInputState state)
    {
        if (state.ControllerMode == Quest3ControllerMode.Mode0)
        {
            bool leftGripRightGripCombo =
                state.UiContext.Kind != Quest3UiContextKind.Menu &&
                snapshot.IsPressed(Quest3Button.LeftGrip) &&
                snapshot.IsPressed(Quest3Button.RightGrip);
            if (leftGripRightGripCombo)
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.L3);
            }
            else if (snapshot.IsPressed(Quest3Button.RightGrip))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R1);
            }

            if (IsMappedRightTriggerPressed(snapshot))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.R2);
            }

            if (!suppressRightStickClick && snapshot.IsPressed(Quest3Button.RightStickClick))
            {
                state.Gamepad.Press(Quest3VirtualGamepadButton.L1);
            }

            return;
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
        if (uiContext.Kind == Quest3UiContextKind.Menu)
        {
            MapMenuPanelSticks(snapshot, uiContext, settings, state);
            MapMenuPanelButtons(snapshot, state.Gamepad);
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
            MappedFaceButtonCount(snapshot) == 1 &&
            (IsMappedFaceButtonPressed(snapshot, Quest3Button.A) || IsMappedFaceButtonPressed(snapshot, Quest3Button.B)))
        {
            state.SwapMoveAndCameraSticks = true;
        }
    }

    private static void MapMenuPanelSticks(
        Quest3InputSnapshot snapshot,
        Quest3UiContext uiContext,
        ModConfig settings,
        Quest3VirtualInputState state)
    {
        state.LeftStick = Vector2.zero;
        state.RightStick = Vector2.zero;

        Quest3StickDirection leftDirection = ResolveStickDirection(
            snapshot.Left.Stick,
            settings.Quest3RightStickDeadzone.Value,
            settings.Quest3RightStickDiagonalGuardDegrees.Value);
        if (uiContext.ShouldScrollChildScrollRect)
        {
            AddScrollDirection(state, leftDirection);
        }

        if (!uiContext.ShouldScrollChildScrollRect || (leftDirection != Quest3StickDirection.Up && leftDirection != Quest3StickDirection.Down))
        {
            PressDPadDirection(state.Gamepad, leftDirection);
        }
    }

    private static void MapMenuPanelButtons(Quest3InputSnapshot snapshot, Quest3VirtualGamepadState gamepad)
    {
        if (snapshot.IsPressed(Quest3Button.LeftGrip))
        {
            gamepad.Press(Quest3VirtualGamepadButton.L1);
        }

        if (snapshot.IsPressed(Quest3Button.LeftTrigger))
        {
            gamepad.Press(Quest3VirtualGamepadButton.L2);
        }
    }

    private static void PressDPadDirection(Quest3VirtualGamepadState gamepad, Quest3StickDirection direction)
    {
        switch (direction)
        {
            case Quest3StickDirection.Up:
                gamepad.Press(Quest3VirtualGamepadButton.DPadUp);
                break;
            case Quest3StickDirection.Down:
                gamepad.Press(Quest3VirtualGamepadButton.DPadDown);
                break;
            case Quest3StickDirection.Left:
                gamepad.Press(Quest3VirtualGamepadButton.DPadLeft);
                break;
            case Quest3StickDirection.Right:
                gamepad.Press(Quest3VirtualGamepadButton.DPadRight);
                break;
        }
    }

    private static void AddScrollDirection(Quest3VirtualInputState state, Quest3StickDirection direction)
    {
        switch (direction)
        {
            case Quest3StickDirection.Up:
                state.AddMouseScroll(VirtualMouseScrollStep);
                break;
            case Quest3StickDirection.Down:
                state.AddMouseScroll(-VirtualMouseScrollStep);
                break;
        }
    }

    private static void AddChildSliderDirection(Quest3VirtualInputState state, Quest3StickDirection direction)
    {
        switch (direction)
        {
            case Quest3StickDirection.Left:
                state.ChildSliderDelta = -1.0f;
                break;
            case Quest3StickDirection.Right:
                state.ChildSliderDelta = 1.0f;
                break;
        }
    }

    private void BeginMomentaryMode(Quest3Button modeButton, Quest3ControllerMode mode)
    {
        activeMomentaryModeButton = modeButton;
        SetControllerMode(mode);
    }

    private void EndMomentaryMode(Quest3InputSnapshot snapshot, float rightStickDeadzone)
    {
        activeMomentaryModeButton = null;
        SetControllerMode(Quest3ControllerMode.Mode0);
        SuppressActionInputsIfHeld(snapshot);
        suppressRightStickUntilNeutral = snapshot.Right.Stick.magnitude > rightStickDeadzone;
    }

    private void SuppressActionInputsIfHeld(Quest3InputSnapshot snapshot)
    {
        SuppressFaceButtonIfHeld(snapshot, Quest3Button.A);
        SuppressFaceButtonIfHeld(snapshot, Quest3Button.B);
        SuppressFaceButtonIfHeld(snapshot, Quest3Button.X);
        SuppressFaceButtonIfHeld(snapshot, Quest3Button.Y);

        if (snapshot.IsPressed(Quest3Button.RightTrigger))
        {
            suppressRightTriggerUntilUp = true;
        }
    }

    private void SuppressFaceButtonIfHeld(Quest3InputSnapshot snapshot, Quest3Button button)
    {
        if (snapshot.IsPressed(button))
        {
            suppressedFaceButtonsUntilUp[(int)button] = true;
        }
    }

    private void ApplySuppressedInputs(Quest3InputSnapshot snapshot, float rightStickDeadzone, Quest3VirtualInputState state)
    {
        UpdateFaceButtonSuppression(snapshot, Quest3Button.A);
        UpdateFaceButtonSuppression(snapshot, Quest3Button.B);
        UpdateFaceButtonSuppression(snapshot, Quest3Button.X);
        UpdateFaceButtonSuppression(snapshot, Quest3Button.Y);
        if (suppressRightTriggerUntilUp && !snapshot.IsPressed(Quest3Button.RightTrigger))
        {
            suppressRightTriggerUntilUp = false;
        }

        if (!suppressRightStickUntilNeutral)
        {
            return;
        }

        if (snapshot.Right.Stick.magnitude <= rightStickDeadzone)
        {
            suppressRightStickUntilNeutral = false;
            return;
        }

        state.RightStick = Vector2.zero;
    }

    private void UpdateFaceButtonSuppression(Quest3InputSnapshot snapshot, Quest3Button button)
    {
        if (suppressedFaceButtonsUntilUp[(int)button] && !snapshot.IsPressed(button))
        {
            suppressedFaceButtonsUntilUp[(int)button] = false;
        }
    }

    private bool IsMappedFaceButtonPressed(Quest3InputSnapshot snapshot, Quest3Button button)
    {
        return snapshot.IsPressed(button) && !suppressedFaceButtonsUntilUp[(int)button];
    }

    private bool IsMappedRightTriggerPressed(Quest3InputSnapshot snapshot)
    {
        return snapshot.IsPressed(Quest3Button.RightTrigger) && !suppressRightTriggerUntilUp;
    }

    private int MappedFaceButtonCount(Quest3InputSnapshot snapshot)
    {
        int count = 0;
        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.A))
        {
            count++;
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.B))
        {
            count++;
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.X))
        {
            count++;
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.Y))
        {
            count++;
        }

        return count;
    }

    private static bool IsAnyFaceButtonDownFrame(Quest3ButtonTrackerSet buttons)
    {
        return buttons[Quest3Button.A].IsDownFrame ||
            buttons[Quest3Button.B].IsDownFrame ||
            buttons[Quest3Button.X].IsDownFrame ||
            buttons[Quest3Button.Y].IsDownFrame;
    }

    private void SetControllerMode(Quest3ControllerMode mode)
    {
        if (ControllerMode == mode)
        {
            return;
        }

        ControllerMode = mode;
    }

    private void MapCursorMode(
        Quest3InputSnapshot snapshot,
        Quest3UiContext uiContext,
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
            state.Gamepad.Press(uiContext.Kind == Quest3UiContextKind.Menu
                ? Quest3VirtualGamepadButton.L2
                : Quest3VirtualGamepadButton.Start);
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

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.A))
        {
            PressSemanticInput(state, Quest3SemanticInput.Confirm);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.B))
        {
            PressSemanticInput(state, Quest3SemanticInput.Cancel);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.Y))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.Square);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.X))
        {
            state.Gamepad.Press(Quest3VirtualGamepadButton.Triangle);
        }

        state.CursorStickDirection = ResolveStickDirection(
            snapshot.Left.Stick,
            settings.Quest3RightStickDeadzone.Value,
            settings.Quest3RightStickDiagonalGuardDegrees.Value);

        if (uiContext.ShouldScrollChildScrollRect)
        {
            state.LeftStick = Vector2.zero;
            AddScrollDirection(state, state.CursorStickDirection);
            if (uiContext.ShouldDriveChildSlider)
            {
                AddChildSliderDirection(state, state.CursorStickDirection);
            }
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
                break;
            case Quest3SemanticInput.Down:
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

    private void MapAbxyToDPad(Quest3InputSnapshot snapshot, Quest3VirtualGamepadState gamepad)
    {
        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.X))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadUp);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.A))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadDown);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.Y))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadLeft);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.B))
        {
            gamepad.Press(Quest3VirtualGamepadButton.DPadRight);
        }
    }

    private void MapAbxyToPsFaceButtons(Quest3InputSnapshot snapshot, Quest3VirtualGamepadState gamepad)
    {
        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.X))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Triangle);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.A))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Cross);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.Y))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Square);
        }

        if (IsMappedFaceButtonPressed(snapshot, Quest3Button.B))
        {
            gamepad.Press(Quest3VirtualGamepadButton.Circle);
        }
    }
}
