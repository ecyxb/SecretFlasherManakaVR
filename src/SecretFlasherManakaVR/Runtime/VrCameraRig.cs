using System;
using SecretFlasherManakaVR.OpenVR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class VrCameraRig
    {
        private const string RigName = "SecretFlasherManakaVR Camera Rig";

        private GameObject root;
        private Camera leftEye;
        private Camera rightEye;
        private Camera leftUiOverlay;
        private Camera rightUiOverlay;
        private RenderTexture leftTexture;
        private RenderTexture rightTexture;
        private D3D11SharedTexture leftSharedTexture;
        private D3D11SharedTexture rightSharedTexture;
        private int textureWidth;
        private int textureHeight;
        private int antiAliasing;
        private bool leftSharedTextureFailed;
        private bool rightSharedTextureFailed;

        public VrCameraRig(IVrRuntimeLogger logger)
        {
        }

        public Camera LeftEyeCamera
        {
            get { return leftEye; }
        }

        public Camera RightEyeCamera
        {
            get { return rightEye; }
        }

        public RenderTexture LeftTexture
        {
            get { return leftTexture; }
        }

        public RenderTexture RightTexture
        {
            get { return rightTexture; }
        }

        public IntPtr LeftSubmitTexturePtr
        {
            get { return GetSubmitPointer(leftTexture, leftSharedTexture); }
        }

        public IntPtr RightSubmitTexturePtr
        {
            get { return GetSubmitPointer(rightTexture, rightSharedTexture); }
        }

        public OpenVRTextureSubmitType LeftSubmitTextureType
        {
            get { return GetSubmitType(leftSharedTexture); }
        }

        public OpenVRTextureSubmitType RightSubmitTextureType
        {
            get { return GetSubmitType(rightSharedTexture); }
        }

        public Vector3 HeadPosition
        {
            get
            {
                if (leftEye != null && rightEye != null)
                {
                    return (leftEye.transform.position + rightEye.transform.position) * 0.5f;
                }

                return leftEye == null ? Vector3.zero : leftEye.transform.position;
            }
        }

        public Quaternion HeadRotation
        {
            get { return leftEye == null ? Quaternion.identity : leftEye.transform.rotation; }
        }

        public bool IsOurCamera(Camera camera)
        {
            return camera != null &&
                (camera == leftEye || camera == rightEye || camera == leftUiOverlay || camera == rightUiOverlay);
        }

        public void EnsureCreated()
        {
            if (root != null)
            {
                return;
            }

            root = new GameObject(RigName);
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;

            leftEye = CreateEyeCamera("Left Eye");
            rightEye = CreateEyeCamera("Right Eye");
            leftUiOverlay = CreateEyeCamera("Left UI Overlay");
            rightUiOverlay = CreateEyeCamera("Right UI Overlay");
        }

        public void EnsureRenderTextures(int width, int height, int aa)
        {
            EnsureCreated();
            width = Mathf.Clamp(width, 256, 8192);
            height = Mathf.Clamp(height, 256, 8192);
            aa = Mathf.Clamp(aa, 1, 8);

            if (leftTexture != null && rightTexture != null &&
                textureWidth == width && textureHeight == height && antiAliasing == aa)
            {
                return;
            }

            ReleaseRenderTextures();
            textureWidth = width;
            textureHeight = height;
            antiAliasing = aa;
            leftTexture = CreateRenderTexture("SecretFlasherManakaVR Left Eye", width, height, aa);
            rightTexture = CreateRenderTexture("SecretFlasherManakaVR Right Eye", width, height, aa);
            leftEye.targetTexture = leftTexture;
            rightEye.targetTexture = rightTexture;
            if (leftUiOverlay != null)
            {
                leftUiOverlay.targetTexture = leftTexture;
            }

            if (rightUiOverlay != null)
            {
                rightUiOverlay.targetTexture = rightTexture;
            }
        }

        public void CopyFromSource(Camera source)
        {
            if (source == null)
            {
                return;
            }

            CopyCamera(source, leftEye);
            CopyCamera(source, rightEye);
        }

        public void ApplyPose(Vector3 headPosition, Quaternion headRotation, float ipdMeters, float ipdScale, float worldScale)
        {
            Vector3 halfIpd = Vector3.right * (ipdMeters * 0.5f * ipdScale * worldScale);
            leftEye.transform.SetPositionAndRotation(headPosition + headRotation * -halfIpd, headRotation);
            rightEye.transform.SetPositionAndRotation(headPosition + headRotation * halfIpd, headRotation);
        }

        public void ApplyProjection(RuntimeEye eye, Matrix4x4 projection)
        {
            if (eye == RuntimeEye.Left)
            {
                leftEye.projectionMatrix = projection;
            }
            else
            {
                rightEye.projectionMatrix = projection;
            }
        }

        public void ApplyCullingFromSourceProjection(Camera source)
        {
            if (source == null)
            {
                return;
            }

            if (leftEye != null)
            {
                leftEye.cullingMatrix = source.projectionMatrix * leftEye.worldToCameraMatrix;
            }

            if (rightEye != null)
            {
                rightEye.cullingMatrix = source.projectionMatrix * rightEye.worldToCameraMatrix;
            }
        }

        public void ResetCullingMatrices()
        {
            if (leftEye != null)
            {
                leftEye.ResetCullingMatrix();
            }

            if (rightEye != null)
            {
                rightEye.ResetCullingMatrix();
            }
        }

        public void Render(int uiOverlayLayerMask)
        {
            VrRuntimeState.BeginVrEyeRender(leftEye, rightEye, leftUiOverlay, rightUiOverlay);
            try
            {
                if (leftEye != null && leftTexture != null)
                {
                    if (!leftTexture.IsCreated())
                    {
                        leftTexture.Create();
                    }

                    leftEye.Render();
                    RenderUiOverlay(leftEye, leftUiOverlay, leftTexture, uiOverlayLayerMask);
                }

                if (rightEye != null && rightTexture != null)
                {
                    if (!rightTexture.IsCreated())
                    {
                        rightTexture.Create();
                    }

                    rightEye.Render();
                    RenderUiOverlay(rightEye, rightUiOverlay, rightTexture, uiOverlayLayerMask);
                }
            }
            finally
            {
                VrRuntimeState.EndVrEyeRender();
            }

            // Keep all texture ownership inside Unity. Direct D3D11 copy/submit
            // paths were accepted by OpenVR but crashed in the NVIDIA user-mode
            // driver on some runs.
        }

        public void Mirror(VrMirrorMode mode)
        {
            if (mode == VrMirrorMode.LeftEye && leftTexture != null)
            {
                Graphics.Blit(leftTexture, (RenderTexture)null);
            }
            else if (mode == VrMirrorMode.RightEye && rightTexture != null)
            {
                Graphics.Blit(rightTexture, (RenderTexture)null);
            }
        }

        public void Shutdown()
        {
            ReleaseRenderTextures();

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }

            leftEye = null;
            rightEye = null;
            leftUiOverlay = null;
            rightUiOverlay = null;
        }

        private Camera CreateEyeCamera(string name)
        {
            GameObject cameraObject = new GameObject(name);
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            return camera;
        }

        private RenderTexture CreateRenderTexture(string name, int width, int height, int aa)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = 24,
                msaaSamples = aa,
                useMipMap = false,
                autoGenerateMips = false,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
                volumeDepth = 1
            };
            RenderTexture texture = new RenderTexture(descriptor);
            texture.name = name;
            texture.Create();
            return texture;
        }

        private void ReleaseRenderTextures()
        {
            if (leftEye != null)
            {
                leftEye.targetTexture = null;
            }

            if (rightEye != null)
            {
                rightEye.targetTexture = null;
            }

            if (leftUiOverlay != null)
            {
                leftUiOverlay.targetTexture = null;
            }

            if (rightUiOverlay != null)
            {
                rightUiOverlay.targetTexture = null;
            }

            ReleaseTexture(leftTexture);
            ReleaseTexture(rightTexture);
            ReleaseSharedTextures();
            leftTexture = null;
            rightTexture = null;
            leftSharedTextureFailed = false;
            rightSharedTextureFailed = false;
        }

        private void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }

        private void CopyCamera(Camera source, Camera target)
        {
            if (target == null)
            {
                return;
            }

            target.fieldOfView = source.fieldOfView;
            target.nearClipPlane = source.nearClipPlane;
            target.farClipPlane = source.farClipPlane;
            target.cullingMask = source.cullingMask;
            target.clearFlags = source.clearFlags;
            target.backgroundColor = source.backgroundColor;
            target.orthographic = source.orthographic;
            target.orthographicSize = source.orthographicSize;
            target.depth = source.depth;
            target.allowHDR = source.allowHDR;
            target.allowMSAA = source.allowMSAA;
            target.useOcclusionCulling = source.useOcclusionCulling;
            target.ResetCullingMatrix();
        }

        private void RenderUiOverlay(Camera eyeCamera, Camera overlayCamera, RenderTexture targetTexture, int layerMask)
        {
            if (eyeCamera == null || overlayCamera == null || targetTexture == null || layerMask == 0)
            {
                return;
            }

            overlayCamera.transform.SetPositionAndRotation(eyeCamera.transform.position, eyeCamera.transform.rotation);
            overlayCamera.targetTexture = targetTexture;
            overlayCamera.clearFlags = CameraClearFlags.Depth;
            overlayCamera.backgroundColor = Color.clear;
            overlayCamera.cullingMask = layerMask;
            overlayCamera.fieldOfView = eyeCamera.fieldOfView;
            overlayCamera.nearClipPlane = eyeCamera.nearClipPlane;
            overlayCamera.farClipPlane = eyeCamera.farClipPlane;
            overlayCamera.orthographic = eyeCamera.orthographic;
            overlayCamera.orthographicSize = eyeCamera.orthographicSize;
            overlayCamera.allowHDR = eyeCamera.allowHDR;
            overlayCamera.allowMSAA = eyeCamera.allowMSAA;
            overlayCamera.useOcclusionCulling = false;
            overlayCamera.projectionMatrix = eyeCamera.projectionMatrix;
            overlayCamera.cullingMatrix = eyeCamera.projectionMatrix * overlayCamera.worldToCameraMatrix;
            overlayCamera.Render();
        }

        private void PrepareSharedSubmitTextures()
        {
            PrepareSharedSubmitTexture(leftTexture, ref leftSharedTexture, ref leftSharedTextureFailed);
            PrepareSharedSubmitTexture(rightTexture, ref rightSharedTexture, ref rightSharedTextureFailed);
        }

        private void PrepareSharedSubmitTexture(RenderTexture source, ref D3D11SharedTexture sharedTexture, ref bool failed)
        {
            if (source == null || !source.IsCreated())
            {
                return;
            }

            if (sharedTexture == null && !failed)
            {
                if (D3D11SharedTexture.TryCreate(source, out sharedTexture, out _))
                {
                }
                else
                {
                    failed = true;
                    return;
                }
            }

            if (sharedTexture != null && !sharedTexture.CopyFromSource(out _))
            {
                sharedTexture.Dispose();
                sharedTexture = null;
                failed = true;
            }
        }

        private void ReleaseSharedTextures()
        {
            if (leftSharedTexture != null)
            {
                leftSharedTexture.Dispose();
                leftSharedTexture = null;
            }

            if (rightSharedTexture != null)
            {
                rightSharedTexture.Dispose();
                rightSharedTexture = null;
            }
        }

        private static IntPtr GetNativePointer(RenderTexture texture)
        {
            return texture == null ? IntPtr.Zero : texture.GetNativeTexturePtr();
        }

        private static IntPtr GetSubmitPointer(RenderTexture texture, D3D11SharedTexture sharedTexture)
        {
            if (sharedTexture != null)
            {
                return sharedTexture.NativePointer;
            }

            return GetNativePointer(texture);
        }

        private static OpenVRTextureSubmitType GetSubmitType(D3D11SharedTexture sharedTexture)
        {
            return OpenVRTextureSubmitType.DirectX;
        }
    }
}
