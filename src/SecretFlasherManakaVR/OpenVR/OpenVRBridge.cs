using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SecretFlasherManakaVR.OpenVR
{
    public sealed class OpenVRBridge : IOpenVRBridge
    {
        private Valve.VR.CVRSystem? system;
        private Valve.VR.CVRCompositor? compositor;
        private Valve.VR.TrackedDevicePose_t[] poses = Array.Empty<Valve.VR.TrackedDevicePose_t>();
        private readonly Valve.VR.TrackedDevicePose_t[] gamePoses = Array.Empty<Valve.VR.TrackedDevicePose_t>();
        private bool initialized;
        private bool poseFrameAvailable;
        private bool hasCompositorFrameIndex;
        private uint compositorFrameIndex;
        private bool submittedLeftThisFrame;
        private bool submittedRightThisFrame;

        public OpenVRBridge()
        {
        }

        public bool IsInitialized => initialized;

        public string LastError { get; private set; } = string.Empty;

        public OpenVRInitResult Initialize(bool autoStartSteamVR)
        {
            if (initialized && TryGetRecommendedRenderTargetSize(out var currentWidth, out var currentHeight, out _))
            {
                return OpenVRInitResult.Success(currentWidth, currentHeight);
            }

            try
            {
                if (!Valve.VR.OpenVR.IsRuntimeInstalled())
                {
                    return FailInit("OpenVR runtime is not installed. Install SteamVR or place the official OpenVR runtime where it can be loaded.");
                }

                if (!autoStartSteamVR && !Valve.VR.OpenVR.IsHmdPresent())
                {
                    return FailInit("No OpenVR HMD is present. SteamVR auto-start is disabled.");
                }

                var initError = Valve.VR.EVRInitError.None;
                system = Valve.VR.OpenVR.Init(ref initError, Valve.VR.EVRApplicationType.VRApplication_Scene);
                if (initError != Valve.VR.EVRInitError.None || system == null)
                {
                    return FailInit("OpenVR init failed: " + ErrorName(initError));
                }

                compositor = Valve.VR.OpenVR.Compositor;
                if (compositor == null)
                {
                    Shutdown();
                    return FailInit("OpenVR compositor interface is not available.");
                }

                poses = new Valve.VR.TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
                compositor.SetTrackingSpace(Valve.VR.ETrackingUniverseOrigin.TrackingUniverseStanding);
                initialized = true;

                if (!TryGetRecommendedRenderTargetSize(out var width, out var height, out var sizeError))
                {
                    Shutdown();
                    return FailInit(sizeError);
                }

                LastError = string.Empty;
                return OpenVRInitResult.Success(width, height);
            }
            catch (DllNotFoundException ex)
            {
                return FailInit("openvr_api.dll could not be loaded: " + ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                return FailInit("The loaded openvr_api.dll is missing a required entry point: " + ex);
            }
            catch (Exception ex)
            {
                return FailInit("Unexpected OpenVR init failure: " + ex);
            }
        }

        public void Shutdown()
        {
            if (!initialized && system == null && compositor == null)
            {
                return;
            }

            try
            {
                Valve.VR.OpenVR.Shutdown();
            }
            catch (Exception ex)
            {
                LastError = "OpenVR shutdown failed: " + ex;
            }
            finally
            {
                initialized = false;
                system = null;
                compositor = null;
                poses = Array.Empty<Valve.VR.TrackedDevicePose_t>();
                poseFrameAvailable = false;
                hasCompositorFrameIndex = false;
                compositorFrameIndex = 0;
                submittedLeftThisFrame = false;
                submittedRightThisFrame = false;
            }
        }

        public bool TryGetRecommendedRenderTargetSize(out uint width, out uint height, out string error)
        {
            width = 0;
            height = 0;

            if (!EnsureInitialized(out error) || system == null)
            {
                return false;
            }

            try
            {
                system.GetRecommendedRenderTargetSize(ref width, ref height);
                if (width == 0 || height == 0)
                {
                    error = SetError("OpenVR returned an invalid recommended render target size.");
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = SetError("Failed to read OpenVR render target size: " + ex.Message);
                return false;
            }
        }

        public bool TryUpdatePoses(out string error)
        {
            if (!EnsureInitialized(out error) || compositor == null)
            {
                return false;
            }

            try
            {
                var compositorError = compositor.WaitGetPoses(poses, gamePoses);
                if (compositorError != Valve.VR.EVRCompositorError.None)
                {
                    error = SetError("OpenVR WaitGetPoses failed: " + compositorError);
                    return false;
                }

                UpdateSubmitFrameState();
                poseFrameAvailable = true;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = SetError("Failed to update OpenVR poses: " + ex.Message);
                return false;
            }
        }

        public bool TryGetHmdPose(out OpenVRPose pose, out string error)
        {
            pose = OpenVRPose.Invalid("NotInitialized");

            if (!EnsureInitialized(out error))
            {
                return false;
            }

            try
            {
                if (!poseFrameAvailable && !TryUpdatePoses(out error))
                {
                    return false;
                }

                var nativePose = poses[Valve.VR.OpenVR.k_unTrackedDeviceIndex_Hmd];
                if (!nativePose.bDeviceIsConnected || !nativePose.bPoseIsValid)
                {
                    error = SetError("OpenVR HMD pose is not valid: " + nativePose.eTrackingResult);
                    pose = OpenVRPose.Invalid(nativePose.eTrackingResult.ToString());
                    return false;
                }

                var matrix = ToUnityMatrix(nativePose.mDeviceToAbsoluteTracking);
                pose = new OpenVRPose(
                    true,
                    nativePose.bDeviceIsConnected,
                    MatrixPosition(matrix),
                    MatrixRotation(matrix),
                    matrix,
                    nativePose.eTrackingResult.ToString());
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = SetError("Failed to read OpenVR HMD pose: " + ex.Message);
                return false;
            }
        }

        public bool GetRecommendedRenderTargetSize(out uint width, out uint height)
        {
            return TryGetRecommendedRenderTargetSize(out width, out height, out _);
        }

        public bool TryGetProjectionMatrix(OpenVREye eye, float nearClip, float farClip, out Matrix4x4 projection)
        {
            return TryGetEyeProjection(eye, nearClip, farClip, OpenVRProjectionMode.RawSwapVertical, out projection, out _);
        }

        public Matrix4x4 GetProjectionMatrix(OpenVREye eye, float nearClip, float farClip)
        {
            return TryGetEyeProjection(eye, nearClip, farClip, OpenVRProjectionMode.RawSwapVertical, out var projection, out _) ? projection : Matrix4x4.identity;
        }

        public float GetIpdMeters()
        {
            if (!TryGetEyeToHeadTransform(OpenVREye.Left, out var leftPosition, out _, out _, out _) ||
                !TryGetEyeToHeadTransform(OpenVREye.Right, out var rightPosition, out _, out _, out _))
            {
                return 0.064f;
            }

            return Mathf.Abs(rightPosition.x - leftPosition.x);
        }

        public bool TryGetEyeProjection(OpenVREye eye, float nearClip, float farClip, OpenVRProjectionMode mode, out Matrix4x4 projection, out string error)
        {
            projection = Matrix4x4.identity;

            if (!EnsureInitialized(out error) || system == null)
            {
                return false;
            }

            try
            {
                if (mode == OpenVRProjectionMode.ValveMatrix)
                {
                    projection = ToMatrix4x4(system.GetProjectionMatrix(ToValveEye(eye), nearClip, farClip));
                    error = string.Empty;
                    return true;
                }

                float left = 0.0f;
                float right = 0.0f;
                float top = 0.0f;
                float bottom = 0.0f;
                system.GetProjectionRaw(ToValveEye(eye), ref left, ref right, ref top, ref bottom);
                projection = BuildUnityProjectionFromRaw(left, right, top, bottom, nearClip, farClip, mode);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = SetError($"Failed to read OpenVR {eye} projection: {ex.Message}");
                return false;
            }
        }

        public bool TryGetEyeToHeadTransform(OpenVREye eye, out Vector3 position, out Quaternion rotation, out Matrix4x4 matrix, out string error)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            matrix = Matrix4x4.identity;

            if (!EnsureInitialized(out error) || system == null)
            {
                return false;
            }

            try
            {
                matrix = ToUnityMatrix(system.GetEyeToHeadTransform(ToValveEye(eye)));
                position = MatrixPosition(matrix);
                rotation = MatrixRotation(matrix);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = SetError($"Failed to read OpenVR {eye} eye-to-head transform: {ex.Message}");
                return false;
            }
        }

        public void BeginFrame()
        {
            TryUpdatePoses(out _);
        }

        public bool Submit(OpenVREye eye, RenderTexture texture)
        {
            if (texture == null)
            {
                LastError = "Cannot submit a null RenderTexture to OpenVR.";
                return false;
            }

            return Submit(eye, texture.GetNativeTexturePtr(), QualitySettings.activeColorSpace == ColorSpace.Linear, out _);
        }

        public bool Submit(OpenVREye eye, IntPtr nativeTexturePtr)
        {
            return Submit(eye, nativeTexturePtr, QualitySettings.activeColorSpace == ColorSpace.Linear, out _);
        }

        public bool Submit(OpenVREye eye, IntPtr nativeTexturePtr, bool isLinearColorSpace, out string error)
        {
            return Submit(eye, nativeTexturePtr, OpenVRTextureSubmitType.DirectX, isLinearColorSpace, true, out error);
        }

        public bool Submit(OpenVREye eye, IntPtr nativeTexturePtr, OpenVRTextureSubmitType submitType, bool isLinearColorSpace, out string error)
        {
            return Submit(eye, nativeTexturePtr, submitType, isLinearColorSpace, true, out error);
        }

        public bool Submit(OpenVREye eye, IntPtr nativeTexturePtr, OpenVRTextureSubmitType submitType, bool isLinearColorSpace, bool flipV, out string error)
        {
            if (!EnsureInitialized(out error) || compositor == null)
            {
                return false;
            }

            if (nativeTexturePtr == IntPtr.Zero)
            {
                error = SetError("Cannot submit a null native texture pointer to OpenVR.");
                return false;
            }

            if ((eye == OpenVREye.Left && submittedLeftThisFrame) ||
                (eye == OpenVREye.Right && submittedRightThisFrame))
            {
                error = string.Empty;
                return true;
            }

            try
            {
                var texture = new Valve.VR.Texture_t
                {
                    handle = nativeTexturePtr,
                    eType = ToValveTextureType(submitType),
                    eColorSpace = Valve.VR.EColorSpace.Auto
                };
                var bounds = new Valve.VR.VRTextureBounds_t
                {
                    uMin = 0.0f,
                    vMin = flipV ? 1.0f : 0.0f,
                    uMax = 1.0f,
                    vMax = flipV ? 0.0f : 1.0f
                };

                var compositorError = compositor.Submit(ToValveEye(eye), ref texture, ref bounds, Valve.VR.EVRSubmitFlags.Submit_Default);
                if (compositorError != Valve.VR.EVRCompositorError.None)
                {
                    if (compositorError == Valve.VR.EVRCompositorError.AlreadySubmitted)
                    {
                        MarkEyeSubmitted(eye);
                    }

                    error = SetError($"OpenVR Submit({eye}) failed: {compositorError}");
                    return false;
                }

                MarkEyeSubmitted(eye);
                if (eye == OpenVREye.Right)
                {
                    poseFrameAvailable = false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = SetError($"Failed to submit {eye} texture to OpenVR: {ex.Message}");
                return false;
            }
        }

        public void PostPresentHandoff()
        {
            if (!initialized || compositor == null)
            {
                return;
            }

            try
            {
                compositor.PostPresentHandoff();
            }
            catch
            {
                SetError("OpenVR PostPresentHandoff failed.");
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        private static Valve.VR.EVREye ToValveEye(OpenVREye eye)
        {
            return eye == OpenVREye.Right ? Valve.VR.EVREye.Eye_Right : Valve.VR.EVREye.Eye_Left;
        }

        private static Valve.VR.ETextureType ToValveTextureType(OpenVRTextureSubmitType submitType)
        {
            return submitType == OpenVRTextureSubmitType.DXGISharedHandle
                ? Valve.VR.ETextureType.DXGISharedHandle
                : Valve.VR.ETextureType.DirectX;
        }

        private void MarkEyeSubmitted(OpenVREye eye)
        {
            if (eye == OpenVREye.Left)
            {
                submittedLeftThisFrame = true;
            }
            else
            {
                submittedRightThisFrame = true;
            }
        }

        private void UpdateSubmitFrameState()
        {
            if (compositor == null || !TryGetCompositorFrameIndex(out var frameIndex))
            {
                hasCompositorFrameIndex = false;
                submittedLeftThisFrame = false;
                submittedRightThisFrame = false;
                return;
            }

            if (!hasCompositorFrameIndex || frameIndex != compositorFrameIndex)
            {
                hasCompositorFrameIndex = true;
                compositorFrameIndex = frameIndex;
                submittedLeftThisFrame = false;
                submittedRightThisFrame = false;
            }
        }

        private bool TryGetCompositorFrameIndex(out uint frameIndex)
        {
            frameIndex = 0;
            if (compositor == null)
            {
                return false;
            }

            try
            {
                var timing = new Valve.VR.Compositor_FrameTiming
                {
                    m_nSize = (uint)Marshal.SizeOf<Valve.VR.Compositor_FrameTiming>()
                };

                if (!compositor.GetFrameTiming(ref timing, 0))
                {
                    return false;
                }

                frameIndex = timing.m_nFrameIndex;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Matrix4x4 ToMatrix4x4(Valve.VR.HmdMatrix44_t source)
        {
            var matrix = Matrix4x4.identity;
            matrix[0, 0] = source.m0;
            matrix[0, 1] = source.m1;
            matrix[0, 2] = source.m2;
            matrix[0, 3] = source.m3;
            matrix[1, 0] = source.m4;
            matrix[1, 1] = source.m5;
            matrix[1, 2] = source.m6;
            matrix[1, 3] = source.m7;
            matrix[2, 0] = source.m8;
            matrix[2, 1] = source.m9;
            matrix[2, 2] = source.m10;
            matrix[2, 3] = source.m11;
            matrix[3, 0] = source.m12;
            matrix[3, 1] = source.m13;
            matrix[3, 2] = source.m14;
            matrix[3, 3] = source.m15;
            return matrix;
        }

        private static Matrix4x4 BuildUnityProjectionFromRaw(float leftTan, float rightTan, float topTan, float bottomTan, float nearClip, float farClip, OpenVRProjectionMode mode)
        {
            float left = leftTan * nearClip;
            float right = rightTan * nearClip;
            float top = topTan * nearClip;
            float bottom = bottomTan * nearClip;

            if (mode == OpenVRProjectionMode.RawInvertVertical)
            {
                top = -top;
                bottom = -bottom;
            }
            else if (mode == OpenVRProjectionMode.RawNoSwap)
            {
                return Matrix4x4.Frustum(left, right, bottom, top, nearClip, farClip);
            }

            if (top < bottom)
            {
                float temp = top;
                top = bottom;
                bottom = temp;
            }

            return Matrix4x4.Frustum(left, right, bottom, top, nearClip, farClip);
        }

        private static Matrix4x4 ToUnityMatrix(Valve.VR.HmdMatrix34_t source)
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
            matrix[3, 0] = 0.0f;
            matrix[3, 1] = 0.0f;
            matrix[3, 2] = 0.0f;
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

            if (forward.sqrMagnitude < 0.000001f || upwards.sqrMagnitude < 0.000001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(forward.normalized, upwards.normalized);
        }

        private bool EnsureInitialized(out string error)
        {
            if (initialized)
            {
                error = string.Empty;
                return true;
            }

            error = SetError("OpenVR bridge is not initialized.");
            return false;
        }

        private OpenVRInitResult FailInit(string error)
        {
            LastError = error ?? string.Empty;
            initialized = false;
            return OpenVRInitResult.Failure(LastError);
        }

        private string SetError(string error)
        {
            LastError = error ?? string.Empty;
            return LastError;
        }

        private static string ErrorName(Valve.VR.EVRInitError error)
        {
            try
            {
                return Valve.VR.OpenVR.GetStringForHmdError(error) ?? error.ToString();
            }
            catch
            {
                return error.ToString();
            }
        }
    }
}
