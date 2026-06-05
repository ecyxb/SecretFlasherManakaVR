using System;
using System.Linq;
using System.Reflection;
using SecretFlasherManakaVR.InputMapping;
using SecretFlasherManakaVR.OpenVR;
using SecretFlasherManakaVR.Runtime;
using UnityEngine;

namespace SecretFlasherManakaVR;

public sealed class VrRunnerHost : MonoBehaviour
{
    private static readonly string[] RuntimeTypeNames =
    {
        "SecretFlasherManakaVR.Runtime.VrRuntimeManager",
        "SecretFlasherManakaVR.VrRuntimeManager"
    };

    private Plugin? _plugin;
    private ModConfig? _settings;
    private object? _runtime;
    private object? _openVrBridge;
    private IVrRuntimeLogger? _runtimeLogger;
    private MethodInfo? _updateMethod;
    private MethodInfo? _lateUpdateMethod;
    private MethodInfo? _shutdownMethod;
    private bool _startupAttempted;
    private bool _runtimeDisabled;
    private bool _runtimeShutdownAttempted;
    private bool _runtimeShutdownInProgress;
    private bool _missingRuntimeReported;
    private int _lastLateTickFrame = -1;
    private int _lastGameLateUpdateFrame = -1;
    private bool _gameLateUpdateObserved;

    public VrRunnerHost(IntPtr pointer)
        : base(pointer)
    {
    }

    public static VrRunnerHost? Instance { get; private set; }

    public bool IsRuntimeActive => _runtime is not null && !_runtimeDisabled;

    public ModConfig? Settings => _settings;

    public void Initialize(Plugin plugin, ModConfig settings)
    {
        _plugin = plugin;
        _settings = settings;
        _runtimeLogger = new BepInExVrRuntimeLogger(Plugin.Logger);
        Instance = this;
        _runtimeLogger.Info("VrRunnerHost initialized.");
        _runtimeLogger.Info(
            "Initial runtime settings: " +
            $"EnableVR={settings.EnableVR.Value}, " +
            $"AutoStartSteamVR={settings.AutoStartSteamVR.Value}, " +
            $"Quest3Input={settings.EnableQuest3InputMapping.Value}.");

        try
        {
            Quest3InputSystem.Configure(settings);
            _runtimeLogger.Info("Quest 3 input system configured.");
        }
        catch (Exception ex)
        {
            _runtimeLogger.Error("Quest 3 input system configuration failed.", ex);
        }
    }

    private void Awake()
    {
        Instance = this;
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        _runtimeLogger?.Info("VrRunnerHost Awake completed.");
    }

    private void Start()
    {
        _runtimeLogger?.Info("VrRunnerHost Start: attempting runtime startup.");
        TryStartRuntime();
    }

    private void Update()
    {
        if (!_startupAttempted)
        {
            TryStartRuntime();
        }

        Quest3InputSystem.Tick();
        InvokeLifecycle(_updateMethod, "update");
    }

    private void LateUpdate()
    {
        if (_gameLateUpdateObserved && Time.frameCount <= _lastGameLateUpdateFrame + 1)
        {
            return;
        }

        InvokeLateTick("late update fallback");
    }

    public void LateTickFromGameLateUpdate()
    {
        _gameLateUpdateObserved = true;
        _lastGameLateUpdateFrame = Time.frameCount;

        if (!_startupAttempted)
        {
            TryStartRuntime();
        }

        InvokeLateTick("game late update dispatcher");
    }

    private void OnDestroy()
    {
        _runtimeLogger?.Info("VrRunnerHost OnDestroy: shutting down runtime.");
        ShutdownRuntime("VR runner host destroyed.");
        Quest3InputSystem.Shutdown();
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public void DisableRuntime(string reason, Exception? exception = null)
    {
        if (exception is null)
        {
            _runtimeLogger?.Warning(reason);
        }
        else
        {
            _runtimeLogger?.Error(reason, exception);
        }

        _runtimeDisabled = true;

        ShutdownRuntime("VR runtime disabled.");
    }

    private void TryStartRuntime()
    {
        _startupAttempted = true;

        if (_runtimeDisabled || _settings is null)
        {
            if (_settings is null)
            {
                _runtimeLogger?.Warning("Runtime startup skipped because settings are not available.");
            }

            return;
        }

        if (!_settings.EnableVR.Value)
        {
            _runtimeLogger?.Info("Runtime startup skipped because EnableVR is false.");
            _runtimeDisabled = true;
            return;
        }

        try
        {
            _runtimeLogger?.Info("Searching for VR runtime type.");
            var runtimeType = FindRuntimeType();
            if (runtimeType is null)
            {
                if (!_missingRuntimeReported)
                {
                    _missingRuntimeReported = true;
                    _runtimeLogger?.Error(
                        "VR runtime type was not found in loaded assemblies. " +
                        "Expected SecretFlasherManakaVR.Runtime.VrRuntimeManager.");
                }

                return;
            }

            _runtimeLogger ??= new BepInExVrRuntimeLogger(Plugin.Logger);
            _runtimeLogger.Info($"Found VR runtime type: {runtimeType.FullName} in {runtimeType.Assembly.GetName().Name}.");
            _runtimeLogger.Info("Creating OpenVR bridge.");
            _openVrBridge = new OpenVRBridge();
            _runtimeLogger.Info("Creating VR runtime instance.");
            _runtime = CreateRuntime(runtimeType, _runtimeLogger);
            CacheLifecycleMethods(runtimeType);
            LogLifecycleMethods(runtimeType);
            InvokeBestInitializeMethod(_runtime);
            _runtimeLogger.Info("VR runtime startup invocation completed.");
        }
        catch (Exception ex)
        {
            DisableRuntime("VR runtime initialization failed.", ex);
        }
    }

    private static Type? FindRuntimeType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var typeName in RuntimeTypeNames)
            {
                var type = assembly.GetType(typeName, throwOnError: false);
                if (type is not null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static object CreateRuntime(Type runtimeType, IVrRuntimeLogger runtimeLogger)
    {
        var loggerConstructor = runtimeType.GetConstructor(new[] { typeof(IVrRuntimeLogger) });
        if (loggerConstructor is not null)
        {
            return loggerConstructor.Invoke(new object[] { runtimeLogger });
        }

        return Activator.CreateInstance(runtimeType)
            ?? throw new InvalidOperationException("Could not construct runtime type.");
    }

    private void InvokeBestInitializeMethod(object runtime)
    {
        var runtimeType = runtime.GetType();
        var candidates = runtimeType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name is "Initialize" or "StartRuntime" or "Load")
            .OrderByDescending(method => method.GetParameters().Length);

        foreach (var method in candidates)
        {
            if (TryBuildArguments(method, out var arguments))
            {
                _runtimeLogger?.Info($"Invoking runtime initialization method: {runtimeType.FullName}.{method.Name}({arguments.Length} args).");
                method.Invoke(runtime, arguments);
                return;
            }
        }

        _runtimeLogger?.Warning($"No compatible initialization method was found on {runtimeType.FullName}.");
    }

    private bool TryBuildArguments(MethodInfo method, out object?[] arguments)
    {
        var parameters = method.GetParameters();
        arguments = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!TryResolveParameter(parameters[i].ParameterType, out arguments[i]))
            {
                arguments = Array.Empty<object?>();
                return false;
            }
        }

        return true;
    }

    private bool TryResolveParameter(Type parameterType, out object? value)
    {
        var candidates = new object?[]
        {
            _openVrBridge,
            BuildRuntimeSettings(),
            _runtimeLogger,
            this,
            _plugin,
            _settings
        };
        foreach (var candidate in candidates)
        {
            if (candidate is not null && parameterType.IsInstanceOfType(candidate))
            {
                value = candidate;
                return true;
            }
        }

        value = null;
        return false;
    }

    private VrRuntimeSettings BuildRuntimeSettings()
    {
        var settings = _settings;
        if (settings is null)
        {
            return new VrRuntimeSettings();
        }

        return new VrRuntimeSettings
        {
            EnableVR = settings.EnableVR.Value,
            AutoStartSteamVR = settings.AutoStartSteamVR.Value,
            IPDScale = settings.IPDScale.Value,
            WorldScale = settings.WorldScale.Value,
            CameraHeightOffset = settings.CameraHeightOffset.Value,
            RecenteringKey = settings.RecenteringKey.Value,
            AutoRecenterOnStart = settings.AutoRecenterOnStart.Value,
            SuppressMouseLookInput = settings.SuppressMouseLookInput.Value,
            IgnoreHeadPositionForVrCamera = settings.IgnoreHeadPositionForVrCamera.Value,
            HeadPositionCameraOffsetMinX = settings.HeadPositionCameraOffsetMinX.Value,
            HeadPositionCameraOffsetMaxX = settings.HeadPositionCameraOffsetMaxX.Value,
            HeadPositionCameraOffsetMinY = settings.HeadPositionCameraOffsetMinY.Value,
            HeadPositionCameraOffsetMaxY = settings.HeadPositionCameraOffsetMaxY.Value,
            HeadPositionCameraOffsetMinZ = settings.HeadPositionCameraOffsetMinZ.Value,
            HeadPositionCameraOffsetMaxZ = settings.HeadPositionCameraOffsetMaxZ.Value,
            VrCameraBasePositionSmoothFactor = settings.VrCameraBasePositionSmoothFactor.Value,
            VrCameraBaseRotationSmoothFactor = settings.VrCameraBaseRotationSmoothFactor.Value,
            SourceRotationMode = settings.SourceRotationMode.Value,
            MirrorMode = VrMirrorMode.Disabled,
            SceneTransitionVrPauseSeconds = settings.SceneTransitionVrPauseSeconds.Value,
            RenderScale = settings.RenderScale.Value,
            EnableVrCameraPostProcessing = settings.EnableVrCameraPostProcessing.Value,
            VrCameraPostProcessingWhitelist = settings.VrCameraPostProcessingWhitelist.Value,
            VrPp2EffectWhitelist = settings.VrPp2EffectWhitelist.Value,
            VrPp2VolumeLayer = settings.VrPp2VolumeLayer.Value,
            UseOpenVRProjection = settings.UseOpenVRProjection.Value,
            OpenVRProjectionMode = settings.OpenVRProjectionMode.Value,
            UseSourceProjectionForCulling = settings.UseSourceProjectionForCulling.Value,
            FlipSubmitV = settings.FlipSubmitV.Value,
            DisableSourceCameraRendering = true,
            DisableMirrorManagersWhileVrActive = settings.DisableMirrorManagersWhileVrActive.Value,
            DisableTargetTextureCameras = settings.DisableTargetTextureCameras.Value,
            PreventReflectionReenableWhileVrActive = settings.PreventReflectionReenableWhileVrActive.Value,
            BlockReflectionCameraRenderWhileVrActive = settings.BlockReflectionCameraRenderWhileVrActive.Value,
            BlockNestedCameraRenderDuringVrRender = settings.BlockNestedCameraRenderDuringVrRender.Value,
            DisableReflectionProbes = settings.DisableReflectionProbes.Value,
            ReflectionCameraNameKeywords = settings.ReflectionCameraNameKeywords.Value,
            EnableVrMirrorRenderer = settings.EnableVrMirrorRenderer.Value,
            VrMirrorUpdateIntervalFrames = settings.VrMirrorUpdateIntervalFrames.Value,
            VrMirrorMaxUpdatesPerFrame = settings.VrMirrorMaxUpdatesPerFrame.Value,
            VrMirrorMaxDistance = settings.VrMirrorMaxDistance.Value,
            EnableVrUiBridge = settings.EnableVrUiBridge.Value,
            ConvertOverlayCanvasToWorldSpace = settings.ConvertOverlayCanvasToWorldSpace.Value,
            VrUiFollowMode = settings.VrUiFollowMode.Value,
            IgnoreHeadRollForVrUi = settings.IgnoreHeadRollForVrUi.Value,
            VrUiDistance = settings.VrUiDistance.Value,
            VrUiVerticalOffset = settings.VrUiVerticalOffset.Value,
            VrUiPanelScale = settings.VrUiPanelScale.Value,
            VrUiPanelPixelOffsetY = settings.VrUiPanelPixelOffsetY.Value,
            VrUiStatusInfoOffsetX = settings.VrUiStatusInfoOffsetX.Value,
            VrUiStatusInfoOffsetY = settings.VrUiStatusInfoOffsetY.Value,
            EnableVrFullscreenEffectLayer = settings.EnableVrFullscreenEffectLayer.Value,
            VrFullscreenEffectPanelScale = settings.VrFullscreenEffectPanelScale.Value,
            VrFullscreenEffectCurveDegrees = settings.VrFullscreenEffectCurveDegrees.Value,
            VrFullscreenEffectDepthOffset = settings.VrFullscreenEffectDepthOffset.Value,
            VrFullscreenEffectHeartBeatAlphaBoost = settings.VrFullscreenEffectHeartBeatAlphaBoost.Value,
            VrUiFaceRtOffsetX = settings.VrUiFaceRtOffsetX.Value,
            VrUiFaceRtOffsetY = settings.VrUiFaceRtOffsetY.Value,
            VrUiFaceRtScale = settings.VrUiFaceRtScale.Value,
            VrUiBodyRtOffsetX = settings.VrUiBodyRtOffsetX.Value,
            VrUiBodyRtOffsetY = settings.VrUiBodyRtOffsetY.Value,
            VrUiBodyRtScale = settings.VrUiBodyRtScale.Value,
            VrUiMaxScanInterval = settings.VrUiMaxScanInterval.Value,
            VrUiCanvasNameWhitelist = settings.VrUiCanvasNameWhitelist.Value,
            VrUiCanvasNameBlacklist = settings.VrUiCanvasNameBlacklist.Value,
            FixNpcWorldSpaceUi = settings.FixNpcWorldSpaceUi.Value,
            NpcWorldSpaceUiVerticalOffset = settings.NpcWorldSpaceUiVerticalOffset.Value,
            NpcWorldSpaceUiScale = settings.NpcWorldSpaceUiScale.Value,
            NpcWorldSpaceUiMinScaleDistance = settings.NpcWorldSpaceUiMinScaleDistance.Value,
            NpcWorldSpaceUiMaxScaleDistance = settings.NpcWorldSpaceUiMaxScaleDistance.Value,
        };
    }

    private static VrMirrorMode ConvertMirrorMode(MirrorMode mode)
    {
        return mode switch
        {
            MirrorMode.Disabled => VrMirrorMode.Disabled,
            MirrorMode.LeftEye => VrMirrorMode.LeftEye,
            MirrorMode.RightEye => VrMirrorMode.RightEye,
            _ => VrMirrorMode.SourceCamera
        };
    }

    private void CacheLifecycleMethods(Type runtimeType)
    {
        _updateMethod = FindLifecycleMethod(runtimeType, "UpdateRuntime", "OnUpdate", "Tick");
        _lateUpdateMethod = FindLifecycleMethod(runtimeType, "LateUpdateRuntime", "OnLateUpdate", "LateTick");
        _shutdownMethod = FindLifecycleMethod(runtimeType, "Shutdown", "Dispose", "OnShutdown");
    }

    private void LogLifecycleMethods(Type runtimeType)
    {
        _runtimeLogger?.Info(
            "Runtime lifecycle methods: " +
            $"update={DescribeMethod(runtimeType, _updateMethod)}, " +
            $"lateUpdate={DescribeMethod(runtimeType, _lateUpdateMethod)}, " +
            $"shutdown={DescribeMethod(runtimeType, _shutdownMethod)}.");
    }

    private static string DescribeMethod(Type runtimeType, MethodInfo? method)
    {
        return method is null ? "not found" : $"{runtimeType.Name}.{method.Name}";
    }

    private static MethodInfo? FindLifecycleMethod(Type runtimeType, params string[] names)
    {
        return names
            .Select(name => runtimeType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null))
            .FirstOrDefault(method => method is not null);
    }

    private void InvokeLifecycle(MethodInfo? method, string phase)
    {
        if (_runtime is null || _runtimeDisabled || method is null)
        {
            return;
        }

        try
        {
            method.Invoke(_runtime, null);
        }
        catch (Exception ex)
        {
            DisableRuntime($"VR runtime lifecycle failed during {phase}.", ex);
        }
    }

    private void ShutdownRuntime(string reason)
    {
        if (_runtimeShutdownInProgress)
        {
            return;
        }

        if (_runtime is null && _openVrBridge is null)
        {
            ClearRuntimeReferences();
            return;
        }

        if (_runtimeShutdownAttempted)
        {
            ClearRuntimeReferences();
            return;
        }

        _runtimeShutdownAttempted = true;
        _runtimeShutdownInProgress = true;
        try
        {
            _runtimeLogger?.Info($"Shutting down VR runtime. Reason: {reason}");
            var openVrBridge = _openVrBridge;
            InvokeShutdown(_runtime, _shutdownMethod, reason, "runtime");
            InvokeShutdown(
                openVrBridge,
                openVrBridge is null ? null : FindLifecycleMethod(openVrBridge.GetType(), "Shutdown", "Dispose", "Stop"),
                reason,
                "OpenVR bridge");
        }
        finally
        {
            _runtimeShutdownInProgress = false;
            _runtimeLogger?.Info("Clearing VR runtime references.");
            ClearRuntimeReferences();
        }
    }

    private void InvokeShutdown(object? target, MethodInfo? shutdownMethod, string reason, string targetName)
    {
        if (target is null || shutdownMethod is null)
        {
            return;
        }

        try
        {
            shutdownMethod.Invoke(target, null);
            _runtimeLogger?.Info($"{targetName} shutdown completed. Reason: {reason}");
        }
        catch (Exception ex)
        {
            _runtimeLogger?.Error($"{targetName} shutdown failed. Reason: {reason}", ex);
        }
    }

    private void ClearRuntimeReferences()
    {
        _runtime = null;
        _openVrBridge = null;
        _updateMethod = null;
        _lateUpdateMethod = null;
        _shutdownMethod = null;
    }

    private void InvokeLateTick(string phase)
    {
        int frame = Time.frameCount;
        if (_lastLateTickFrame == frame)
        {
            return;
        }

        _lastLateTickFrame = frame;
        InvokeLifecycle(_lateUpdateMethod, phase);
    }

}
