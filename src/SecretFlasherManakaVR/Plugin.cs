using System;
using BepInEx;
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

    internal static ModConfig Settings { get; private set; } = null!;

    private Harmony? _harmony;
    private GameObject? _runnerObject;

    public override void Load()
    {
        Settings = ModConfig.Bind(Config);

        RegisterHarmonyPatches();
        StartRunner();
    }

    private void RegisterHarmonyPatches()
    {
        try
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }
        catch (Exception)
        {
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
            runner.Initialize(this, Settings);
        }
        catch (Exception)
        {
        }
    }

    private static void RegisterRunnerType()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<VrRunnerHost>();
        }
        catch (Exception)
        {
        }
    }
}
