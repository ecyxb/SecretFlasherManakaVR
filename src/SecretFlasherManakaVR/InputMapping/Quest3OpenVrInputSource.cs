using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using SecretFlasherManakaVR.OpenVR;
using UnityEngine;
using Valve.VR;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3OpenVrInputSource
{
    private const string ActionSetPath = "/actions/quest3";
    private const string LeftHandPath = "/user/hand/left";
    private const string RightHandPath = "/user/hand/right";

    private readonly ModConfig settings;
    private bool triedActionInit;
    private bool actionInputReady;
    private bool actionReadExceptionReported;
    private string manifestPath = string.Empty;
    private ulong actionSet;
    private ulong leftHand;
    private ulong rightHand;
    private ActionHandles actions;
    private readonly VRActiveActionSet_t[] activeSets = new VRActiveActionSet_t[1];

    public Quest3OpenVrInputSource(ModConfig settings)
    {
        this.settings = settings;
    }

    public Quest3InputSnapshot Read()
    {
        if (!VrRuntimeState.IsVrReady)
        {
            return new Quest3InputSnapshot(
                Quest3ControllerState.Disconnected,
                Quest3ControllerState.Disconnected,
                false,
                "vr-not-ready");
        }

        if (TryReadActions(out var actionSnapshot))
        {
            return actionSnapshot;
        }

        return ReadLegacyControllerState();
    }

    private bool TryReadActions(out Quest3InputSnapshot snapshot)
    {
        snapshot = default;

        if (!EnsureActionInputReady())
        {
            return false;
        }

        try
        {
            activeSets[0] = new VRActiveActionSet_t
            {
                ulActionSet = actionSet,
                ulRestrictedToDevice = 0,
                ulSecondaryActionSet = 0,
                unPadding = 0,
                nPriority = 0
            };

            var input = Valve.VR.OpenVR.Input;
            var updateError = input.UpdateActionState(activeSets, (uint)Marshal.SizeOf<VRActiveActionSet_t>());
            if (updateError != EVRInputError.None)
            {
                return false;
            }

            var left = new Quest3ControllerState(
                true,
                TryGetPose(input, actions.LeftPose, leftHand, out var leftPose),
                leftPose,
                GetDigital(input, actions.X, leftHand, out var leftXActive),
                GetDigital(input, actions.Y, leftHand, out var leftYActive),
                GetDigital(input, actions.LeftMenu, leftHand, out var leftMenuActive),
                GetDigital(input, actions.LeftGrip, leftHand, out var leftGripActive),
                IsTriggerPressed(input, actions.LeftTrigger, actions.LeftTriggerValue, leftHand, settings.Quest3TriggerPressThreshold.Value, out var leftTriggerValue, out var leftTriggerActive),
                leftTriggerValue,
                GetDigital(input, actions.L3, leftHand, out var leftL3Active),
                GetAnalog2(input, actions.LeftStick, leftHand, out var leftStickActive));

            var right = new Quest3ControllerState(
                true,
                TryGetPose(input, actions.RightPose, rightHand, out var rightPose),
                rightPose,
                GetDigital(input, actions.A, rightHand, out var rightAActive),
                GetDigital(input, actions.B, rightHand, out var rightBActive),
                false,
                GetDigital(input, actions.RightGrip, rightHand, out var rightGripActive),
                IsTriggerPressed(input, actions.RightTrigger, actions.RightTriggerValue, rightHand, settings.Quest3TriggerPressThreshold.Value, out var rightTriggerValue, out var rightTriggerActive),
                rightTriggerValue,
                GetDigital(input, actions.R3, rightHand, out var rightR3Active),
                GetAnalog2(input, actions.RightStick, rightHand, out var rightStickActive));

            bool anyActionActive =
                left.HasPose || right.HasPose ||
                leftXActive || leftYActive || leftMenuActive || leftGripActive || leftTriggerActive || leftL3Active || leftStickActive ||
                rightAActive || rightBActive || rightGripActive || rightTriggerActive || rightR3Active || rightStickActive;

            if (!anyActionActive)
            {
                return false;
            }

            snapshot = new Quest3InputSnapshot(left, right, true, "steamvr-actions");
            return true;
        }
        catch (Exception ex)
        {
            if (!actionReadExceptionReported)
            {
                actionReadExceptionReported = true;
                Plugin.Logger.LogError($"Quest 3 SteamVR action read failed. Falling back to legacy controller state. {ex}");
            }

            return false;
        }
    }

    private bool EnsureActionInputReady()
    {
        if (actionInputReady)
        {
            return true;
        }

        if (triedActionInit)
        {
            return false;
        }

        triedActionInit = true;
        manifestPath = ResolveManifestPath();
        Plugin.Logger.LogInfo($"Quest 3 SteamVR input action manifest path: {manifestPath}");
        if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
        {
            Plugin.Logger.LogWarning("Quest 3 SteamVR input action manifest was not found. Falling back to legacy controller state.");
            return false;
        }

        try
        {
            var input = Valve.VR.OpenVR.Input;
            var manifestError = input.SetActionManifestPath(manifestPath);
            if (manifestError != EVRInputError.None)
            {
                Plugin.Logger.LogWarning($"Quest 3 SetActionManifestPath failed: {manifestError}. Falling back to legacy controller state.");
                return false;
            }

            if (!GetHandle(input.GetActionSetHandle, ActionSetPath, out actionSet) ||
                !GetHandle(input.GetInputSourceHandle, LeftHandPath, out leftHand) ||
                !GetHandle(input.GetInputSourceHandle, RightHandPath, out rightHand) ||
                !TryResolveActionHandles(input, out actions))
            {
                Plugin.Logger.LogWarning("Quest 3 SteamVR input action handles could not be resolved. Falling back to legacy controller state.");
                return false;
            }

            actionInputReady = true;
            Plugin.Logger.LogInfo("Quest 3 SteamVR input actions initialized.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Quest 3 SteamVR input action initialization failed. Falling back to legacy controller state. {ex}");
            return false;
        }
    }

    private string ResolveManifestPath()
    {
        string configured = settings.Quest3InputActionManifestPath.Value;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(Paths.PluginPath, "SecretFlasherManakaVR_Input", "actions.json");
    }

    private static bool TryResolveActionHandles(CVRInput input, out ActionHandles resolved)
    {
        resolved = default;
        return GetHandle(input.GetActionHandle, "/actions/quest3/in/a", out resolved.A) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/b", out resolved.B) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/x", out resolved.X) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/y", out resolved.Y) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/left_menu", out resolved.LeftMenu) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/left_grip", out resolved.LeftGrip) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/right_grip", out resolved.RightGrip) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/left_trigger", out resolved.LeftTrigger) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/right_trigger", out resolved.RightTrigger) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/left_trigger_value", out resolved.LeftTriggerValue) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/right_trigger_value", out resolved.RightTriggerValue) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/l3", out resolved.L3) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/r3", out resolved.R3) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/left_stick", out resolved.LeftStick) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/right_stick", out resolved.RightStick) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/left_pose", out resolved.LeftPose) &&
            GetHandle(input.GetActionHandle, "/actions/quest3/in/right_pose", out resolved.RightPose);
    }

    private delegate EVRInputError HandleGetter(string path, ref ulong handle);

    private static bool GetHandle(HandleGetter getter, string path, out ulong handle)
    {
        handle = 0;
        return getter(path, ref handle) == EVRInputError.None && handle != 0;
    }

    private static bool GetDigital(CVRInput input, ulong action, ulong hand, out bool active)
    {
        active = false;
        var data = new InputDigitalActionData_t();
        var error = input.GetDigitalActionData(action, ref data, (uint)Marshal.SizeOf<InputDigitalActionData_t>(), hand);
        active = error == EVRInputError.None && data.bActive;
        return active && data.bState;
    }

    private static Vector2 GetAnalog2(CVRInput input, ulong action, ulong hand, out bool active)
    {
        active = false;
        var data = new InputAnalogActionData_t();
        var error = input.GetAnalogActionData(action, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), hand);
        active = error == EVRInputError.None && data.bActive;
        return active ? new Vector2(data.x, data.y) : Vector2.zero;
    }

    private static bool IsTriggerPressed(CVRInput input, ulong digitalAction, ulong analogAction, ulong hand, float threshold, out float value, out bool active)
    {
        value = Mathf.Clamp01(GetAnalog1(input, analogAction, hand, out var analogActive));
        bool digital = GetDigital(input, digitalAction, hand, out var digitalActive);
        active = analogActive || digitalActive;
        return digital || value >= Mathf.Clamp(threshold, 0.01f, 0.95f);
    }

    private static float GetAnalog1(CVRInput input, ulong action, ulong hand, out bool active)
    {
        active = false;
        var data = new InputAnalogActionData_t();
        var error = input.GetAnalogActionData(action, ref data, (uint)Marshal.SizeOf<InputAnalogActionData_t>(), hand);
        active = error == EVRInputError.None && data.bActive;
        return active ? data.x : 0.0f;
    }

    private static bool TryGetPose(CVRInput input, ulong action, ulong hand, out OpenVRPose pose)
    {
        pose = OpenVRPose.Invalid("NoPose");
        var data = new InputPoseActionData_t();
        var error = input.GetPoseActionDataForNextFrame(
            action,
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            ref data,
            (uint)Marshal.SizeOf<InputPoseActionData_t>(),
            hand);

        if (error != EVRInputError.None || !data.bActive || !data.pose.bPoseIsValid)
        {
            return false;
        }

        pose = ToOpenVRPose(data.pose);
        return pose.IsValid;
    }

    private Quest3InputSnapshot ReadLegacyControllerState()
    {
        var system = Valve.VR.OpenVR.System;
        if (system == null)
        {
            return new Quest3InputSnapshot(
                Quest3ControllerState.Disconnected,
                Quest3ControllerState.Disconnected,
                false,
                "legacy-no-system");
        }

        var left = ReadLegacyHand(system, ETrackedControllerRole.LeftHand, true);
        var right = ReadLegacyHand(system, ETrackedControllerRole.RightHand, false);
        return new Quest3InputSnapshot(left, right, true, "legacy-controller-state");
    }

    private static Quest3ControllerState ReadLegacyHand(CVRSystem system, ETrackedControllerRole role, bool isLeft)
    {
        uint index = system.GetTrackedDeviceIndexForControllerRole(role);
        if (index == Valve.VR.OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            return Quest3ControllerState.Disconnected;
        }

        var state = new VRControllerState_t();
        var pose = new TrackedDevicePose_t();
        bool ok = system.GetControllerStateWithPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            index,
            ref state,
            (uint)Marshal.SizeOf<VRControllerState_t>(),
            ref pose);

        if (!ok)
        {
            return Quest3ControllerState.Disconnected;
        }

        bool appMenu = IsPressed(state, EVRButtonId.k_EButton_ApplicationMenu);
        bool primary = IsPressed(state, EVRButtonId.k_EButton_A);
        bool trigger = IsPressed(state, EVRButtonId.k_EButton_SteamVR_Trigger) || state.rAxis1.x > 0.55f;
        float triggerValue = Mathf.Clamp01(state.rAxis1.x);
        bool grip = IsPressed(state, EVRButtonId.k_EButton_Grip);
        bool stickClick = IsPressed(state, EVRButtonId.k_EButton_Axis0) || IsPressed(state, EVRButtonId.k_EButton_IndexController_JoyStick);
        var stick = new Vector2(state.rAxis0.x, state.rAxis0.y);

        return new Quest3ControllerState(
            true,
            pose.bPoseIsValid,
            ToOpenVRPose(pose),
            primary,
            appMenu,
            isLeft ? appMenu : false,
            grip,
            trigger,
            triggerValue,
            stickClick,
            stick);
    }

    private static bool IsPressed(VRControllerState_t state, EVRButtonId button)
    {
        return (state.ulButtonPressed & (1UL << (int)button)) != 0;
    }

    private static OpenVRPose ToOpenVRPose(TrackedDevicePose_t nativePose)
    {
        if (!nativePose.bDeviceIsConnected || !nativePose.bPoseIsValid)
        {
            return OpenVRPose.Invalid(nativePose.eTrackingResult.ToString());
        }

        var matrix = ToUnityMatrix(nativePose.mDeviceToAbsoluteTracking);
        return new OpenVRPose(
            true,
            nativePose.bDeviceIsConnected,
            MatrixPosition(matrix),
            MatrixRotation(matrix),
            matrix,
            nativePose.eTrackingResult.ToString());
    }

    private static Matrix4x4 ToUnityMatrix(HmdMatrix34_t source)
    {
        var matrix = Matrix4x4.identity;
        matrix[0, 0] = source.m0;
        matrix[0, 1] = source.m1;
        matrix[0, 2] = -source.m2;
        matrix[0, 3] = source.m3;
        matrix[1, 0] = source.m4;
        matrix[1, 1] = source.m5;
        matrix[1, 2] = -source.m6;
        matrix[1, 3] = source.m7;
        matrix[2, 0] = -source.m8;
        matrix[2, 1] = -source.m9;
        matrix[2, 2] = source.m10;
        matrix[2, 3] = -source.m11;
        matrix[3, 3] = 1.0f;
        return matrix;
    }

    private static Vector3 MatrixPosition(Matrix4x4 matrix)
    {
        return new Vector3(matrix[0, 3], matrix[1, 3], matrix[2, 3]);
    }

    private static Quaternion MatrixRotation(Matrix4x4 matrix)
    {
        var forward = new Vector3(matrix[0, 2], matrix[1, 2], matrix[2, 2]);
        var upwards = new Vector3(matrix[0, 1], matrix[1, 1], matrix[2, 1]);
        return forward.sqrMagnitude < 0.000001f || upwards.sqrMagnitude < 0.000001f
            ? Quaternion.identity
            : Quaternion.LookRotation(forward.normalized, upwards.normalized);
    }

    private struct ActionHandles
    {
        public ulong A;
        public ulong B;
        public ulong X;
        public ulong Y;
        public ulong LeftMenu;
        public ulong LeftGrip;
        public ulong RightGrip;
        public ulong LeftTrigger;
        public ulong RightTrigger;
        public ulong LeftTriggerValue;
        public ulong RightTriggerValue;
        public ulong L3;
        public ulong R3;
        public ulong LeftStick;
        public ulong RightStick;
        public ulong LeftPose;
        public ulong RightPose;
    }
}
