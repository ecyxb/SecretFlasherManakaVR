using System;
using System.IO;
using System.Linq;
using BepInEx;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using SecretFlasherManakaPoseLab;
using SecretFlasherManakaVR.OpenVR;
using SecretFlasherManakaVR.Runtime;
using SecretFlasherManakaVR.InputMapping;
using UnityEngine;
using System.Text.Json;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace SecretFlasherManakaVR;

internal static class ThreePointBodyController
{
    private static AvatarRig? avatar;
    private static OpenVRPose headOrigin, leftOrigin, rightOrigin;
    private static Quaternion inverseTrackingYaw;
    private static Vector3 eyeInFrame;
    private static TrackingFrame? lastFrame;
    private static bool requested, lost;
    private static bool headValid, leftValid, rightValid;
    private static float nextCalibration, lostSince, nextStatus;
    private static float scale = 1;
    private static float physicalIpd, measuredEyeSeparation;
    private static string state = "Disabled", detail = "F6: calibrate/toggle; F7: recalibrate.";
    private static readonly string Folder = Path.Combine(Paths.ConfigPath, "ManakaVRBody");
    private static readonly string CommandPath = Path.Combine(Folder, "control.json");
    private static DateTime commandWrite;
    private static float nextPoll;
    private static BodyTrackingMode? observedMode;
    internal static bool Active => avatar != null;
    internal static float TrackingWorldScale => scale;
    internal static void RecordStereo(float deviceIpd, Vector3 leftEye, Vector3 rightEye)
    {
        physicalIpd = deviceIpd;
        measuredEyeSeparation = Vector3.Distance(leftEye, rightEye);
    }

    internal static void Update()
    {
        if (Plugin.Settings == null) return;
        try
        {
            SynchronizeMode();
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + .5f;
                PollCommand();
            }
            SynchronizeMode();
            if (!TrackingModePolicy.CanCalibrate(Plugin.Settings.TrackingMode.Value))
            {
                state = Plugin.Settings.UseLegacyView ? "Legacy" : "UnsupportedTrackingMode";
                detail = Plugin.Settings.UseLegacyView ? "Original VR camera and head/body controls; no full-body IK."
                    : "Real 6/8/10/11-point device binding is not implemented. Select ThreePoint for HMD + controllers.";
                headValid = leftValid = rightValid = false;
                WriteStatus(false);
                return;
            }
            if (Input.GetKeyDown(KeyCode.F6)) Toggle();
            if (Input.GetKeyDown(KeyCode.F7)) Recalibrate();
            if (avatar != null && (avatar.Root == null || avatar.Head == null || avatar.Player != PlayerController.Instance || !avatar.Root.gameObject.activeInHierarchy))
                Release("Player changed; press F6 in the new scene.");
            if (!VrRuntimeState.IsVrReady)
            {
                if (Active) Release("SteamVR stopped; press F6 after reconnecting.");
                headValid = leftValid = rightValid = false;
                state = requested ? "WaitingForSteamVR" : "Idle";
            }
            WriteStatus(false);
        }
        catch (Exception e) { Stop(e); }
    }

    private static void SynchronizeMode()
    {
        var mode = Plugin.Settings.TrackingMode.Value;
        if (observedMode == mode) return;
        // A command is an event, not startup configuration: never replay yesterday's calibration.
        if (observedMode == null && File.Exists(CommandPath)) commandWrite = File.GetLastWriteTimeUtc(CommandPath);
        Release("Tracking mode changed; calibration cleared.");
        PlayerHeadPoseController.SuspendForBodyTracking();
        VrRuntimeState.ClearHeadPose();
        observedMode = mode;
        Plugin.Logger.LogInfo($"Tracking profile selected: {mode}; supported={TrackingModePolicy.IsSupported(mode)}.");
    }

    private static void Toggle()
    {
        if (Active || requested) Release("Body control released.");
        else { requested = true; state = "WaitingForThreeDevices"; detail = "Stand facing forward, hands down; waiting for all three valid poses."; }
        WriteStatus(true);
    }

    private static void Recalibrate()
    {
        if (!TrackingModePolicy.CanCalibrate(Plugin.Settings.TrackingMode.Value))
            throw new InvalidOperationException("Select ThreePoint before requesting calibration.");
        Release("Recalibrating after animation settles.");
        requested = true;
        nextCalibration = Time.unscaledTime + .35f;
    }

    internal static void Prepare(IOpenVRBridge bridge, OpenVRPose head)
    {
        if (!TrackingModePolicy.CanCalibrate(Plugin.Settings.TrackingMode.Value)) return;
        try
        {
            headValid = head.IsConnected && head.IsValid;
            leftValid = bridge.TryGetControllerPose(true, out var left, out _);
            rightValid = bridge.TryGetControllerPose(false, out var right, out _);
            if (!PlayerHeadPoseController.IsTrackingFirstPersonView())
            {
                if (Active) Release("Game camera detached; press F6 after returning to first person.");
                if (requested) { state = "WaitingForFirstPerson"; detail = "Enter a first-person gameplay scene before calibration."; }
                return;
            }
            bool allValid = headValid && leftValid && rightValid;
            if (!allValid)
            {
                if (Active)
                {
                    if (!lost) { lostSince = Time.unscaledTime; lost = true; }
                    state = "TrackingLost";
                    detail = "Holding last body pose for at most 0.75 s; then restoring animation.";
                    if (Time.unscaledTime - lostSince >= .75f) Release("Tracking lost; reconnect all devices and press F6.");
                }
                else if (requested) state = "WaitingForThreeDevices";
                return;
            }
            lost = false;
            if (avatar == null && requested && Time.unscaledTime >= nextCalibration)
            {
                var player = PlayerController.Instance;
                if (player == null || !player.gameObject.activeInHierarchy) { state = "WaitingForPlayer"; return; }
                PlayerHeadPoseController.SuspendForBodyTracking();
                var candidate = new AvatarRig(player);
                try
                {
                    headOrigin = head; leftOrigin = left; rightOrigin = right;
                    inverseTrackingYaw = Quaternion.Inverse(Quaternion.Euler(0, head.Rotation.eulerAngles.y, 0));
                    // Camera anchor belongs to the player root, never to a bone that this solver moves.
                    var profile = Plugin.Settings.ThreePointProfile;
                    eyeInFrame = Quaternion.Inverse(candidate.FrameRotation) * (candidate.Head.position - candidate.Root.position)
                        + new Vector3(0, profile.EyeHeightOffset.Value, profile.EyeForwardOffset.Value);
                    float heightRatio = profile.AutoScale.Value && head.Position.y > .5f
                        ? Mathf.Clamp(eyeInFrame.y / head.Position.y, .5f, 1.5f) : 1f;
                    scale = heightRatio * profile.TrackingScale.Value;
                    candidate.Acquire();
                    avatar = candidate;
                }
                catch { candidate.Release(); throw; }
                Plugin.Logger.LogInfo($"Three-point body calibrated with a SHARED head origin and anatomical hands-down basis. Tracking scale={scale:F3}; animation wrist positions are not tracking anchors.");
            }
            if (avatar == null)
            {
                state = "ReadyToCalibrate";
                detail = "All three devices tracked. In gameplay stand facing forward, hands down, and press F6.";
                return;
            }
            lastFrame = new TrackingFrame
            {
                ThreePointTracking = true, TrackingPoints = 6, PelvisFollowsHead = true,
                Head = Offset(head, headOrigin), LeftHand = HandOffset(left, leftOrigin, true), RightHand = HandOffset(right, rightOrigin, false)
            };
            lastFrame.LeftGrip = Quest3InputSystem.Current.Snapshot.Left.Grip ? 1 : 0;
            lastFrame.RightGrip = Quest3InputSystem.Current.Snapshot.Right.Grip ? 1 : 0;
            var h = lastFrame.Head;
            lastFrame.Hips = new TargetOffset { X = h.X, Y = Mathf.Clamp(h.Y * .85f, -.7f, 0), Z = h.Z };
            // Initial three-point mode follows horizontal body translation without claiming measured feet or a gait solver.
            lastFrame.LeftFoot = new TargetOffset { X = h.X, Z = h.Z };
            lastFrame.RightFoot = new TargetOffset { X = h.X, Z = h.Z };
            state = "Tracking";
            detail = "Real HMD + left/right controller input; lower body inferred. F6 releases, F7 recalibrates.";
        }
        catch (Exception e) { Stop(e); }
    }

    private static TargetOffset Offset(OpenVRPose pose, OpenVRPose origin)
    {
        var p = TrackingSpaceMath.PositionDelta(N(pose.Position), N(origin.Position), N(inverseTrackingYaw), scale);
        var q = TrackingSpaceMath.RotationDelta(N(pose.Rotation), N(origin.Rotation), N(inverseTrackingYaw));
        var e = new Quaternion(q.X, q.Y, q.Z, q.W).eulerAngles;
        return new TargetOffset { X = p.X, Y = p.Y, Z = p.Z, Pitch = e.x, Yaw = e.y, Roll = e.z };
    }

    private static TargetOffset HandOffset(OpenVRPose pose, OpenVRPose rotationOrigin, bool left)
    {
        var p = TrackingSpaceMath.HandPositionOffset(N(pose.Position), N(headOrigin.Position), N(inverseTrackingYaw),
            scale, N(eyeInFrame), N(avatar!.HandBasePosition(left)));
        var delta = TrackingSpaceMath.RotationDelta(N(pose.Rotation), N(rotationOrigin.Rotation), N(inverseTrackingYaw));
        var e = avatar.HandRotationOffset(left, new Quaternion(delta.X, delta.Y, delta.Z, delta.W)).eulerAngles;
        return new TargetOffset { X = p.X, Y = p.Y, Z = p.Z, Pitch = e.x, Yaw = e.y, Roll = e.z };
    }

    internal static bool TryView(OpenVRPose head, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero; rotation = Quaternion.identity;
        if (avatar == null || !head.IsValid) return false;
        Vector3 basePosition = avatar.ToWorld(eyeInFrame);
        Quaternion baseRotation = avatar.FrameRotation;
        VrRuntimeState.SetTrackingToWorldTransform(basePosition, baseRotation, headOrigin.Position * scale, inverseTrackingYaw, scale, true);
        return VrRuntimeState.TryTransformTrackingPose(head.Position, head.Rotation, out position, out rotation);
    }

    internal static void Apply()
    {
        if (avatar == null || lastFrame == null) return;
        try
        {
            avatar.Apply(lastFrame);
            PlayerNeckVisibilityController.SetDesired(true);
            WriteStatus(false);
        }
        catch (Exception e) { Stop(e); }
    }

    internal static void Release(string reason)
    {
        var previous = avatar; avatar = null;
        requested = false; lastFrame = null; lost = false;
        try { previous?.Release(); }
        finally { PlayerNeckVisibilityController.SetDesired(false); }
        state = "Idle"; detail = reason;
        if (previous != null) Plugin.Logger.LogInfo("Three-point body released: " + reason);
        WriteStatus(true);
    }

    private static void Stop(Exception e)
    {
        try { Release(e.Message); } catch { avatar = null; requested = false; }
        state = "Error"; detail = e.Message;
        Plugin.Logger.LogError("Three-point body stopped: " + e);
        WriteStatus(true);
    }

    private static void PollCommand()
    {
        if (!File.Exists(CommandPath)) return;
        var write = File.GetLastWriteTimeUtc(CommandPath);
        if (write == commandWrite) return;
        commandWrite = write;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CommandPath));
            string? action = doc.RootElement.GetProperty("Action").GetString();
            switch (action)
            {
                case "calibrate": Recalibrate(); break;
                case "release": Release("Released via control.json."); break;
                case "mode":
                    var value = doc.RootElement.GetProperty("Mode").GetString();
                    if (!Enum.TryParse<BodyTrackingMode>(value, true, out var mode) || !TrackingModePolicy.IsKnown(mode))
                        throw new InvalidDataException("Unknown tracking mode.");
                    Plugin.Settings.TrackingMode.Value = mode;
                    SynchronizeMode();
                    break;
                default: throw new InvalidDataException("Action must be calibrate, release or mode.");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Body command ignored: " + e.Message); }
    }

    private static void WriteStatus(bool force)
    {
        if (!force && Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + 1;
        try
        {
            Directory.CreateDirectory(Folder);
            var data = new
            {
                Time = DateTime.UtcNow, State = state, Detail = detail, Active, Requested = requested,
                SelectedMode = Plugin.Settings.TrackingMode.Value.ToString(),
                ModeSupported = TrackingModePolicy.IsSupported(Plugin.Settings.TrackingMode.Value),
                LegacyHeadControlAllowed = Plugin.Settings.AllowLegacyBones,
                CameraPolicy = Plugin.Settings.UseLegacyView ? "Legacy source camera, offsets, clamps and smoothing"
                    : Active ? "Calibrated player-root anchor; full HMD pose; shared device/stereo scale" : "Full HMD pose on source yaw; no legacy offsets, clamps, smoothing or bone control",
                InputSource = "SteamVR compositor", TrackingPoints = Active ? 3 : 0,
                Calibration = "Shared HMD origin; anatomical hands-down orientation",
                VrRuntimeState.HeadTrackingAlignmentErrorMetres,
                MenuInput = new {
                    Quest3InputSystem.Current.IsCursorMode, Quest3InputSystem.Current.Snapshot.Source,
                    Quest3InputSystem.Current.Snapshot.IsFresh,
                    LeftPose = Quest3InputSystem.Current.Snapshot.Left.HasPose,
                    RightPose = Quest3InputSystem.Current.Snapshot.Right.HasPose,
                    L3 = Quest3InputSystem.Current.Snapshot.Left.StickClick,
                    R3 = Quest3InputSystem.Current.Snapshot.Right.StickClick,
                    Quest3InputSystem.RayVisible, Quest3InputSystem.RayError,
                    RayGeometryValid = Quest3CursorRay.TryCreate(Quest3InputSystem.Current, out _),
                    UiPanelAvailable = VrUiBridge.TryGetCapturedPanelPose(out _, out _, out _)
                },
                HmdValid = headValid, LeftValid = leftValid, RightValid = rightValid,
                LowerBody = Active ? "Inferred pelvis and feet; no physical lower-body trackers" : "Game animation",
                TrackingScale = Active ? (float?)scale : null,
                PhysicalIpdMetres = Active ? (float?)physicalIpd : null,
                RenderedEyeSeparationMetres = Active ? (float?)measuredEyeSeparation : null,
                ExpectedEyeSeparationMetres = Active ? (float?)TrackingSpaceMath.EyeSeparation(physicalIpd, Plugin.Settings.IPDScale.Value, scale) : null,
                HeadErrorMetres = avatar == null ? (float?)null : avatar.HeadError,
                MaxLimbErrorMetres = avatar == null ? (float?)null : avatar.MaxLimbError,
                Targets = avatar?.Targets.Take(6).Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                Actual = avatar?.ActualPoints.Take(6).Select(v => new[] { v.x, v.y, v.z }).ToArray()
            };
            string path = Path.Combine(Folder, "status.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Body status write failed: " + e.Message); }
    }

    private static NVector N(Vector3 v) => new(v.x, v.y, v.z);
    private static NQuaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);
}
