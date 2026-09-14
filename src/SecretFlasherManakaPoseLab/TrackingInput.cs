using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace SecretFlasherManakaPoseLab;

// Coordinates are metres/degrees in the player's yaw-aligned frame, relative to calibration.
// A real tracking provider can implement this interface without changing the pose solver.
internal interface ITrackingInput { TrackingFrame Sample(float time); }

public sealed class TargetOffset
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float Roll { get; set; }
    internal Vector3 Position => new(X, Y, Z);
    internal Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, Roll);
    internal void Sanitize()
    {
        X = Limit(X, -1, 1); Y = Limit(Y, -1, 1); Z = Limit(Z, -1, 1);
        Pitch = Limit(Pitch, -120, 120); Yaw = Limit(Yaw, -150, 150); Roll = Limit(Roll, -120, 120);
    }
    internal static float Limit(float n, float min, float max) => float.IsFinite(n) ? Math.Clamp(n, min, max) : 0;
}

public sealed class TrackingFrame
{
    // Runtime-only mode: pelvis and feet are inferred, not physical tracker measurements.
    internal bool ThreePointTracking { get; set; }
    public int TrackingPoints { get; set; } = 6;
    // Three-point tracking normally infers pelvis translation from the head.
    // Disable for strict six-point experiments where the hips tracker takes precedence.
    public bool PelvisFollowsHead { get; set; } = true;
    public TargetOffset Head { get; set; } = new();
    public TargetOffset Hips { get; set; } = new();
    public TargetOffset LeftHand { get; set; } = new();
    public TargetOffset RightHand { get; set; } = new();
    public TargetOffset LeftFoot { get; set; } = new();
    public TargetOffset RightFoot { get; set; } = new();
    public TargetOffset LeftKnee { get; set; } = new();
    public TargetOffset RightKnee { get; set; } = new();
    public TargetOffset LeftElbow { get; set; } = new();
    public TargetOffset RightElbow { get; set; } = new();
    public TargetOffset Chest { get; set; } = new();
    public float LeftGrip { get; set; }
    public float RightGrip { get; set; }
    // Exact skeleton-relative paths, exported by skeleton.json. Works for non-humanoid extra bones too.
    public Dictionary<string, TargetOffset> Bones { get; set; } = new();
    public void Sanitize()
    {
        if (!TrackingLayout.IsSupported(TrackingPoints)) throw new InvalidDataException("TrackingPoints must be 6, 8, 10 or 11.");
        Head ??= new(); Hips ??= new(); LeftHand ??= new(); RightHand ??= new(); LeftFoot ??= new(); RightFoot ??= new();
        LeftKnee ??= new(); RightKnee ??= new(); LeftElbow ??= new(); RightElbow ??= new(); Chest ??= new();
        foreach (var t in AllTargets()) t.Sanitize();
        LeftGrip = TargetOffset.Limit(LeftGrip, 0, 1); RightGrip = TargetOffset.Limit(RightGrip, 0, 1);
        Bones ??= new();
        foreach (var key in Bones.Keys.ToArray()) { Bones[key] ??= new(); Bones[key].Sanitize(); }
    }
    internal TargetOffset[] AllTargets() => new[] { Head, Hips, LeftHand, RightHand, LeftFoot, RightFoot, LeftKnee, RightKnee, LeftElbow, RightElbow, Chest };
}

internal sealed class FakeTrackingInput : ITrackingInput
{
    internal TrackingFrame Manual = new();
    internal bool Demo = true;
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public TrackingFrame Sample(float time)
    {
        if (!Demo) return Manual;
        float wave = Mathf.Sin(time * 1.3f);
        float hipsY = -.08f - .06f * Mathf.Sin(time * .65f);
        bool trackChest = Manual.TrackingPoints == 11;
        return new TrackingFrame
        {
            TrackingPoints = Manual.TrackingPoints,
            PelvisFollowsHead = Manual.PelvisFollowsHead,
            // Lower the torso targets together in 11-point mode; unrelated vertical signals
            // would ask the fixed-length spine to stretch during every simulated squat.
            Head = new() { X = (trackChest ? .035f : .06f) * wave, Y = trackChest ? hipsY : -.05f + .03f * Mathf.Sin(time), Z = trackChest ? .015f : .05f, Yaw = 25 * wave, Pitch = 10 * Mathf.Sin(time * .7f) },
            Hips = new() { Y = hipsY, X = .035f * wave, Yaw = 10 * wave },
            LeftHand = new() { X = -.07f, Y = .18f + .12f * Mathf.Sin(time), Z = .23f, Roll = -25 * wave },
            RightHand = new() { X = .07f, Y = .32f + .14f * wave, Z = .22f, Roll = 35 * wave },
            LeftFoot = new() { Z = .04f * wave },
            RightFoot = new() { Z = -.04f * wave },
            LeftKnee = new() { X = -.08f - .05f * Mathf.Sin(time * .8f), Z = .16f },
            RightKnee = new() { X = .08f + .05f * Mathf.Sin(time * .8f), Z = .16f },
            LeftElbow = new() { X = -.12f, Y = .06f * wave, Z = -.12f + .08f * Mathf.Sin(time) },
            RightElbow = new() { X = .12f, Y = -.06f * wave, Z = -.12f - .08f * Mathf.Sin(time) },
            Chest = new() { X = .035f * wave, Y = hipsY, Z = .015f, Yaw = 18 * Mathf.Sin(time * .6f), Roll = 7 * wave },
            LeftGrip = .5f + .5f * Mathf.Sin(time), RightGrip = .5f + .5f * Mathf.Sin(time + 1.4f)
        };
    }
}
