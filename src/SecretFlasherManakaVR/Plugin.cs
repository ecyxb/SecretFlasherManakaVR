using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SecretFlasherManakaVR;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.codex.secretflashermanaka.vr";
    public const string PluginName = "SecretFlasherManaka VR";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Logger { get; private set; } = null!;
    internal static ModConfig Settings { get; private set; } = null!;

    private Harmony? _harmony;
    private GameObject? _runnerObject;

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo($"{PluginName} {PluginVersion} loading.");

        try
        {
            Logger.LogInfo("Binding configuration.");
            Settings = ModConfig.Bind(Config);
            Logger.LogInfo(
                "Configuration loaded. " +
                $"EnableVR={Settings.EnableVR.Value}, " +
                $"AutoStartSteamVR={Settings.AutoStartSteamVR.Value}, " +
                $"Quest3Input={Settings.EnableQuest3InputMapping.Value}, " +
                $"RenderScale={Settings.RenderScale.Value}.");

            RegisterHarmonyPatches();
            StartRunner();

            Logger.LogInfo($"{PluginName} load complete.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"{PluginName} failed during Load. {ex}");
            throw;
        }
    }

    private void RegisterHarmonyPatches()
    {
        try
        {
            Logger.LogInfo("Registering Harmony patches.");
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo("Harmony patches registered.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to register Harmony patches. VR mod will continue with reduced functionality. {ex}");
        }
    }

    private void StartRunner()
    {
        try
        {
            Logger.LogInfo("Starting VR runner host.");
            RegisterRunnerType();

            _runnerObject = new GameObject("SecretFlasherManakaVR.Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
            _runnerObject.hideFlags = HideFlags.DontSave;

            var runner = _runnerObject.AddComponent<VrRunnerHost>();
            runner.Initialize(this, Settings);
            Logger.LogInfo("VR runner host created and initialized.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to start VR runner host. {ex}");
        }
    }

    private static void RegisterRunnerType()
    {
        try
        {
            Logger.LogInfo("Registering VrRunnerHost IL2CPP type.");
            ClassInjector.RegisterTypeInIl2Cpp<VrRunnerHost>();
            Logger.LogInfo("VrRunnerHost IL2CPP type registered.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"VrRunnerHost IL2CPP type registration did not complete normally. It may already be registered. {ex}");
        }
    }
}
