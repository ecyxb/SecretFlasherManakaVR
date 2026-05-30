using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3VirtualGamepadDriver
{
    private Gamepad? gamepad;
    private bool creationFailed;

    public void Tick(Quest3VirtualInputState state)
    {
        if (!EnsureDevice())
        {
            return;
        }

        Gamepad device = gamepad!;
        var gamepadState = BuildState(state);
        InputSystem.QueueStateEvent(device, gamepadState);
        device.MakeCurrent();
    }

    public void Shutdown()
    {
        if (gamepad != null)
        {
            try
            {
                InputSystem.RemoveDevice(gamepad);
            }
            catch (Exception)
            {
            }

            gamepad = null;
        }
    }

    private bool EnsureDevice()
    {
        if (gamepad != null)
        {
            return true;
        }

        if (creationFailed)
        {
            return false;
        }

        try
        {
            gamepad = InputSystem.AddDevice<Gamepad>("SecretFlasherManakaVR Gamepad");
            gamepad.MakeCurrent();
            return true;
        }
        catch (Exception)
        {
            creationFailed = true;
            return false;
        }
    }

    private static GamepadState BuildState(Quest3VirtualInputState state)
    {
        Vector2 leftStick = state.LeftStick;
        Vector2 rightStick = state.RightStick;

        if (state.SwapMoveAndCameraSticks)
        {
            leftStick = state.RightStick;
            rightStick = state.LeftStick;
        }

        bool shouldSuppressRightStickY = !state.IsCursorMode &&
            state.ControllerMode == Quest3ControllerMode.Mode0 &&
            PlayerHeadPoseController.ShouldSuppressMode0RightStickY;

        if (shouldSuppressRightStickY)
        {
            rightStick.y = 0.0f;
        }

        var result = new GamepadState
        {
            leftStick = leftStick,
            rightStick = state.IsCursorMode ? Vector2.zero : rightStick
        };

        ApplyPressedButtons(ref result, state.Gamepad);

        return result;
    }

    private static void ApplyPressedButtons(ref GamepadState state, Quest3VirtualGamepadState buttons)
    {
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.Cross);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.Circle);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.Square);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.Triangle);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.DPadUp);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.DPadDown);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.DPadLeft);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.DPadRight);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.L1);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.L2);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.R1);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.R2);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.R3);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.Start);
        ApplyButtonIfPressed(ref state, buttons, Quest3VirtualGamepadButton.Select);
    }

    private static void ApplyButtonIfPressed(ref GamepadState state, Quest3VirtualGamepadState buttons, Quest3VirtualGamepadButton button)
    {
        if (buttons.IsPressed(button))
        {
            ApplyButton(ref state, button);
        }
    }

    private static void ApplyButton(ref GamepadState state, Quest3VirtualGamepadButton button)
    {
        switch (button)
        {
            case Quest3VirtualGamepadButton.Cross:
                state = state.WithButton(GamepadButton.Cross, true);
                break;
            case Quest3VirtualGamepadButton.Circle:
                state = state.WithButton(GamepadButton.Circle, true);
                break;
            case Quest3VirtualGamepadButton.Square:
                state = state.WithButton(GamepadButton.Square, true);
                break;
            case Quest3VirtualGamepadButton.Triangle:
                state = state.WithButton(GamepadButton.Triangle, true);
                break;
            case Quest3VirtualGamepadButton.DPadUp:
                state = state.WithButton(GamepadButton.DpadUp, true);
                break;
            case Quest3VirtualGamepadButton.DPadDown:
                state = state.WithButton(GamepadButton.DpadDown, true);
                break;
            case Quest3VirtualGamepadButton.DPadLeft:
                state = state.WithButton(GamepadButton.DpadLeft, true);
                break;
            case Quest3VirtualGamepadButton.DPadRight:
                state = state.WithButton(GamepadButton.DpadRight, true);
                break;
            case Quest3VirtualGamepadButton.L1:
                state = state.WithButton(GamepadButton.LeftShoulder, true);
                break;
            case Quest3VirtualGamepadButton.L2:
                state.leftTrigger = 1.0f;
                break;
            case Quest3VirtualGamepadButton.R1:
                state = state.WithButton(GamepadButton.RightShoulder, true);
                break;
            case Quest3VirtualGamepadButton.R2:
                state.rightTrigger = 1.0f;
                break;
            case Quest3VirtualGamepadButton.R3:
                state = state.WithButton(GamepadButton.RightStick, true);
                break;
            case Quest3VirtualGamepadButton.Start:
                state = state.WithButton(GamepadButton.Start, true);
                break;
            case Quest3VirtualGamepadButton.Select:
                state = state.WithButton(GamepadButton.Select, true);
                break;
        }
    }
}
