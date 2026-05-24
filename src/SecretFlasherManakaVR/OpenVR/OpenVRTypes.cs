using UnityEngine;

namespace SecretFlasherManakaVR.OpenVR
{
    public enum OpenVREye
    {
        Left = 0,
        Right = 1
    }

    public enum OpenVRTextureSubmitType
    {
        DirectX = 0,
        DXGISharedHandle = 5
    }

    public enum OpenVRProjectionMode
    {
        SourceCamera = 0,
        ValveMatrix = 1,
        RawSwapVertical = 2,
        RawNoSwap = 3,
        RawInvertVertical = 4
    }

    public readonly struct OpenVRInitResult
    {
        public OpenVRInitResult(bool isSuccess, uint recommendedWidth, uint recommendedHeight, string error)
        {
            IsSuccess = isSuccess;
            RecommendedWidth = recommendedWidth;
            RecommendedHeight = recommendedHeight;
            Error = error ?? string.Empty;
        }

        public bool IsSuccess { get; }

        public uint RecommendedWidth { get; }

        public uint RecommendedHeight { get; }

        public string Error { get; }

        public static OpenVRInitResult Success(uint recommendedWidth, uint recommendedHeight)
        {
            return new OpenVRInitResult(true, recommendedWidth, recommendedHeight, string.Empty);
        }

        public static OpenVRInitResult Failure(string error)
        {
            return new OpenVRInitResult(false, 0, 0, error);
        }
    }

    public readonly struct OpenVRPose
    {
        public OpenVRPose(bool isValid, bool isConnected, Vector3 position, Quaternion rotation, Matrix4x4 deviceToAbsoluteTracking, string trackingResult)
        {
            IsValid = isValid;
            IsConnected = isConnected;
            Position = position;
            Rotation = rotation;
            DeviceToAbsoluteTracking = deviceToAbsoluteTracking;
            TrackingResult = trackingResult ?? string.Empty;
        }

        public bool IsValid { get; }

        public bool IsConnected { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Matrix4x4 DeviceToAbsoluteTracking { get; }

        public string TrackingResult { get; }

        public static OpenVRPose Invalid(string trackingResult)
        {
            return new OpenVRPose(false, false, Vector3.zero, Quaternion.identity, Matrix4x4.identity, trackingResult);
        }
    }
}
