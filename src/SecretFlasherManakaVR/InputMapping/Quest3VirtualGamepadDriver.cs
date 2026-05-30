using System;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3VirtualGamepadDriver
{
    private readonly ManualLogSource? logger;
    private Gamepad? gamepad;
    private bool creationFailed;
    private string lastSummary = string.Empty;

    public Quest3VirtualGamepadDriver(ManualLogSource? logger)
    {
        this.logger = logger;
    }

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

        string summary = state.Gamepad.BuildSummary();
        if (summary != lastSummary)
        {
            lastSummary = summary;
            logger?.LogInfo("[Quest3Input] virtual gamepad " + summary);
        }
    }

    public void Shutdown()
    {
        if (gamepad != null)
        {
            try
            {
                InputSystem.RemoveDevice(gamepad);
            }
            catch (Exception ex)
            {
                logger?.LogDebug("Quest 3 virtual gamepad remove failed: " + ex.Message);
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
            logger?.LogInfo("Quest 3 virtual Unity InputSystem Gamepad created: " + gamepad.displayName);
            return true;
        }
        catch (Exception ex)
        {
            creationFailed = true;
            logger?.LogWarning("Quest 3 virtual Gamepad creation failed: " + ex);
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

        var result = new GamepadState
        {
            leftStick = leftStick,
            rightStick = state.IsCursorMode ? Vector2.zero : rightStick
        };

        foreach (Quest3VirtualGamepadButton button in state.Gamepad.Buttons)
        {
            ApplyButton(ref result, button);
        }

        return result;
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
