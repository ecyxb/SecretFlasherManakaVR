using System;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class EyeMaskFinalComposite
    {
        private Material overlayMaterial;

        public void Apply(RenderTexture leftEye, RenderTexture rightEye, Texture eyeMaskTexture)
        {
            if (eyeMaskTexture == null)
            {
                return;
            }

            EnsureMaterial();
            if (overlayMaterial == null)
            {
                return;
            }

            overlayMaterial.mainTexture = eyeMaskTexture;
            Draw(leftEye);
            Draw(rightEye);
        }

        public void Shutdown()
        {
            if (overlayMaterial != null)
            {
                UnityEngine.Object.Destroy(overlayMaterial);
                overlayMaterial = null;
            }
        }

        private void EnsureMaterial()
        {
            if (overlayMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("No compatible shader found for VR eye mask final composite.");
            }

            overlayMaterial = new Material(shader);
            overlayMaterial.name = "SecretFlasherManakaVR Eye Mask Final Composite";
            overlayMaterial.renderQueue = 5000;
            overlayMaterial.SetInt("_Cull", 0);
        }

        private void Draw(RenderTexture target)
        {
            if (target == null || !target.IsCreated())
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadOrtho();
                overlayMaterial.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.TexCoord2(0.0f, 0.0f);
                GL.Vertex3(0.0f, 0.0f, 0.0f);
                GL.TexCoord2(1.0f, 0.0f);
                GL.Vertex3(1.0f, 0.0f, 0.0f);
                GL.TexCoord2(1.0f, 1.0f);
                GL.Vertex3(1.0f, 1.0f, 0.0f);
                GL.TexCoord2(0.0f, 1.0f);
                GL.Vertex3(0.0f, 1.0f, 0.0f);
                GL.End();
                GL.PopMatrix();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
