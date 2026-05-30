using SecretFlasherManakaVR.Runtime;
using UnityEngine;

namespace SecretFlasherManakaVR.InputMapping;

internal sealed class Quest3CursorRayRenderer
{
    private GameObject? rayObject;
    private LineRenderer? line;
    private bool creationFailed;

    public void Tick(Quest3VirtualInputState state)
    {
        if (!Quest3CursorRay.TryCreate(state, out var ray))
        {
            SetVisible(false);
            return;
        }

        EnsureCreated();
        if (line == null)
        {
            return;
        }

        line.SetPosition(0, ray.Origin);
        Vector3 end = VrUiBridge.TryRaycastCapturedScreen(ray.Origin, ray.Direction, out _, out var hitPoint)
            ? hitPoint
            : ray.End;
        line.SetPosition(1, end);
        SetVisible(true);
    }

    public void Shutdown()
    {
        if (rayObject != null)
        {
            Object.Destroy(rayObject);
            rayObject = null;
            line = null;
        }
    }

    private void EnsureCreated()
    {
        if (line != null || creationFailed)
        {
            return;
        }

        try
        {
            rayObject = new GameObject("SecretFlasherManakaVR.Quest3CursorRay");
            Object.DontDestroyOnLoad(rayObject);
            rayObject.hideFlags = HideFlags.DontSave;
            rayObject.layer = VrUiBridge.VrUiOverlayLayer;
            line = rayObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.01f;
            line.endWidth = 0.0025f;
            line.material = CreateRayMaterial();
            line.startColor = new Color(0.2f, 0.85f, 1.0f, 0.95f);
            line.endColor = new Color(0.2f, 0.85f, 1.0f, 0.15f);
            line.sortingOrder = 1000;
            SetVisible(false);
        }
        catch (System.Exception)
        {
            creationFailed = true;
        }
    }

    private void SetVisible(bool visible)
    {
        if (line != null && line.enabled != visible)
        {
            line.enabled = visible;
        }
    }

    private static Material CreateRayMaterial()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        var material = new Material(shader);
        material.name = "SecretFlasherManakaVR Cursor Ray Material";
        material.renderQueue = 5000;
        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        return material;
    }
}
