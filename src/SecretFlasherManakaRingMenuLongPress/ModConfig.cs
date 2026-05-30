using BepInEx.Configuration;

namespace SecretFlasherManakaRingMenuLongPress;

internal sealed class ModConfig
{
    private const string RingMenuSection = "RingMenu - 环形菜单";

    private ModConfig(ConfigEntry<bool> enablePatch, ConfigEntry<int> ringMenuLongPressCount)
    {
        EnablePatch = enablePatch;
        RingMenuLongPressCount = ringMenuLongPressCount;
    }

    public ConfigEntry<bool> EnablePatch { get; }

    public ConfigEntry<int> RingMenuLongPressCount { get; }

    public static ModConfig Bind(ConfigFile config)
    {
        ConfigEntry<bool> enablePatch = config.Bind(
            RingMenuSection,
            nameof(EnablePatch),
            true,
            "是否启用环形菜单长按阈值替换。 / Enables replacing the ring-menu-only long-press threshold.");

        ConfigEntry<int> ringMenuLongPressCount = config.Bind(
            RingMenuSection,
            nameof(RingMenuLongPressCount),
            20,
            new ConfigDescription(
                "传给 RingMenuParentView 的 InputManager.GetLongDown 长按计数。原版环形菜单为 10，原版通用长按为 20；数值越大越不容易误开环形菜单。 / Long-press count passed to InputManager.GetLongDown for RingMenuParentView. Vanilla ring menu uses 10, and vanilla generic long-press uses 20; larger values make accidental ring-menu opens less likely.",
                new AcceptableValueRange<int>(1, 120)));

        return new ModConfig(enablePatch, ringMenuLongPressCount);
    }
}
