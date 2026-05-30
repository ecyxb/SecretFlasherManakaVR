using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal enum Quest3Button
{
    A,
    B,
    X,
    Y,
    LeftMenu,
    LeftGrip,
    RightGrip,
    LeftTrigger,
    RightTrigger,
    LeftStickClick,
    RightStickClick
}

internal enum Quest3StickDirection
{
    None,
    Left,
    Right,
    Up,
    Down
}

internal readonly struct Quest3ControllerState
{
    public Quest3ControllerState(
        bool isConnected,
        bool hasPose,
        OpenVR.OpenVRPose pose,
        bool primaryButton,
        bool secondaryButton,
        bool menuButton,
        bool grip,
        bool trigger,
        float triggerValue,
        bool stickClick,
        Vector2 stick)
    {
        IsConnected = isConnected;
        HasPose = hasPose;
        Pose = pose;
        PrimaryButton = primaryButton;
        SecondaryButton = secondaryButton;
        MenuButton = menuButton;
        Grip = grip;
        Trigger = trigger;
        TriggerValue = triggerValue;
        StickClick = stickClick;
        Stick = stick;
    }

    public bool IsConnected { get; }

    public bool HasPose { get; }

    public OpenVR.OpenVRPose Pose { get; }

    public bool PrimaryButton { get; }

    public bool SecondaryButton { get; }

    public bool MenuButton { get; }

    public bool Grip { get; }

    public bool Trigger { get; }

    public float TriggerValue { get; }

    public bool StickClick { get; }

    public Vector2 Stick { get; }

    public static Quest3ControllerState Disconnected { get; } = new Quest3ControllerState(
        false,
        false,
        OpenVR.OpenVRPose.Invalid("Disconnected"),
        false,
        false,
        false,
        false,
        false,
        0.0f,
        false,
        Vector2.zero);
}

internal readonly struct Quest3InputSnapshot
{
    public Quest3InputSnapshot(Quest3ControllerState left, Quest3ControllerState right, bool isFresh, string source)
    {
        Left = left;
        Right = right;
        IsFresh = isFresh;
        Source = source ?? string.Empty;
    }

    public Quest3ControllerState Left { get; }

    public Quest3ControllerState Right { get; }

    public bool IsFresh { get; }

    public string Source { get; }

    public bool IsAnyControllerConnected => Left.IsConnected || Right.IsConnected;

    public bool IsPressed(Quest3Button button)
    {
        return button switch
        {
            Quest3Button.A => Right.PrimaryButton,
            Quest3Button.B => Right.SecondaryButton,
            Quest3Button.X => Left.PrimaryButton,
            Quest3Button.Y => Left.SecondaryButton,
            Quest3Button.LeftMenu => Left.MenuButton,
            Quest3Button.LeftGrip => Left.Grip,
            Quest3Button.RightGrip => Right.Grip,
            Quest3Button.LeftTrigger => Left.Trigger,
            Quest3Button.RightTrigger => Right.Trigger,
            Quest3Button.LeftStickClick => Left.StickClick,
            Quest3Button.RightStickClick => Right.StickClick,
            _ => false
        };
    }

    public int PressedAbxyCount
    {
        get
        {
            int count = 0;
            if (IsPressed(Quest3Button.A))
            {
                count++;
            }

            if (IsPressed(Quest3Button.B))
            {
                count++;
            }

            if (IsPressed(Quest3Button.X))
            {
                count++;
            }

            if (IsPressed(Quest3Button.Y))
            {
                count++;
            }

            return count;
        }
    }
}
