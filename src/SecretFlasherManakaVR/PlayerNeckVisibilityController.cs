using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

namespace SecretFlasherManakaVR;

internal static class PlayerNeckVisibilityController
{
    private const float NeckHiddenScale = 0.02f;

    private static Transform? neck;
    private static bool desiredHidden;
    private static int desiredFrame = -1;
    private static bool scaleActive;
    private static bool scaleModeLogged;
    private static Vector3 originalNeckScale;

    public static void CapturePlayer(PlayerAvatarObjectReferencer referencer)
    {
        Restore();
        desiredHidden = false;
        desiredFrame = -1;
        neck = referencer == null ? null : referencer.Neck;
    }

    public static void SetDesired(bool shouldHide)
    {
        desiredHidden = shouldHide &&
            neck != null &&
            Plugin.Settings != null &&
            Plugin.Settings.HidePlayerNeckInVrFirstPerson.Value;
        desiredFrame = Time.frameCount;

        if (!desiredHidden)
        {
            Restore();
        }
    }

    public static void BeginVrEyeRender()
    {
        try
        {
            if (!ShouldHideForCurrentRender())
            {
                Restore();
                return;
            }

            ApplyScale();
        }
        catch (Exception ex)
        {
            Restore();
            Plugin.Logger.LogWarning($"Player neck render-scope hide failed. {ex}");
        }
    }

    public static void EndVrEyeRender()
    {
        Restore();
    }

    private static bool ShouldHideForCurrentRender()
    {
        return desiredHidden &&
            desiredFrame == Time.frameCount &&
            neck != null &&
            Plugin.Settings != null &&
            Plugin.Settings.HidePlayerNeckInVrFirstPerson.Value;
    }

    private static void ApplyScale()
    {
        if (neck == null)
        {
            return;
        }

        if (!scaleActive)
        {
            originalNeckScale = neck.localScale;
            scaleActive = true;
            if (!scaleModeLogged)
            {
                Plugin.Logger.LogInfo("Player neck hide uses render-scoped neck bone scaling.");
                scaleModeLogged = true;
            }
        }

        neck.localScale = originalNeckScale * NeckHiddenScale;
    }

    private static void Restore()
    {
        if (!scaleActive)
        {
            return;
        }

        if (neck != null)
        {
            neck.localScale = originalNeckScale;
        }

        scaleActive = false;
    }
}
