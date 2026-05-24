using System;
using System.Runtime.InteropServices;

namespace SecretFlasherManakaVR.OpenVR
{
    internal static class OpenVRNative
    {
        public const uint MaxTrackedDeviceCount = 64;
        public const uint HmdTrackedDeviceIndex = 0;

        public static readonly string[] SystemInterfaceVersions =
        {
            "FnTable:IVRSystem_026"
        };

        public static readonly string[] CompositorInterfaceVersions =
        {
            "FnTable:IVRCompositor_029"
        };

        [DllImport("openvr_api", EntryPoint = "VR_IsRuntimeInstalled", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsRuntimeInstalled();

        [DllImport("openvr_api", EntryPoint = "VR_IsHmdPresent", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsHmdPresent();

        [DllImport("openvr_api", EntryPoint = "VR_InitInternal", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint InitInternal(ref EVRInitError error, EVRApplicationType applicationType);

        [DllImport("openvr_api", EntryPoint = "VR_ShutdownInternal", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ShutdownInternal();

        [DllImport("openvr_api", EntryPoint = "VR_GetGenericInterface", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetGenericInterface([MarshalAs(UnmanagedType.LPStr)] string interfaceVersion, ref EVRInitError error);

        [DllImport("openvr_api", EntryPoint = "VR_GetStringForHmdError", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetStringForHmdErrorRaw(EVRInitError error);

        public static string GetStringForHmdError(EVRInitError error)
        {
            try
            {
                var ptr = GetStringForHmdErrorRaw(error);
                return ptr == IntPtr.Zero ? error.ToString() : Marshal.PtrToStringAnsi(ptr) ?? error.ToString();
            }
            catch
            {
                return error.ToString();
            }
        }

        public enum EVRApplicationType
        {
            Other = 0,
            Scene = 1,
            Overlay = 2,
            Background = 3,
            Utility = 4,
            VRMonitor = 5,
            SteamWatchdog = 6,
            Bootstrapper = 7,
            WebHelper = 8,
            OpenXRInstance = 9
        }

        public enum EVRInitError
        {
            None = 0,
            Unknown = 1,
            InitInstallationNotFound = 100,
            InitInstallationCorrupt = 101,
            InitVRClientDLLNotFound = 102,
            InitFileNotFound = 103,
            InitFactoryNotFound = 104,
            InitInterfaceNotFound = 105,
            InitInvalidInterface = 106,
            InitUserConfigDirectoryInvalid = 107,
            InitHmdNotFound = 108,
            InitNotInitialized = 109,
            InitPathRegistryNotFound = 110,
            InitNoConfigPath = 111,
            InitNoLogPath = 112,
            InitPathRegistryNotWritable = 113,
            InitAppInfoInitFailed = 114,
            InitRetry = 115,
            InitInitCanceledByUser = 116,
            InitAnotherAppLaunching = 117,
            InitSettingsInitFailed = 118,
            InitShuttingDown = 119,
            InitTooManyObjects = 120,
            InitNoServerForBackgroundApp = 121,
            InitNotSupportedWithCompositor = 122,
            InitNotAvailableToUtilityApps = 123,
            InitInternal = 124,
            InitHmdDriverIdIsNone = 125,
            InitHmdNotFoundPresenceFailed = 126,
            InitVRMonitorNotFound = 127,
            InitVRMonitorStartupFailed = 128,
            InitLowPowerWatchdogNotSupported = 129,
            InitInvalidApplicationType = 130,
            InitNotAvailableToWatchdogApps = 131,
            InitWatchdogDisabledInSettings = 132,
            InitVRDashboardNotFound = 133,
            InitVRDashboardStartupFailed = 134,
            InitVRHomeNotFound = 135,
            InitVRHomeStartupFailed = 136,
            InitRebootingBusy = 137,
            InitFirmwareUpdateBusy = 138,
            InitFirmwareRecoveryBusy = 139,
            InitUSBServiceBusy = 140,
            InitVRWebHelperStartupFailed = 141,
            InitTrackerManagerInitFailed = 142,
            InitAlreadyRunning = 143,
            InitFailedForVrMonitor = 144,
            InitPropertyManagerInitFailed = 145,
            InitWebServerFailed = 146,
            InitIllegalTypeTransition = 147,
            InitMismatchedRuntimes = 148,
            InitInvalidProcessId = 149,
            InitVpnBlocked = 150,
            InitVRServerNotFound = 151,
            InitVRServerStartupFailed = 152,
            InitRuntimeOutOfDate = 153,
            InitHmdInUse = 154,
            InitNotAvailableToWatchdogApps2 = 155
        }

        public enum EVREye
        {
            Left = 0,
            Right = 1
        }

        public enum ETextureType
        {
            Invalid = -1,
            DirectX = 0,
            OpenGL = 1,
            Vulkan = 2,
            IOSurface = 3,
            DirectX12 = 4,
            DXGISharedHandle = 5,
            Metal = 6
        }

        public enum EColorSpace
        {
            Auto = 0,
            Gamma = 1,
            Linear = 2
        }

        public enum EVRSubmitFlags
        {
            Default = 0,
            LensDistortionAlreadyApplied = 1,
            GlueToHand = 2,
            SubmitReserved = 4,
            TextureWithPose = 8,
            TextureWithDepth = 16,
            FrameDiscontinuty = 32,
            VulkanTextureWithArrayData = 64,
            DisableTemporalCompositor = 128
        }

        public enum EVRCompositorError
        {
            None = 0,
            RequestFailed = 1,
            IncompatibleVersion = 100,
            DoNotHaveFocus = 101,
            InvalidTexture = 102,
            IsNotSceneApplication = 103,
            TextureIsOnWrongDevice = 104,
            TextureUsesUnsupportedFormat = 105,
            SharedTexturesNotSupported = 106,
            IndexOutOfRange = 107,
            AlreadySubmitted = 108,
            InvalidBounds = 109,
            AlreadySet = 110
        }

        public enum ETrackingUniverseOrigin
        {
            Seated = 0,
            Standing = 1,
            RawAndUncalibrated = 2
        }

        public enum ETrackingResult
        {
            Uninitialized = 1,
            CalibratingInProgress = 100,
            CalibratingOutOfRange = 101,
            RunningOK = 200,
            RunningOutOfRange = 201,
            FallbackRotationOnly = 300
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HmdVector3
        {
            public float v0;
            public float v1;
            public float v2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HmdMatrix34
        {
            public float m0;
            public float m1;
            public float m2;
            public float m3;
            public float m4;
            public float m5;
            public float m6;
            public float m7;
            public float m8;
            public float m9;
            public float m10;
            public float m11;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HmdMatrix44
        {
            public float m0;
            public float m1;
            public float m2;
            public float m3;
            public float m4;
            public float m5;
            public float m6;
            public float m7;
            public float m8;
            public float m9;
            public float m10;
            public float m11;
            public float m12;
            public float m13;
            public float m14;
            public float m15;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TrackedDevicePose
        {
            public HmdMatrix34 DeviceToAbsoluteTracking;
            public HmdVector3 Velocity;
            public HmdVector3 AngularVelocity;
            public ETrackingResult TrackingResult;

            [MarshalAs(UnmanagedType.I1)]
            public bool PoseIsValid;

            [MarshalAs(UnmanagedType.I1)]
            public bool DeviceIsConnected;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Texture
        {
            public IntPtr Handle;
            public ETextureType Type;
            public EColorSpace ColorSpace;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VRTextureBounds
        {
            public float UMin;
            public float VMin;
            public float UMax;
            public float VMax;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IVRSystemFnTable
        {
            public GetRecommendedRenderTargetSizeDelegate GetRecommendedRenderTargetSize;
            public GetProjectionMatrixDelegate GetProjectionMatrix;
            public IntPtr GetProjectionRaw;
            public IntPtr ComputeDistortion;
            public IntPtr ComputeDistortionSet;
            public GetEyeToHeadTransformDelegate GetEyeToHeadTransform;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IVRCompositorFnTable
        {
            public SetTrackingSpaceDelegate SetTrackingSpace;
            public GetTrackingSpaceDelegate GetTrackingSpace;
            public WaitGetPosesDelegate WaitGetPoses;
            public IntPtr GetLastPoses;
            public IntPtr GetLastPoseForTrackedDeviceIndex;
            public IntPtr GetSubmitTexture;
            public SubmitDelegate Submit;
            public IntPtr SubmitWithArrayIndex;
            public ClearLastSubmittedFrameDelegate ClearLastSubmittedFrame;
            public PostPresentHandoffDelegate PostPresentHandoff;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void GetRecommendedRenderTargetSizeDelegate(ref uint width, ref uint height);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate HmdMatrix44 GetProjectionMatrixDelegate(EVREye eye, float nearClip, float farClip);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate HmdMatrix34 GetEyeToHeadTransformDelegate(EVREye eye);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void SetTrackingSpaceDelegate(ETrackingUniverseOrigin origin);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate ETrackingUniverseOrigin GetTrackingSpaceDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate EVRCompositorError WaitGetPosesDelegate(
            [In, Out] TrackedDevicePose[] renderPoseArray,
            uint renderPoseArrayCount,
            IntPtr gamePoseArray,
            uint gamePoseArrayCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate EVRCompositorError SubmitDelegate(
            EVREye eye,
            ref Texture texture,
            ref VRTextureBounds bounds,
            EVRSubmitFlags submitFlags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ClearLastSubmittedFrameDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void PostPresentHandoffDelegate();
    }
}
