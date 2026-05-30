namespace SecretFlasherManakaVR.InputMapping;

internal enum Quest3VirtualGamepadButton
{
    Cross,
    Circle,
    Square,
    Triangle,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    L1,
    L2,
    R1,
    R2,
    R3,
    Start,
    Select
}

internal sealed class Quest3VirtualGamepadState
{
    private ulong buttons;

    public void Clear()
    {
        buttons = 0UL;
    }

    public void Press(Quest3VirtualGamepadButton button)
    {
        buttons |= Mask(button);
    }

    public bool IsPressed(Quest3VirtualGamepadButton button)
    {
        return (buttons & Mask(button)) != 0UL;
    }

    private static ulong Mask(Quest3VirtualGamepadButton button)
    {
        return 1UL << (int)button;
    }
}
