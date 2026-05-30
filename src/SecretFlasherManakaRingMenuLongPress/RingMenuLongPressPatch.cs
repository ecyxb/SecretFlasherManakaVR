using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.Scripts.Base;
using HarmonyLib;

namespace SecretFlasherManakaRingMenuLongPress;

[HarmonyPatch(typeof(RingMenuParentView), nameof(RingMenuParentView.GetLongDown), typeof(InputManager.InputType))]
internal static class RingMenuParentViewGetLongDownPatch
{
    private static bool Prefix(InputManager.InputType type, ref bool __result)
    {
        if (!Plugin.Settings.EnablePatch.Value)
        {
            return true;
        }

        int longPressCount = Plugin.Settings.RingMenuLongPressCount.Value;
        __result = GetLongDownWithConfiguredCount(type, longPressCount);
        return false;
    }

    private static bool GetLongDownWithConfiguredCount(InputManager.InputType type, int longPressCount)
    {
        if (!InputManager.IsGamePad)
        {
            return InputManager.GetLongDown(type, longPressCount);
        }

        if (!IsShiftedSkillRingMenu(type))
        {
            return InputManager.GetLongDown(type, longPressCount);
        }

        if (!InputManager.IsDown(InputManager.InputType.PadShift))
        {
            return false;
        }

        return InputManager.GetLongDown(MapShiftedSkillRingMenu(type), longPressCount);
    }

    private static bool IsShiftedSkillRingMenu(InputManager.InputType type)
    {
        return type == InputManager.InputType.SkillRingMenu5 ||
            type == InputManager.InputType.SkillRingMenu6 ||
            type == InputManager.InputType.SkillRingMenu7 ||
            type == InputManager.InputType.SkillRingMenu8;
    }

    private static InputManager.InputType MapShiftedSkillRingMenu(InputManager.InputType type)
    {
        switch (type)
        {
            case InputManager.InputType.SkillRingMenu5:
                return InputManager.InputType.Interact;
            case InputManager.InputType.SkillRingMenu6:
                return InputManager.InputType.Crouch;
            case InputManager.InputType.SkillRingMenu7:
                return InputManager.InputType.Dash;
            case InputManager.InputType.SkillRingMenu8:
                return InputManager.InputType.CloseClothesA;
            default:
                return type;
        }
    }
}
