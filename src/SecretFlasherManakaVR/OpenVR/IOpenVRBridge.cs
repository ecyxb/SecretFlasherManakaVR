using System;
using UnityEngine;

namespace SecretFlasherManakaVR.OpenVR
{
    public interface IOpenVRBridge : IDisposable
    {
        bool IsInitialized { get; }

        string LastError { get; }

        OpenVRInitResult Initialize(bool autoStartSteamVR);

        void Shutdown();

        bool TryGetRecommendedRenderTargetSize(out uint width, out uint height, out string error);

        bool TryUpdatePoses(out string error);

        bool TryGetHmdPose(out OpenVRPose pose, out string error);

        bool TryGetEyeProjection(OpenVREye eye, float nearClip, float farClip, OpenVRProjectionMode mode, out Matrix4x4 projection, out string error);

        bool TryGetEyeToHeadTransform(OpenVREye eye, out Vector3 position, out Quaternion rotation, out Matrix4x4 matrix, out string error);

        void BeginFrame();

        bool Submit(OpenVREye eye, IntPtr nativeTexturePtr, bool isLinearColorSpace, out string error);

        bool Submit(OpenVREye eye, IntPtr textureHandle, OpenVRTextureSubmitType submitType, bool isLinearColorSpace, out string error);

        bool Submit(OpenVREye eye, IntPtr textureHandle, OpenVRTextureSubmitType submitType, bool isLinearColorSpace, bool flipV, out string error);
    }
}
