using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
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
    private ManualLogSource? _logger;
    private object? _runtime;
    private object? _openVrBridge;
    private BepInExRuntimeLogger? _runtimeLogger;
    private MethodInfo? _updateMethod;
    private MethodInfo? _lateUpdateMethod;
    private MethodInfo? _shutdownMethod;
    private bool _startupAttempted;
    private bool _runtimeDisabled;
    private bool _missingRuntimeReported;

    public VrRunnerHost(IntPtr pointer)
        : base(pointer)
    {
    }

    public static VrRunnerHost? Instance { get; private set; }

    public bool IsRuntimeActive => _runtime is not null && !_runtimeDisabled;

    public ModConfig? Settings => _settings;

    public void Initialize(Plugin plugin, ModConfig settings, ManualLogSource logger)
    {
        _plugin = plugin;
        _settings = settings;
        _logger = logger;
        Instance = this;
    }

    private void Awake()
    {
        Instance = this;
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        TryStartRuntime();
    }

    private void Update()
    {
        if (!_startupAttempted)
        {
            TryStartRuntime();
        }

        InvokeLifecycle(_updateMethod, "update");
    }

    private void LateUpdate()
    {
        InvokeLifecycle(_lateUpdateMethod, "late update");
    }

    private void OnDestroy()
    {
        InvokeLifecycle(_shutdownMethod, "shutdown");
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public void DisableRuntime(string reason, Exception? exception = null)
    {
        _runtimeDisabled = true;
        if (exception is null)
        {
            _logger?.LogWarning($"{reason} VR is disabled; normal game continues.");
        }
        else
        {
            _logger?.LogWarning($"{reason} VR is disabled; normal game continues. {exception}");
        }
    }

    private void TryStartRuntime()
    {
        _startupAttempted = true;

        if (_runtimeDisabled || _settings is null)
        {
            return;
        }

        if (!_settings.EnableVR.Value)
        {
            _logger?.LogInfo("EnableVR=false; skipping VR runtime initialization.");
            _runtimeDisabled = true;
            return;
        }

        try
        {
            var runtimeType = FindRuntimeType();
            if (runtimeType is null)
            {
                if (!_missingRuntimeReported)
                {
                    _logger?.LogWarning("VR runtime type not found yet. Expected Agent C to provide SecretFlasherManakaVR.Runtime.VrRuntimeManager.");
                    _missingRuntimeReported = true;
                }

                return;
            }

            _runtimeLogger = new BepInExRuntimeLogger(_logger);
            _openVrBridge = new OpenVRBridge(
                message => _logger?.LogInfo(message),
                message => _logger?.LogWarning(message));
            _runtime = CreateRuntime(runtimeType, _runtimeLogger);
            InvokeBestInitializeMethod(_runtime);
            CacheLifecycleMethods(runtimeType);
            _logger?.LogInfo($"VR runtime attached: {runtimeType.FullName}");
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
            ?? throw new InvalidOperationException($"Could not construct runtime type {runtimeType.FullName}.");
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
                method.Invoke(runtime, arguments);
                return;
            }
        }

        _logger?.LogWarning($"Runtime {runtimeType.FullName} has no supported Initialize/StartRuntime/Load method; continuing with lifecycle calls only.");
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
            _settings,
            _logger
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
            SourceRotationMode = settings.SourceRotationMode.Value,
            MirrorMode = ConvertMirrorMode(settings.MirrorMode.Value),
            LogPoseDebug = settings.LogPoseDebug.Value,
            SceneTransitionVrPauseSeconds = settings.SceneTransitionVrPauseSeconds.Value,
            RenderScale = settings.RenderScale.Value,
            UseOpenVRProjection = settings.UseOpenVRProjection.Value,
            OpenVRProjectionMode = settings.OpenVRProjectionMode.Value,
            UseSourceProjectionForCulling = settings.UseSourceProjectionForCulling.Value,
            FlipSubmitV = settings.FlipSubmitV.Value,
            DisableReflectionCameras = settings.DisableReflectionCameras.Value,
            DisableTargetTextureCameras = settings.DisableTargetTextureCameras.Value,
            KeepReflectionCamerasDisabledWhileVrActive = settings.KeepReflectionCamerasDisabledWhileVrActive.Value,
            PreventReflectionReenableWhileVrActive = settings.PreventReflectionReenableWhileVrActive.Value,
            BlockReflectionCameraRenderWhileVrActive = settings.BlockReflectionCameraRenderWhileVrActive.Value,
            BlockNestedCameraRenderDuringVrRender = settings.BlockNestedCameraRenderDuringVrRender.Value,
            LogReflectionCameraDiagnostics = settings.LogReflectionCameraDiagnostics.Value,
            ReflectionCameraNameKeywords = settings.ReflectionCameraNameKeywords.Value
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
            DisableRuntime($"VR runtime {phase} failed.", ex);
        }
    }

    private sealed class BepInExRuntimeLogger : IVrRuntimeLogger
    {
        private readonly ManualLogSource? _source;

        public BepInExRuntimeLogger(ManualLogSource? source)
        {
            _source = source;
        }

        public void Info(string message)
        {
            _source?.LogInfo(message);
        }

        public void Warning(string message)
        {
            _source?.LogWarning(message);
        }

        public void Error(string message)
        {
            _source?.LogError(message);
        }

        public void Debug(string message)
        {
            _source?.LogDebug(message);
        }
    }
}
