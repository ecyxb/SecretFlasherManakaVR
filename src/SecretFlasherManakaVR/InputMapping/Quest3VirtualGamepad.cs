using System;
using System.Collections.Generic;
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
    private readonly HashSet<Quest3VirtualGamepadButton> buttons = new HashSet<Quest3VirtualGamepadButton>();

    public void Clear()
    {
        buttons.Clear();
    }

    public void Press(Quest3VirtualGamepadButton button)
    {
        buttons.Add(button);
    }

    public bool IsPressed(Quest3VirtualGamepadButton button)
    {
        return buttons.Contains(button);
    }

    public IEnumerable<Quest3VirtualGamepadButton> Buttons => buttons;

    public string BuildSummary()
    {
        if (buttons.Count == 0)
        {
            return "none";
        }

        var names = new List<string>();
        foreach (Quest3VirtualGamepadButton button in buttons)
        {
            names.Add(button.ToString());
        }

        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names.ToArray());
    }
}
