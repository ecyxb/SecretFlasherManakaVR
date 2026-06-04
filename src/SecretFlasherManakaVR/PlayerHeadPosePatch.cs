using System;
using GameBaseCameraController = ExposureUnnoticed2.Object3D.Camera.BaseCameraController;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.Scripts.Other;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace SecretFlasherManakaVR;

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize))]
internal static class PlayerHeadPoseInitializePatch
{
    private static void Postfix(PlayerController __instance)
    {
        PlayerHeadPoseController.CapturePlayer(__instance);
    }
}

internal static class PlayerHeadPoseController
{
    private const float MovementThreshold = 0.001f;
    private const float MovementVelocityThreshold = 0.02f;
    private const float DefaultBodyYawTurnDegreesPerSecond = 180.0f;

    private static IntPtr cachedPlayerPtr = IntPtr.Zero;
    private static Quaternion resetHmdRotation = Quaternion.identity;
    private static Quaternion smoothedDelta = Quaternion.identity;
    private static float resetBodyYawDegrees;
    private static float lastObservedBodyYawDegrees;
    private static float consumedBodyYawDegrees;
    private static float lastConsumedBodyYawStepDegrees;
    private static int resetRecenterSerial = -1;
    private static int lastActiveFrame = -10000;
    private static bool hasReset;
    private static bool hasBodyYawObservation;
    private static GameBaseCameraController? cachedBaseCameraController;
    private static Transform? cachedPlayerRoot;
    private static CharacterController? cachedCharacterController;
    private static Rigidbody? cachedRigidbody;
    private static PlayerMoveCalculator? cachedMoveCalculator;
    private static Transform? cachedChest;
    private static Transform? cachedNeck;
    private static Transform? cachedHead;
    private static Transform? cachedFpCameraTarget;
    private static Transform? cachedCameraTarget;
    private static BoneOffsetState chestOffsetState;
    private static BoneOffsetState neckOffsetState;
    private static BoneOffsetState headOffsetState;

    public static bool IsActiveRecently
    {
        get { return Time.frameCount - lastActiveFrame <= 2; }
    }

    public static bool ShouldSuppressMode0RightStickY
    {
        get { return CanApply(); }
    }

    public static void CapturePlayer(PlayerController player)
    {
        try
        {
            if (player == null)
            {
                return;
            }

            PlayerAvatarObjectReferencer referencer = player.PlayerAvatarObjectReferencer;
            if (referencer == null ||
                referencer.Head == null ||
                referencer.Neck == null ||
                referencer.Chest == null)
            {
                return;
            }

            RestoreAppliedOffsets();
            cachedPlayerPtr = GetObjectPointer(player);
            CachePlayerMovementReferences(player);
            cachedChest = referencer.Chest;
            cachedNeck = referencer.Neck;
            cachedHead = referencer.Head;
            cachedFpCameraTarget = referencer.FPCameraTarget;
            cachedCameraTarget = referencer.CameraTarget;
            PlayerNeckVisibilityController.CapturePlayer(referencer);
            hasReset = false;
            Plugin.Logger.LogInfo($"Player head pose controller captured player: {player.name}.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Player head pose controller failed to capture player during initialization. {ex}");
        }
    }

    public static bool IsCachedPlayer(PlayerController player)
    {
        return cachedPlayerPtr != IntPtr.Zero &&
            player != null &&
            GetObjectPointer(player) == cachedPlayerPtr;
    }

    public static Transform? CachedPlayerRoot
    {
        get { return cachedPlayerRoot; }
    }

    public static void Apply()
    {
        if (!CanApply())
        {
            RestoreAppliedOffsets();
            PlayerNeckVisibilityController.SetDesired(false);
            hasReset = false;
            return;
        }

        RestoreAppliedOffsets();
        if (!hasReset || resetRecenterSerial != VrRuntimeState.RecenterSerial)
        {
            resetHmdRotation = VrRuntimeState.HeadTrackingRotation;
            ResetBodyYawBaseline();
            smoothedDelta = Quaternion.identity;
            resetRecenterSerial = VrRuntimeState.RecenterSerial;
            hasReset = true;
        }

        SyncNativeBodyYawBaseline();
        Quaternion rawDelta = Quaternion.Inverse(resetHmdRotation) * VrRuntimeState.HeadTrackingRotation;
        float bodyYaw = NormalizeAngle(rawDelta.eulerAngles.y);
        float headYaw = bodyYaw;
        Quaternion limitedDelta = LimitDelta(rawDelta, headYaw, out float limitedHeadYaw);
        lastConsumedBodyYawStepDegrees = 0.0f;
        if (Plugin.Settings.PlayerHeadPoseYawTurnsBodyWhileMoving.Value &&
            IsPlayerActuallyMoving())
        {
            float residualYaw = ApplyBodyYawCorrection(bodyYaw);
            limitedDelta = LimitDelta(rawDelta, residualYaw, out float correctedHeadYaw);
            smoothedDelta = ConsumeSmoothedYaw(smoothedDelta, lastConsumedBodyYawStepDegrees);
        }

        smoothedDelta = Smooth(smoothedDelta, limitedDelta);

        ApplyWeightedOffset(cachedChest, smoothedDelta, Plugin.Settings.PlayerHeadPoseChestWeight.Value, ref chestOffsetState);
        ApplyWeightedOffset(cachedNeck, smoothedDelta, Plugin.Settings.PlayerHeadPoseNeckWeight.Value, ref neckOffsetState);
        ApplyWeightedOffset(cachedHead, smoothedDelta, Plugin.Settings.PlayerHeadPoseHeadWeight.Value, ref headOffsetState);
        PlayerNeckVisibilityController.SetDesired(true);
        lastActiveFrame = Time.frameCount;
    }

    private static bool CanApply()
    {
        if (!VrRuntimeState.IsVrReady ||
            !VrRuntimeState.HasHeadPose ||
            Plugin.Settings == null ||
            !Plugin.Settings.EnablePlayerHeadPoseControl.Value ||
            cachedHead == null ||
            cachedNeck == null ||
            cachedChest == null)
        {
            return false;
        }

        try
        {
            return IsFirstPersonCameraFollowingPlayer();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsFirstPersonCameraFollowingPlayer()
    {
        Camera? camera = VrRuntimeState.SourceCamera != null ? VrRuntimeState.SourceCamera : Camera.main;
        if (camera == null)
        {
            return false;
        }

        if (!IsFirstPersonView(camera))
        {
            return false;
        }

        Transform? target = cachedFpCameraTarget;
        if (target == null)
        {
            target = cachedCameraTarget;
        }

        if (target == null)
        {
            target = cachedHead;
        }

        if (target == null)
        {
            return false;
        }

        float maxDistance = Mathf.Max(2.0f, Plugin.Settings.PlayerHeadPoseFollowDistance.Value);
        float distance = Vector3.Distance(camera.transform.position, target.position);
        return distance <= maxDistance;
    }

    private static bool IsFirstPersonView(Camera sourceCamera)
    {
        GameBaseCameraController? baseCamera = GetBaseCameraController();
        if (baseCamera == null)
        {
            return true;
        }

        if (baseCamera.IsActiveInventoryCamera)
        {
            return false;
        }

        Camera gameCamera = baseCamera.Camera != null ? baseCamera.Camera : baseCamera.camera;
        int mask = gameCamera == null ? sourceCamera.cullingMask : gameCamera.cullingMask;
        if (baseCamera.fpsCameraMask != 0 && mask == baseCamera.fpsCameraMask)
        {
            return true;
        }

        if (baseCamera.tpsCameraMask != 0 && mask == baseCamera.tpsCameraMask)
        {
            return false;
        }

        return true;
    }

    private static GameBaseCameraController? GetBaseCameraController()
    {
        if (cachedBaseCameraController != null)
        {
            return cachedBaseCameraController;
        }

        cachedBaseCameraController = UnityEngine.Object.FindObjectOfType<GameBaseCameraController>();
        return cachedBaseCameraController;
    }

    private static Quaternion LimitDelta(Quaternion delta, out float yaw)
    {
        Vector3 angles = delta.eulerAngles;
        float pitch = Mathf.Clamp(NormalizeAngle(angles.x), -Plugin.Settings.PlayerHeadPosePitchLimitDegrees.Value, Plugin.Settings.PlayerHeadPosePitchLimitDegrees.Value);
        yaw = Mathf.Clamp(NormalizeAngle(angles.y), -Plugin.Settings.PlayerHeadPoseYawLimitDegrees.Value, Plugin.Settings.PlayerHeadPoseYawLimitDegrees.Value);
        float roll = Mathf.Clamp(NormalizeAngle(angles.z), -Plugin.Settings.PlayerHeadPoseRollLimitDegrees.Value, Plugin.Settings.PlayerHeadPoseRollLimitDegrees.Value);
        return Quaternion.Euler(pitch, yaw, roll);
    }

    private static Quaternion LimitDelta(Quaternion delta, float yaw, out float clampedYaw)
    {
        Vector3 angles = delta.eulerAngles;
        float pitch = Mathf.Clamp(NormalizeAngle(angles.x), -Plugin.Settings.PlayerHeadPosePitchLimitDegrees.Value, Plugin.Settings.PlayerHeadPosePitchLimitDegrees.Value);
        clampedYaw = Mathf.Clamp(yaw, -Plugin.Settings.PlayerHeadPoseYawLimitDegrees.Value, Plugin.Settings.PlayerHeadPoseYawLimitDegrees.Value);
        float roll = Mathf.Clamp(NormalizeAngle(angles.z), -Plugin.Settings.PlayerHeadPoseRollLimitDegrees.Value, Plugin.Settings.PlayerHeadPoseRollLimitDegrees.Value);
        return Quaternion.Euler(pitch, clampedYaw, roll);
    }

    private static Quaternion LimitDelta(Quaternion delta, float yaw)
    {
        Vector3 angles = delta.eulerAngles;
        float pitch = Mathf.Clamp(NormalizeAngle(angles.x), -Plugin.Settings.PlayerHeadPosePitchLimitDegrees.Value, Plugin.Settings.PlayerHeadPosePitchLimitDegrees.Value);
        float clampedYaw = Mathf.Clamp(yaw, -Plugin.Settings.PlayerHeadPoseYawLimitDegrees.Value, Plugin.Settings.PlayerHeadPoseYawLimitDegrees.Value);
        float roll = Mathf.Clamp(NormalizeAngle(angles.z), -Plugin.Settings.PlayerHeadPoseRollLimitDegrees.Value, Plugin.Settings.PlayerHeadPoseRollLimitDegrees.Value);
        return Quaternion.Euler(pitch, clampedYaw, roll);
    }

    private static void CachePlayerMovementReferences(PlayerController player)
    {
        try
        {
            PlayerClassAccessor pca = player.pca;
            if (pca == null)
            {
                cachedPlayerRoot = player.transform;
                cachedCharacterController = null;
                cachedRigidbody = null;
                cachedMoveCalculator = null;
                return;
            }

            cachedPlayerRoot = pca._Transform_k__BackingField != null ? pca._Transform_k__BackingField : player.transform;
            cachedCharacterController = pca._CharacterController_k__BackingField;
            cachedRigidbody = pca._Rigidbody_k__BackingField;
            cachedMoveCalculator = pca._PlayerMoveCalculator_k__BackingField;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Player movement reference cache failed; falling back to transform-only tracking. {ex}");
            cachedPlayerRoot = player.transform;
            cachedCharacterController = null;
            cachedRigidbody = null;
            cachedMoveCalculator = null;
        }
    }

    private static bool IsPlayerActuallyMoving()
    {
        float? realtimeMove = null;
        float? lastMove = null;
        bool movingFromRealtimeMove = false;
        bool movingFromLastMove = false;
        bool movingFromCharacterController = false;
        bool movingFromRigidbody = false;

        try
        {
            if (cachedMoveCalculator != null)
            {
                realtimeMove = Mathf.Abs(cachedMoveCalculator._JustMoveAmountRealtime_k__BackingField);
                lastMove = Mathf.Abs(cachedMoveCalculator._LastUpdateJustMoveAmount_k__BackingField);
                movingFromRealtimeMove = realtimeMove.Value > MovementThreshold;
                movingFromLastMove = lastMove.Value > MovementThreshold;
                return movingFromRealtimeMove;
            }

            if (cachedCharacterController != null)
            {
                Vector3 velocity = cachedCharacterController.velocity;
                velocity.y = 0.0f;
                movingFromCharacterController = velocity.sqrMagnitude > MovementVelocityThreshold * MovementVelocityThreshold;
            }

            if (cachedRigidbody != null)
            {
                Vector3 velocity = cachedRigidbody.velocity;
                velocity.y = 0.0f;
                movingFromRigidbody = velocity.sqrMagnitude > MovementVelocityThreshold * MovementVelocityThreshold;
            }
        }
        catch (Exception)
        {
        }

        return movingFromRealtimeMove ||
            movingFromLastMove ||
            movingFromCharacterController ||
            movingFromRigidbody;
    }

    private static void ResetBodyYawBaseline()
    {
        resetBodyYawDegrees = cachedPlayerRoot == null ? 0.0f : GetYawDegrees(cachedPlayerRoot.rotation);
        lastObservedBodyYawDegrees = resetBodyYawDegrees;
        consumedBodyYawDegrees = 0.0f;
        lastConsumedBodyYawStepDegrees = 0.0f;
        hasBodyYawObservation = cachedPlayerRoot != null;
    }

    private static void SyncNativeBodyYawBaseline()
    {
        if (cachedPlayerRoot == null)
        {
            hasBodyYawObservation = false;
            consumedBodyYawDegrees = 0.0f;
            lastConsumedBodyYawStepDegrees = 0.0f;
            return;
        }

        float currentYaw = GetYawDegrees(cachedPlayerRoot.rotation);
        if (!hasBodyYawObservation)
        {
            resetBodyYawDegrees = currentYaw;
            lastObservedBodyYawDegrees = currentYaw;
            consumedBodyYawDegrees = 0.0f;
            lastConsumedBodyYawStepDegrees = 0.0f;
            hasBodyYawObservation = true;
            return;
        }

        float nativeYawDelta = Mathf.DeltaAngle(lastObservedBodyYawDegrees, currentYaw);
        if (Mathf.Abs(nativeYawDelta) > 0.001f)
        {
            resetBodyYawDegrees = NormalizeAngle(resetBodyYawDegrees + nativeYawDelta);
        }

        lastObservedBodyYawDegrees = currentYaw;
    }

    private static float ApplyBodyYawCorrection(float bodyYaw)
    {
        if (cachedPlayerRoot == null)
        {
            lastConsumedBodyYawStepDegrees = 0.0f;
            return bodyYaw;
        }

        float currentYaw = GetYawDegrees(cachedPlayerRoot.rotation);
        float targetYaw = resetBodyYawDegrees + bodyYaw;
        float turnSpeed = Plugin.Settings == null
            ? DefaultBodyYawTurnDegreesPerSecond
            : Plugin.Settings.PlayerHeadPoseBodyYawTurnSpeedDegreesPerSecond.Value;
        float nextYaw = turnSpeed <= 0.0f
            ? targetYaw
            : Mathf.MoveTowardsAngle(
                currentYaw,
                targetYaw,
                turnSpeed * Time.unscaledDeltaTime);
        SetYaw(cachedPlayerRoot, nextYaw);
        float appliedYawDelta = Mathf.DeltaAngle(currentYaw, nextYaw);
        ConsumeBodyYawBaseline(appliedYawDelta);
        lastObservedBodyYawDegrees = nextYaw;
        hasBodyYawObservation = true;
        float residualYaw = Mathf.DeltaAngle(nextYaw, targetYaw);
        return residualYaw;
    }

    private static void ConsumeBodyYawBaseline(float appliedYawDelta)
    {
        lastConsumedBodyYawStepDegrees = appliedYawDelta;
        if (Mathf.Abs(appliedYawDelta) <= 0.001f)
        {
            return;
        }

        resetBodyYawDegrees = NormalizeAngle(resetBodyYawDegrees + appliedYawDelta);
        consumedBodyYawDegrees = NormalizeAngle(consumedBodyYawDegrees + appliedYawDelta);
        resetHmdRotation = Quaternion.AngleAxis(appliedYawDelta, Vector3.up) * resetHmdRotation;
    }

    private static Quaternion ConsumeSmoothedYaw(Quaternion current, float appliedYawDelta)
    {
        if (Mathf.Abs(appliedYawDelta) <= 0.001f)
        {
            return current;
        }

        Vector3 angles = current.eulerAngles;
        float pitch = NormalizeAngle(angles.x);
        float yaw = NormalizeAngle(angles.y) - appliedYawDelta;
        float roll = NormalizeAngle(angles.z);
        return Quaternion.Euler(pitch, NormalizeAngle(yaw), roll);
    }

    private static float GetYawDegrees(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0.0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return 0.0f;
        }

        forward.Normalize();
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    private static void SetYaw(Transform transform, float yaw)
    {
        Vector3 angles = transform.rotation.eulerAngles;
        transform.rotation = Quaternion.Euler(angles.x, yaw, angles.z);
    }

    private static Quaternion Smooth(Quaternion current, Quaternion target)
    {
        float speed = Plugin.Settings.PlayerHeadPoseSmoothFactor.Value;
        if (speed <= 0.0f)
        {
            return target;
        }

        float t = 1.0f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        return Quaternion.Slerp(current, target, Mathf.Clamp01(t));
    }

    private static void RestoreAppliedOffsets()
    {
        chestOffsetState.RestoreIfStillApplied();
        neckOffsetState.RestoreIfStillApplied();
        headOffsetState.RestoreIfStillApplied();
    }

    private static void ApplyWeightedOffset(Transform? bone, Quaternion delta, float weight, ref BoneOffsetState state)
    {
        if (bone == null || weight <= 0.0f)
        {
            state.Clear();
            return;
        }

        Quaternion weighted = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(weight));
        Quaternion finalRotation = bone.rotation * weighted;
        bone.rotation = finalRotation;
        state.Save(bone, weighted, finalRotation);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360.0f;
        if (angle > 180.0f)
        {
            angle -= 360.0f;
        }
        else if (angle < -180.0f)
        {
            angle += 360.0f;
        }

        return angle;
    }

    private static IntPtr GetObjectPointer(Il2CppObjectBase? value)
    {
        return value == null
            ? IntPtr.Zero
            : IL2CPP.Il2CppObjectBaseToPtr(value);
    }

    private struct BoneOffsetState
    {
        private const float RotationMatchDot = 0.9999f;

        private Transform? bone;
        private Quaternion weightedOffset;
        private Quaternion finalRotation;
        private bool hasAppliedOffset;

        public void Save(Transform bone, Quaternion weightedOffset, Quaternion finalRotation)
        {
            this.bone = bone;
            this.weightedOffset = weightedOffset;
            this.finalRotation = finalRotation;
            hasAppliedOffset = true;
        }

        public void RestoreIfStillApplied()
        {
            if (!hasAppliedOffset || bone == null)
            {
                Clear();
                return;
            }

            if (!ApproximatelySameRotation(bone.rotation, finalRotation))
            {
                Clear();
                return;
            }

            bone.rotation = bone.rotation * Quaternion.Inverse(weightedOffset);
            Clear();
        }

        public void Clear()
        {
            bone = null;
            weightedOffset = Quaternion.identity;
            finalRotation = Quaternion.identity;
            hasAppliedOffset = false;
        }

        private static bool ApproximatelySameRotation(Quaternion left, Quaternion right)
        {
            return Mathf.Abs(Quaternion.Dot(left, right)) >= RotationMatchDot;
        }
    }
}
