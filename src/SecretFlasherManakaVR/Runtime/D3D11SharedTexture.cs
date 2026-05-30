using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class D3D11SharedTexture : IDisposable
    {
        private const uint D3D11UsageDefault = 0;
        private const uint D3D11BindShaderResource = 0x8;
        private const uint D3D11BindRenderTarget = 0x20;

        private static readonly Guid Texture2DGuid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private IntPtr sourceTexture;
        private IntPtr device;
        private IntPtr immediateContext;
        private IntPtr sharedTexture;
        private IntPtr sharedHandle;

        private D3D11SharedTexture(IntPtr sourceTexture, IntPtr device, IntPtr immediateContext, IntPtr sharedTexture, IntPtr sharedHandle)
        {
            this.sourceTexture = sourceTexture;
            this.device = device;
            this.immediateContext = immediateContext;
            this.sharedTexture = sharedTexture;
            this.sharedHandle = sharedHandle;
        }

        public IntPtr NativePointer
        {
            get { return sharedTexture; }
        }

        public IntPtr SharedHandle
        {
            get { return sharedHandle; }
        }

        public bool HasSharedHandle
        {
            get { return sharedHandle != IntPtr.Zero; }
        }

        public static bool TryCreate(RenderTexture source, out D3D11SharedTexture texture, out string error)
        {
            texture = null;
            error = string.Empty;

            if (source == null)
            {
                error = "source RenderTexture is null";
                return false;
            }

            IntPtr nativeSource = source.GetNativeTexturePtr();
            if (nativeSource == IntPtr.Zero)
            {
                error = "source native texture pointer is null";
                return false;
            }

            IntPtr sourceTexture = IntPtr.Zero;
            IntPtr device = IntPtr.Zero;
            IntPtr context = IntPtr.Zero;
            IntPtr sharedTexture = IntPtr.Zero;
            IntPtr sharedHandle = IntPtr.Zero;

            try
            {
                int hr = QueryInterface(nativeSource, Texture2DGuid, out sourceTexture);
                if (hr < 0 || sourceTexture == IntPtr.Zero)
                {
                    error = "QueryInterface(ID3D11Texture2D) failed: 0x" + hr.ToString("X8");
                    return false;
                }

                var desc = GetTexture2DDescription(sourceTexture);
                desc.Usage = D3D11UsageDefault;
                desc.Format = ToSubmitFormat(desc.Format);
                desc.BindFlags |= D3D11BindShaderResource | D3D11BindRenderTarget;
                desc.CpuAccessFlags = 0;
                desc.MiscFlags = 0;

                GetDevice(sourceTexture, out device);
                if (device == IntPtr.Zero)
                {
                    error = "ID3D11Texture2D.GetDevice returned null";
                    return false;
                }

                GetImmediateContext(device, out context);
                if (context == IntPtr.Zero)
                {
                    error = "ID3D11Device.GetImmediateContext returned null";
                    return false;
                }

                hr = CreateTexture2D(device, ref desc, IntPtr.Zero, out sharedTexture);
                if (hr < 0 || sharedTexture == IntPtr.Zero)
                {
                    error = "ID3D11Device.CreateTexture2D(shared) failed: 0x" + hr.ToString("X8");
                    return false;
                }

                texture = new D3D11SharedTexture(sourceTexture, device, context, sharedTexture, sharedHandle);
                sourceTexture = IntPtr.Zero;
                device = IntPtr.Zero;
                context = IntPtr.Zero;
                sharedTexture = IntPtr.Zero;
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to create D3D11 shared texture: " + ex.Message;
                return false;
            }
            finally
            {
                ReleaseIfNeeded(sharedTexture);
                ReleaseIfNeeded(context);
                ReleaseIfNeeded(device);
                ReleaseIfNeeded(sourceTexture);
            }
        }

        public bool CopyFromSource(out string error)
        {
            error = string.Empty;
            if (sourceTexture == IntPtr.Zero || sharedTexture == IntPtr.Zero || immediateContext == IntPtr.Zero)
            {
                error = "shared texture is not initialized";
                return false;
            }

            try
            {
                CopyResource(immediateContext, sharedTexture, sourceTexture);
                return true;
            }
            catch (Exception ex)
            {
                error = "ID3D11DeviceContext.CopyResource failed: " + ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            ReleaseIfNeeded(sharedTexture);
            ReleaseIfNeeded(immediateContext);
            ReleaseIfNeeded(device);
            ReleaseIfNeeded(sourceTexture);
            sharedTexture = IntPtr.Zero;
            immediateContext = IntPtr.Zero;
            device = IntPtr.Zero;
            sourceTexture = IntPtr.Zero;
            sharedHandle = IntPtr.Zero;
        }

        private static int QueryInterface(IntPtr comObject, Guid interfaceGuid, out IntPtr result)
        {
            IntPtr function = ReadVTableFunction(comObject, 0);
            var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(function);
            return queryInterface(comObject, ref interfaceGuid, out result);
        }

        private static uint Release(IntPtr comObject)
        {
            IntPtr function = ReadVTableFunction(comObject, 2);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(function);
            return release(comObject);
        }

        private static void ReleaseIfNeeded(IntPtr comObject)
        {
            if (comObject != IntPtr.Zero)
            {
                Release(comObject);
            }
        }

        private static D3D11Texture2DDescription GetTexture2DDescription(IntPtr texture2D)
        {
            IntPtr function = ReadVTableFunction(texture2D, 10);
            var getDescription = Marshal.GetDelegateForFunctionPointer<GetDescriptionDelegate>(function);
            getDescription(texture2D, out var description);
            return description;
        }

        private static void GetDevice(IntPtr texture2D, out IntPtr device)
        {
            IntPtr function = ReadVTableFunction(texture2D, 3);
            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(function);
            getDevice(texture2D, out device);
        }

        private static int CreateTexture2D(IntPtr device, ref D3D11Texture2DDescription description, IntPtr initialData, out IntPtr texture)
        {
            IntPtr function = ReadVTableFunction(device, 5);
            var createTexture = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(function);
            return createTexture(device, ref description, initialData, out texture);
        }

        private static void GetImmediateContext(IntPtr device, out IntPtr context)
        {
            IntPtr function = ReadVTableFunction(device, 40);
            var getImmediateContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(function);
            getImmediateContext(device, out context);
        }

        private static void CopyResource(IntPtr context, IntPtr destination, IntPtr source)
        {
            IntPtr function = ReadVTableFunction(context, 47);
            var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(function);
            copyResource(context, destination, source);
        }

        private static uint ToSubmitFormat(uint sourceFormat)
        {
            if (sourceFormat == 27)
            {
                return 28;
            }

            if (sourceFormat == 90)
            {
                return 87;
            }

            return sourceFormat;
        }

        private static IntPtr ReadVTableFunction(IntPtr comObject, int slot)
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            return Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid interfaceGuid, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescriptionDelegate(IntPtr self, out D3D11Texture2DDescription description);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceDelegate(IntPtr self, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2DDelegate(IntPtr self, ref D3D11Texture2DDescription description, IntPtr initialData, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(IntPtr self, out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResourceDelegate(IntPtr self, IntPtr destination, IntPtr source);

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11Texture2DDescription
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public DxgiSampleDescription SampleDesc;
            public uint Usage;
            public uint BindFlags;
            public uint CpuAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DxgiSampleDescription
        {
            public uint Count;
            public uint Quality;
        }
    }
}
