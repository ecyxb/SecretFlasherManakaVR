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
    private static float nextDiagnosticTime;
    private static float nextInactiveDiagnosticTime;
    private static float nextBodyYawDiagnosticTime;
    private static float nextMovementDiagnosticTime;

    public static bool IsActiveRecently
    {
        get { return Time.frameCount - lastActiveFrame <= 2; }
    }

    public static bool ShouldSuppressMode0RightStickY
    {
        get { return CanApply(); }
    }

    public static string BuildActivityDiagnostics()
    {
        Camera? camera = VrRuntimeState.SourceCamera != null ? VrRuntimeState.SourceCamera : Camera.main;
        Transform? target = cachedFpCameraTarget != null ? cachedFpCameraTarget : cachedCameraTarget != null ? cachedCameraTarget : cachedHead;
        float distance = camera == null || target == null ? -1.0f : Vector3.Distance(camera.transform.position, target.position);
        float maxDistance = Plugin.Settings == null ? -1.0f : Mathf.Max(2.0f, Plugin.Settings.PlayerHeadPoseFollowDistance.Value);

        return "activeRecently=" + IsActiveRecently +
            " frameDelta=" + (Time.frameCount - lastActiveFrame) +
            " cachedPlayer=" + (cachedPlayerPtr != IntPtr.Zero) +
            " bones=" + (cachedChest != null) + "/" + (cachedNeck != null) + "/" + (cachedHead != null) +
            " actualMoving=" + IsPlayerActuallyMoving() +
            " vrReady=" + VrRuntimeState.IsVrReady +
            " hasHeadPose=" + VrRuntimeState.HasHeadPose +
            " enabled=" + (Plugin.Settings != null && Plugin.Settings.EnablePlayerHeadPoseControl.Value) +
            " yawTurnsBody=" + (Plugin.Settings != null && Plugin.Settings.PlayerHeadPoseYawTurnsBodyWhileMoving.Value) +
            " sourceCamera=" + (camera == null ? "null" : camera.name) +
            " target=" + (target == null ? "null" : target.name) +
            " distance=" + distance.ToString("0.000") +
            " maxDistance=" + maxDistance.ToString("0.000") +
            " firstPerson=" + (camera != null && IsFirstPersonView(camera));
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

            cachedPlayerPtr = GetObjectPointer(player);
            CachePlayerMovementReferences(player);
            cachedChest = referencer.Chest;
            cachedNeck = referencer.Neck;
            cachedHead = referencer.Head;
            cachedFpCameraTarget = referencer.FPCameraTarget;
            cachedCameraTarget = referencer.CameraTarget;
            hasReset = false;
            Plugin.Logger?.LogInfo("Player head pose control captured player avatar bones.");
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("Player head pose capture skipped: " + ex.Message);
        }
    }

    public static bool IsCachedPlayer(PlayerController player)
    {
        return cachedPlayerPtr != IntPtr.Zero &&
            player != null &&
            GetObjectPointer(player) == cachedPlayerPtr;
    }

    public static void Apply()
    {
        if (!CanApply())
        {
            LogInactiveDiagnostic();
            hasReset = false;
            return;
        }

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
            IsPlayerActuallyMoving(out string movementDiagnostics))
        {
            LogMovementDiagnostic(movementDiagnostics, bodyYaw, headYaw, limitedHeadYaw);
            float residualYaw = ApplyBodyYawCorrection(bodyYaw);
            limitedDelta = LimitDelta(rawDelta, residualYaw, out float correctedHeadYaw);
            smoothedDelta = ConsumeSmoothedYaw(smoothedDelta, lastConsumedBodyYawStepDegrees);
        }

        smoothedDelta = Smooth(smoothedDelta, limitedDelta);

        ApplyWeightedOffset(cachedChest, smoothedDelta, Plugin.Settings.PlayerHeadPoseChestWeight.Value);
        ApplyWeightedOffset(cachedNeck, smoothedDelta, Plugin.Settings.PlayerHeadPoseNeckWeight.Value);
        ApplyWeightedOffset(cachedHead, smoothedDelta, Plugin.Settings.PlayerHeadPoseHeadWeight.Value);
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
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("Player head pose control skipped: " + ex.Message);
            return false;
        }
    }

    private static void LogInactiveDiagnostic()
    {
        if (Time.unscaledTime < nextInactiveDiagnosticTime)
        {
            return;
        }

        nextInactiveDiagnosticTime = Time.unscaledTime + 5.0f;
        Plugin.Logger?.LogInfo("Player head pose control inactive. " + BuildActivityDiagnostics());
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
        bool followsPlayer = distance <= maxDistance;
        if (followsPlayer)
        {
            LogActivationDiagnostic(camera, target, distance, maxDistance);
        }

        return followsPlayer;
    }

    private static void LogActivationDiagnostic(Camera camera, Transform target, float distance, float maxDistance)
    {
        if (Time.unscaledTime < nextDiagnosticTime)
        {
            return;
        }

        nextDiagnosticTime = Time.unscaledTime + 10.0f;
        Plugin.Logger?.LogInfo(
            "Player head pose control active. camera=" + camera.name +
            " target=" + target.name +
            " distance=" + distance.ToString("0.000") +
            " max=" + maxDistance.ToString("0.000"));
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
            cachedPlayerRoot = player.transform;
            cachedCharacterController = null;
            cachedRigidbody = null;
            cachedMoveCalculator = null;
            Plugin.Logger?.LogDebug("Player movement reference capture skipped: " + ex.Message);
        }
    }

    private static bool IsPlayerActuallyMoving()
    {
        return IsPlayerActuallyMoving(out _);
    }

    private static bool IsPlayerActuallyMoving(out string diagnostics)
    {
        float? realtimeMove = null;
        float? lastMove = null;
        float? characterControllerSpeed = null;
        float? rigidbodySpeed = null;
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
                diagnostics = BuildMovementDiagnostics(
                    movingFromRealtimeMove,
                    realtimeMove,
                    lastMove,
                    characterControllerSpeed,
                    rigidbodySpeed,
                    movingFromRealtimeMove,
                    movingFromLastMove,
                    movingFromCharacterController,
                    movingFromRigidbody,
                    "moveCalculator");
                return movingFromRealtimeMove;
            }

            if (cachedCharacterController != null)
            {
                Vector3 velocity = cachedCharacterController.velocity;
                velocity.y = 0.0f;
                characterControllerSpeed = velocity.magnitude;
                movingFromCharacterController = velocity.sqrMagnitude > MovementVelocityThreshold * MovementVelocityThreshold;
            }

            if (cachedRigidbody != null)
            {
                Vector3 velocity = cachedRigidbody.velocity;
                velocity.y = 0.0f;
                rigidbodySpeed = velocity.magnitude;
                movingFromRigidbody = velocity.sqrMagnitude > MovementVelocityThreshold * MovementVelocityThreshold;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("Player movement detection skipped: " + ex.Message);
        }

        bool isMoving = movingFromRealtimeMove ||
            movingFromLastMove ||
            movingFromCharacterController ||
            movingFromRigidbody;
        diagnostics = BuildMovementDiagnostics(
            isMoving,
            realtimeMove,
            lastMove,
            characterControllerSpeed,
            rigidbodySpeed,
            movingFromRealtimeMove,
            movingFromLastMove,
            movingFromCharacterController,
            movingFromRigidbody,
            "velocityFallback");
        return isMoving;
    }

    private static string BuildMovementDiagnostics(
        bool isMoving,
        float? realtimeMove,
        float? lastMove,
        float? characterControllerSpeed,
        float? rigidbodySpeed,
        bool movingFromRealtimeMove,
        bool movingFromLastMove,
        bool movingFromCharacterController,
        bool movingFromRigidbody,
        string source)
    {
        return "moving=" + isMoving +
            " source=" + source +
            " realtimeMove=" + FormatNullable(realtimeMove) + ">" + MovementThreshold.ToString("0.000000") + ":" + movingFromRealtimeMove +
            " lastMove=" + FormatNullable(lastMove) + ">" + MovementThreshold.ToString("0.000000") + ":" + movingFromLastMove +
            " ccSpeed=" + FormatNullable(characterControllerSpeed) + ">" + MovementVelocityThreshold.ToString("0.000000") + ":" + movingFromCharacterController +
            " rbSpeed=" + FormatNullable(rigidbodySpeed) + ">" + MovementVelocityThreshold.ToString("0.000000") + ":" + movingFromRigidbody;
    }

    private static void LogMovementDiagnostic(string diagnostics, float bodyYaw, float headYaw, float limitedHeadYaw)
    {
        if (Time.unscaledTime < nextMovementDiagnosticTime)
        {
            return;
        }

        nextMovementDiagnosticTime = Time.unscaledTime + 1.0f;
        Plugin.Logger?.LogInfo(
            "Player body yaw movement gate passed. " + diagnostics +
            " bodyYaw=" + bodyYaw.ToString("0.0") +
            " consumedYaw=" + consumedBodyYawDegrees.ToString("0.0") +
            " headYaw=" + headYaw.ToString("0.0") +
            " limitedHeadYaw=" + limitedHeadYaw.ToString("0.0"));
    }

    private static string FormatNullable(float? value)
    {
        return value.HasValue ? value.Value.ToString("0.000000") : "null";
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
        LogBodyYawDiagnostic(currentYaw, nextYaw, targetYaw, bodyYaw, appliedYawDelta, residualYaw);
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

    private static void LogBodyYawDiagnostic(float gameYaw, float correctedYaw, float targetYaw, float bodyYaw, float appliedYawDelta, float residualYaw)
    {
        if (Time.unscaledTime < nextBodyYawDiagnosticTime)
        {
            return;
        }

        nextBodyYawDiagnosticTime = Time.unscaledTime + 5.0f;
        Plugin.Logger?.LogInfo(
            "Player body yaw hard-corrected. gameYaw=" + gameYaw.ToString("0.0") +
            " correctedYaw=" + correctedYaw.ToString("0.0") +
            " targetYaw=" + targetYaw.ToString("0.0") +
            " bodyYaw=" + bodyYaw.ToString("0.0") +
            " appliedYaw=" + appliedYawDelta.ToString("0.0") +
            " consumedYaw=" + consumedBodyYawDegrees.ToString("0.0") +
            " residualYaw=" + residualYaw.ToString("0.0") +
            " baseYaw=" + resetBodyYawDegrees.ToString("0.0"));
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

    private static void ApplyWeightedOffset(Transform? bone, Quaternion delta, float weight)
    {
        if (bone == null || weight <= 0.0f)
        {
            return;
        }

        Quaternion weighted = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(weight));
        bone.rotation = bone.rotation * weighted;
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
}
