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
        Settings = ModConfig.Bind(Config, Logger);

        Logger.LogInfo($"{PluginName} {PluginVersion} loading.");

        RegisterHarmonyPatches();
        StartRunner();
    }

    private void RegisterHarmonyPatches()
    {
        try
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo("Harmony patch scan completed.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Harmony patch registration failed; VR bootstrap will continue without patches. {ex}");
        }
    }

    private void StartRunner()
    {
        try
        {
            RegisterRunnerType();

            _runnerObject = new GameObject("SecretFlasherManakaVR.Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
            _runnerObject.hideFlags = HideFlags.DontSave;

            var runner = _runnerObject.AddComponent<VrRunnerHost>();
            runner.Initialize(this, Settings, Logger);

            Logger.LogInfo(Settings.EnableVR.Value
                ? "VR runner created; runtime will initialize when available."
                : "VR runner created with EnableVR=false; normal game remains unchanged.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create VR runner. VR is disabled and the normal game will continue. {ex}");
        }
    }

    private static void RegisterRunnerType()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<VrRunnerHost>();
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"VrRunnerHost registration skipped or already completed: {ex.Message}");
        }
    }
}
