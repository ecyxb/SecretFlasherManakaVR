using System;
using UnityEngine;

namespace SecretFlasherManakaVR;

internal static class ReflectionBlocker
{
    public static bool IsReflectionCameraCandidate(Camera camera)
    {
        if (camera == null || camera.gameObject == null || IsPluginEyeCamera(camera) || IsUiPreviewCamera(camera))
        {
            return false;
        }

        if (NameContainsReflectionKeyword(camera.gameObject.name))
        {
            return true;
        }

        RenderTexture target = camera.targetTexture;
        return Plugin.Settings != null &&
            Plugin.Settings.DisableTargetTextureCameras.Value &&
            target != null &&
            !IsPluginEyeTexture(target);
    }

    public static bool IsUiPreviewCamera(Camera camera)
    {
        if (camera == null || camera.gameObject == null)
        {
            return false;
        }

        string cameraName = camera.gameObject.name ?? string.Empty;
        if (NameEqualsAny(cameraName, "BodyCamera", "FaceCamera"))
        {
            return true;
        }

        RenderTexture target = camera.targetTexture;
        string targetName = target == null ? string.Empty : target.name ?? string.Empty;
        return NameContainsAny(targetName, "BodyCamera", "FaceCamera");
    }

    public static bool IsReflectionProbeCandidate(ReflectionProbe probe)
    {
        return probe != null &&
            probe.gameObject != null &&
            Plugin.Settings != null &&
            Plugin.Settings.DisableReflectionProbes.Value;
    }

    public static bool IsMirrorManagerCandidate(Behaviour behaviour)
    {
        return behaviour != null &&
            Plugin.Settings != null &&
            Plugin.Settings.DisableMirrorManagersWhileVrActive.Value &&
            string.Equals(
                behaviour.GetType().FullName,
                "AkilliMum.Standard.Mirror.MirrorManager",
                StringComparison.Ordinal);
    }

    public static bool NameContainsReflectionKeyword(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string rawKeywords = Plugin.Settings == null
            ? "mirror,reflect,reflection,planar,water"
            : Plugin.Settings.ReflectionCameraNameKeywords.Value;
        string[] keywords = rawKeywords.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < keywords.Length; i++)
        {
            string keyword = keywords[i].Trim();
            if (keyword.Length > 0 && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string CameraDescription(Camera camera)
    {
        if (camera == null || camera.gameObject == null)
        {
            return "<null>";
        }

        RenderTexture target = camera.targetTexture;
        string targetName = target == null
            ? "none"
            : (string.IsNullOrEmpty(target.name) ? "<unnamed>" : target.name);
        string targetSize = target == null ? string.Empty : " " + target.width + "x" + target.height;
        return camera.gameObject.name + " targetTexture=" + targetName + targetSize;
    }

    public static string ProbeDescription(ReflectionProbe probe)
    {
        if (probe == null || probe.gameObject == null)
        {
            return "<null>";
        }

        return GetPath(probe.gameObject) +
            " enabled=" + probe.enabled +
            " mode=" + probe.mode +
            " refreshMode=" + probe.refreshMode +
            " timeSlicing=" + probe.timeSlicingMode +
            " resolution=" + probe.resolution +
            " cullingMask=0x" + probe.cullingMask.ToString("X");
    }

    public static string GetPath(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return "<null>";
        }

        Transform current = gameObject.transform;
        string path = gameObject.name;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.gameObject.name + "/" + path;
        }

        return path;
    }

    private static bool IsPluginEyeCamera(Camera camera)
    {
        string name = camera.gameObject.name;
        return string.Equals(name, "Left Eye", StringComparison.Ordinal) ||
            string.Equals(name, "Right Eye", StringComparison.Ordinal) ||
            string.Equals(name, "Left UI Overlay", StringComparison.Ordinal) ||
            string.Equals(name, "Right UI Overlay", StringComparison.Ordinal) ||
            string.Equals(name, "SecretFlasherManakaVR UI Capture Camera", StringComparison.Ordinal);
    }

    private static bool IsPluginEyeTexture(Texture texture)
    {
        return texture != null &&
            !string.IsNullOrEmpty(texture.name) &&
            texture.name.IndexOf("SecretFlasherManakaVR", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool NameEqualsAny(string value, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(value, names[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameContainsAny(string value, params string[] names)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (int i = 0; i < names.Length; i++)
        {
            if (value.IndexOf(names[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
