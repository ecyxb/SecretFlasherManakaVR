using System;
using System.Linq;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace SecretFlasherManakaPoseLab;

internal sealed class AvatarRig
{
    internal sealed class Bone
    {
        internal readonly Transform Transform;
        internal readonly string Path;
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal Bone(Transform t, string path) { Transform = t; Path = path; Position = t.localPosition; Rotation = t.localRotation; Scale = t.localScale; }
        internal void Restore() { if (Transform != null) { Transform.localPosition = Position; Transform.localRotation = Rotation; Transform.localScale = Scale; } }
    }
    private sealed class Limb
    {
        internal Transform Upper = null!, Lower = null!, End = null!;
        internal Vector3 BaseTarget, BasePole, BaseJoint;
        internal Quaternion BaseRotation;
        internal float Error;
    }
    private readonly List<(Behaviour Component, bool Enabled)> drivers = new();
    private readonly List<(SkinnedMeshRenderer Renderer, bool Matrix, bool Offscreen)> meshes = new();
    private readonly List<(Transform Bone, Vector3 Axis, float Angle, bool Left)> fingers = new();
    internal readonly List<Bone> Bones = new();
    private readonly List<Bone> drivenBones = new();
    private readonly HashSet<string> previousOverrides = new();
    internal readonly PlayerController Player;
    internal readonly Transform Root, Hips, Head, Chest;
    internal readonly Animator Animator;
    private readonly Transform[] spine;
    private readonly Limb[] limbs;
    private readonly Quaternion[] neutralHandRotations;
    private readonly Vector3 baseHead;
    private readonly Quaternion baseHeadRotation;
    private readonly Quaternion baseHipsRotation;
    private readonly Vector3 baseHips;
    private readonly Vector3 baseChest;
    private readonly Quaternion baseChestRotation;
    private readonly Transform[] lowerSpine, upperSpine;
    internal float HeadError;
    internal float ChestError, ChestRotationError;
    internal int AppliedTrackingPoints = 6;
    internal readonly Vector3[] Targets = new Vector3[11];
    internal readonly Vector3[] ActualPoints = new Vector3[11];
    internal readonly float[] PointErrors = new float[11];
    internal float Height;
    internal Quaternion FrameRotation => Quaternion.Euler(0, Root.eulerAngles.y, 0);
    internal Vector3 ToWorld(Vector3 v) => Root.position + FrameRotation * v;
    private Vector3 InFrame(Vector3 v) => Quaternion.Inverse(FrameRotation) * (v - Root.position);
    internal float MaxLimbError => limbs.Max(l => l.Error);

    internal AvatarRig(PlayerController player)
    {
        Player = player;
        Root = player.transform;
        var r = player.PlayerAvatarObjectReferencer ?? throw new InvalidOperationException("Avatar references not ready.");
        Hips = r.Hip; Head = r.Head; Chest = r.Chest;
        if (Hips == null || Head == null) throw new InvalidOperationException("Player hips/head unavailable.");
        Animator = Root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.isHuman && a.GetBoneTransform(HumanBodyBones.Head) == Head)
            ?? throw new InvalidOperationException("No humanoid animator matching the player's head. No arbitrary skeleton will be modified.");
        var skeletonRoot = Hips.parent;
        var boneSet = new HashSet<int>();
        foreach (var renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var b in renderer.bones)
            {
                if (b == null || !(b == Hips || b.IsChildOf(Hips))) continue;
                if (boneSet.Add(b.GetInstanceID())) Bones.Add(new Bone(b, PathOf(b, skeletonRoot)));
            }
            meshes.Add((renderer, renderer.forceMatrixRecalculationPerRender, renderer.updateWhenOffscreen));
        }
        for (int id = 0; id < (int)HumanBodyBones.LastBone; id++)
        {
            var b = Animator.GetBoneTransform((HumanBodyBones)id);
            if (b == null) continue;
            if (boneSet.Add(b.GetInstanceID())) Bones.Add(new Bone(b, PathOf(b, skeletonRoot)));
            drivenBones.Add(Bones.First(bone => bone.Transform == b));
        }
        Bones.Sort((a, b) => { int depth = a.Path.Count(c => c == '/').CompareTo(b.Path.Count(c => c == '/')); return depth != 0 ? depth : string.CompareOrdinal(a.Path, b.Path); });
        spine = new[] { r.Spine, r.Chest, Animator.GetBoneTransform(HumanBodyBones.UpperChest), r.Neck }
            .Where(t => t != null).DistinctBy(t => t.GetInstanceID()).ToArray();
        if (Chest == null) throw new InvalidOperationException("Chest reference is required for 11-point tracking.");
        lowerSpine = spine.Where(t => t != Chest && Chest.IsChildOf(t)).ToArray();
        upperSpine = spine.Where(t => t != Chest && t.IsChildOf(Chest)).ToArray();
        limbs = new[]
        {
            MakeLimb(r.UpperArmL, r.LowerArmL, Required(HumanBodyBones.LeftHand), new Vector3(-.12f, -.12f, -.18f)),
            MakeLimb(r.UpperArmR, r.LowerArmR, Required(HumanBodyBones.RightHand), new Vector3(.12f, -.12f, -.18f)),
            MakeLimb(r.LegL, r.LowerLegL, r.FootL, new Vector3(0, 0, .5f)),
            MakeLimb(r.LegR, r.LowerLegR, r.FootR, new Vector3(0, 0, .5f))
        };
        neutralHandRotations = new[] { CaptureNeutralHandRotation(true), CaptureNeutralHandRotation(false) };
        baseHead = InFrame(Head.position); baseHeadRotation = Quaternion.Inverse(FrameRotation) * Head.rotation;
        baseHips = InFrame(Hips.position); baseHipsRotation = Quaternion.Inverse(FrameRotation) * Hips.rotation;
        baseChest = InFrame(Chest.position); baseChestRotation = Quaternion.Inverse(FrameRotation) * Chest.rotation;
        Height = Mathf.Max(1, Vector3.Distance(Head.position, (r.FootL.position + r.FootR.position) * .5f));
        CaptureFingers(true); CaptureFingers(false);
        // Discover and record first; only mutate drivers after successful skeleton validation.
        foreach (var b in Root.GetComponentsInChildren<Behaviour>(true))
        {
            string type = b.GetIl2CppType().FullName;
            if (b == Animator || type == "UnityEngine.Animations.Rigging.RigBuilder" || type == "DynamicBone")
                drivers.Add((b, b.enabled));
        }
    }

    private Transform Required(HumanBodyBones id) => Animator.GetBoneTransform(id) ?? throw new InvalidOperationException($"Missing required bone {id}.");
    internal Vector3 HandBasePosition(bool left) => limbs[left ? 0 : 1].BaseTarget;
    internal Quaternion HandRotationOffset(bool left, Quaternion trackedDelta)
    {
        int i = left ? 0 : 1;
        return trackedDelta * neutralHandRotations[i] * Quaternion.Inverse(limbs[i].BaseRotation);
    }
    private Quaternion CaptureNeutralHandRotation(bool left)
    {
        var hand = Required(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = Required(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        var index = Required(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var little = Required(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        Vector3 fingerDirection = middle.position - hand.position;
        Vector3 palmNormal = Vector3.Cross(index.position - little.position, fingerDirection) * (left ? 1 : -1);
        if (fingerDirection.sqrMagnitude < 1e-8f || palmNormal.sqrMagnitude < 1e-10f)
            throw new InvalidOperationException("Cannot determine anatomical palm frame for body calibration.");
        Quaternion currentPalm = Quaternion.LookRotation(fingerDirection.normalized, palmNormal.normalized);
        Quaternion boneFromPalm = Quaternion.Inverse(currentPalm) * hand.rotation;
        // Both hands hang down, fingers towards the floor and palms towards the thighs.
        // This basis is derived from anatomy, not from the current arm animation.
        return Quaternion.LookRotation(Vector3.down, left ? Vector3.right : Vector3.left) * boneFromPalm;
    }
    private Limb MakeLimb(Transform upper, Transform lower, Transform end, Vector3 poleOffset)
    {
        if (upper == null || lower == null || end == null) throw new InvalidOperationException("Missing limb chain.");
        if (Vector3.Distance(upper.position, lower.position) < .001f || Vector3.Distance(lower.position, end.position) < .001f)
            throw new InvalidOperationException("Limb chain has zero length.");
        return new Limb { Upper = upper, Lower = lower, End = end, BaseTarget = InFrame(end.position),
            BaseJoint = InFrame(lower.position), BasePole = InFrame(lower.position) + poleOffset, BaseRotation = Quaternion.Inverse(FrameRotation) * end.rotation };
    }

    internal void Acquire()
    {
        foreach (var d in drivers) if (d.Component != null) d.Component.enabled = false;
        foreach (var m in meshes) if (m.Renderer != null) { m.Renderer.forceMatrixRecalculationPerRender = true; m.Renderer.updateWhenOffscreen = true; }
    }

    internal void Release()
    {
        foreach (var b in drivenBones) b.Restore();
        foreach (var b in Bones) if (previousOverrides.Contains(b.Path)) b.Restore();
        foreach (var d in drivers) if (d.Component != null) d.Component.enabled = d.Enabled;
        foreach (var m in meshes) if (m.Renderer != null) { m.Renderer.forceMatrixRecalculationPerRender = m.Matrix; m.Renderer.updateWhenOffscreen = m.Offscreen; }
    }

    internal void Apply(TrackingFrame frame)
    {
        if (Root == null || Head == null || !Root.gameObject.activeInHierarchy) throw new InvalidOperationException("Player avatar was unloaded.");
        // Rebuild from calibration, even if this runs for multiple cameras in the same frame.
        foreach (var b in drivenBones) b.Restore();
        foreach (var b in Bones) if (previousOverrides.Contains(b.Path) || frame.Bones.ContainsKey(b.Path)) b.Restore();
        previousOverrides.Clear();
        foreach (var d in drivers) if (d.Component != null && d.Component.enabled) d.Component.enabled = false;
        Hips.position = ToWorld(baseHips + Vector3.ClampMagnitude(frame.Hips.Position, frame.ThreePointTracking ? 3f : .35f));
        Hips.rotation = FrameRotation * frame.Hips.Rotation * baseHipsRotation;
        AppliedTrackingPoints = frame.TrackingPoints;
        Targets[0] = ToWorld(baseHead + Vector3.ClampMagnitude(frame.Head.Position, frame.ThreePointTracking ? 3f : .4f));
        Targets[1] = Hips.position;
        Targets[10] = ToWorld(baseChest + Vector3.ClampMagnitude(frame.Chest.Position, .3f));
        bool trackChest = frame.TrackingPoints == 11;
        if (trackChest)
        {
            SolveSpine(lowerSpine, Chest, Targets[10]);
            Chest.rotation = FrameRotation * frame.Chest.Rotation * baseChestRotation;
            // Only joints ABOVE the tracked chest may solve the head. Preserve the chest orientation.
            SolveSpine(upperSpine, Head, Targets[0]);
        }
        else SolveSpine(spine, Head, Targets[0]);
        // With a chest tracker, moving the pelvis afterwards would invalidate both torso constraints.
        if (frame.PelvisFollowsHead && !trackChest)
            Hips.position += Vector3.ClampMagnitude(Targets[0] - Head.position, .25f);
        Head.rotation = FrameRotation * frame.Head.Rotation * baseHeadRotation;
        HeadError = Vector3.Distance(Head.position, Targets[0]);
        ChestError = Vector3.Distance(Chest.position, Targets[10]);
        ChestRotationError = Quaternion.Angle(Chest.rotation, FrameRotation * frame.Chest.Rotation * baseChestRotation);
        var offsets = new[] { frame.LeftHand, frame.RightHand, frame.LeftFoot, frame.RightFoot };
        var joints = new[] { frame.LeftElbow, frame.RightElbow, frame.LeftKnee, frame.RightKnee };
        for (int i = 0; i < limbs.Length; i++)
        {
            var l = limbs[i]; var o = offsets[i];
            int jointIndex = i < 2 ? 8 + i : 6 + i - 2;
            bool trackJoint = TrackingLayout.IsActive(frame.TrackingPoints, jointIndex);
            Targets[jointIndex] = ToWorld(l.BaseJoint + Vector3.ClampMagnitude(joints[i].Position, .6f));
            Vector3 pole = trackJoint ? Targets[jointIndex] : ToWorld(l.BasePole);
            if (frame.ThreePointTracking && i < 2)
                pole = l.Upper.position + FrameRotation * new Vector3(i == 0 ? -.35f : .35f, -.25f, -.25f);
            Vector3 target = ToWorld(l.BaseTarget + Vector3.ClampMagnitude(o.Position, frame.ThreePointTracking ? 3f : i < 2 ? .8f : .4f));
            Targets[i + 2] = target;
            Vector3 a = l.Upper.position, b = l.Lower.position, c = l.End.position;
            var solved = PoseMath.Solve(N(a), N(target), N(pole), Vector3.Distance(a, b), Vector3.Distance(b, c), N(c - a), N(ToWorld(l.BasePole)));
            l.Upper.rotation = Quaternion.FromToRotation(b - a, U(solved.Joint) - a) * l.Upper.rotation;
            l.Lower.rotation = Quaternion.FromToRotation(l.End.position - l.Lower.position, U(solved.End) - l.Lower.position) * l.Lower.rotation;
            l.End.rotation = FrameRotation * o.Rotation * l.BaseRotation;
            l.Error = Vector3.Distance(l.End.position, target);
            ActualPoints[jointIndex] = l.Lower.position;
            ActualPoints[i + 2] = l.End.position;
        }
        ActualPoints[0] = Head.position; ActualPoints[1] = Hips.position; ActualPoints[10] = Chest.position;
        for (int i = 0; i < PointErrors.Length; i++) PointErrors[i] = Vector3.Distance(ActualPoints[i], Targets[i]);
        foreach (var f in fingers)
            f.Bone.localRotation *= Quaternion.AngleAxis(f.Angle * (f.Left ? frame.LeftGrip : frame.RightGrip), f.Axis);
        foreach (var bone in Bones)
            if (frame.Bones.TryGetValue(bone.Path, out var offset))
            {
                previousOverrides.Add(bone.Path);
                bone.Transform.localPosition += Vector3.ClampMagnitude(offset.Position, .1f);
                bone.Transform.localRotation *= offset.Rotation;
            }
    }

    private static void SolveSpine(Transform[] chain, Transform end, Vector3 target)
    {
        for (int pass = 0; pass < 4; pass++)
            for (int i = chain.Length - 1; i >= 0; i--)
            {
                var joint = chain[i];
                var from = end.position - joint.position;
                var to = target - joint.position;
                if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) continue;
                Quaternion correction = Quaternion.FromToRotation(from, to);
                joint.rotation = Quaternion.RotateTowards(Quaternion.identity, correction, 12) * joint.rotation;
            }
    }

    private void CaptureFingers(bool left)
    {
        var hand = Required(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var index = Animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var little = Animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        if (index == null || little == null) return;
        Vector3 across = (index.position - little.position).normalized;
        Vector3 forward = ((index.position + little.position) * .5f - hand.position).normalized;
        // Palm normal is oriented consistently for left/right hands; thumb curl is intentionally milder.
        Vector3 palm = Vector3.Cross(across, forward).normalized * (left ? -1 : 1);
        int first = (int)(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        for (int f = 0; f < 5; f++)
            for (int j = 0; j < 3; j++)
            {
                var bone = Animator.GetBoneTransform((HumanBodyBones)(first + f * 3 + j));
                if (bone == null) continue;
                Transform? child = j < 2 ? Animator.GetBoneTransform((HumanBodyBones)(first + f * 3 + j + 1)) : null;
                Vector3 direction = child != null ? child.position - bone.position : bone.position - bone.parent.position;
                Vector3 axis = Vector3.Cross(direction.normalized, palm).normalized;
                if (axis.sqrMagnitude < .5f) continue;
                fingers.Add((bone, bone.InverseTransformDirection(axis), f == 0 ? 30 : 65, left));
            }
    }

    internal static string PathOf(Transform bone, Transform root)
    {
        var parts = new List<string>();
        for (Transform t = bone; t != null && t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse(); return string.Join("/", parts);
    }
    private static NVector N(Vector3 v) => new(v.x, v.y, v.z);
    private static Vector3 U(NVector v) => new(v.X, v.Y, v.Z);
}
