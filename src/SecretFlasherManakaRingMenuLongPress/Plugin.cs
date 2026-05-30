using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SecretFlasherManakaRingMenuLongPress;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.codex.secretflashermanaka.ringmenulongpress";
    public const string PluginName = "SecretFlasherManaka Ring Menu Long Press";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Logger { get; private set; } = null!;
    internal static ModConfig Settings { get; private set; } = null!;

    private Harmony? _harmony;

    public override void Load()
    {
        Logger = Log;
        Settings = ModConfig.Bind(Config);

        Logger.LogInfo($"{PluginName} {PluginVersion} loading.");
        Logger.LogInfo($"Ring menu long-press count = {Settings.RingMenuLongPressCount.Value}.");

        try
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo("Harmony patch registered.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to register Harmony patch. The game default will remain active. {ex}");
        }
    }
}
