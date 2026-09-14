using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace SecretFlasherManakaPoseLab;

[BepInPlugin("com.codex.secretflashermanaka.poselab", "Manaka Desktop Pose Lab", "0.2.0")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    private Harmony? harmony;
    public override void Load()
    {
        Logger = Log;
        ClassInjector.RegisterTypeInIl2Cpp<PoseLab>();
        ClassInjector.RegisterTypeInIl2Cpp<PoseCameraHook>();
        AddComponent<PoseLab>();
        harmony = new Harmony("com.codex.secretflashermanaka.poselab");
        harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Desktop Pose Lab loaded. F6 control, F7 demo, F8 camera, F9 reset. No VR runtime required.");
    }
    public override bool Unload()
    {
        PoseLab.Instance?.Shutdown();
        harmony?.UnpatchSelf();
        return true;
    }
}

[HarmonyPatch(typeof(EventMethodManager), "LateUpdate")]
internal static class AfterGameAnimation
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix() => PoseLab.Instance?.SafeApply();
}

public sealed class PoseCameraHook : UnityEngine.MonoBehaviour
{
    public PoseCameraHook(IntPtr ptr) : base(ptr) { }
    private void OnPreCull() => PoseLab.Instance?.SafeApply();
}
