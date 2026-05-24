using System;
using System.Runtime.InteropServices;

namespace SecretFlasherManakaVR.Runtime
{
    internal static class D3D11TextureDiagnostics
    {
        private static readonly Guid Texture2DGuid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        public static string Describe(IntPtr nativeTexturePtr)
        {
            if (nativeTexturePtr == IntPtr.Zero)
            {
                return "native texture pointer is null";
            }

            IntPtr texture2D = IntPtr.Zero;
            try
            {
                int hr = QueryInterface(nativeTexturePtr, Texture2DGuid, out texture2D);
                if (hr < 0 || texture2D == IntPtr.Zero)
                {
                    return "QueryInterface(ID3D11Texture2D) failed: 0x" + hr.ToString("X8");
                }

                var desc = GetTexture2DDescription(texture2D);
                return "dxgiFormat=" + desc.Format +
                    " size=" + desc.Width + "x" + desc.Height +
                    " mipLevels=" + desc.MipLevels +
                    " arraySize=" + desc.ArraySize +
                    " samples=" + desc.SampleDesc.Count + "/" + desc.SampleDesc.Quality +
                    " usage=0x" + desc.Usage.ToString("X") +
                    " bind=0x" + desc.BindFlags.ToString("X") +
                    " cpu=0x" + desc.CpuAccessFlags.ToString("X") +
                    " misc=0x" + desc.MiscFlags.ToString("X");
            }
            catch (Exception ex)
            {
                return "D3D11 texture inspection failed: " + ex.Message;
            }
            finally
            {
                if (texture2D != IntPtr.Zero)
                {
                    Release(texture2D);
                }
            }
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

        private static D3D11Texture2DDescription GetTexture2DDescription(IntPtr texture2D)
        {
            IntPtr function = ReadVTableFunction(texture2D, 10);
            var getDescription = Marshal.GetDelegateForFunctionPointer<GetDescriptionDelegate>(function);
            getDescription(texture2D, out var description);
            return description;
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
