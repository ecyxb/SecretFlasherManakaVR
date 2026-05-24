# OpenVR dependency notes

This project intentionally does not vendor unknown OpenVR binaries.

The bridge in `VRModSrc/SecretFlasherManakaVR/OpenVR` uses the official OpenVR C API exported by `openvr_api.dll`. For integration builds, obtain the runtime DLL from one of these official sources:

- SteamVR installation: `C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\openvr_api.dll`
- Valve OpenVR SDK release: https://github.com/ValveSoftware/openvr/releases

Place the x64 `openvr_api.dll` next to `SecretFlasherManakaVR.dll` in the final plugin install folder, for example:

```text
BepInEx/plugins/SecretFlasherManakaVR/SecretFlasherManakaVR.dll
BepInEx/plugins/SecretFlasherManakaVR/openvr_api.dll
```

Do not use a 32-bit DLL. The game and BepInEx IL2CPP process are Windows x64.

The bridge targets the current public OpenVR function-table interfaces from Valve headers:

- `FnTable:IVRSystem_026`
- `FnTable:IVRCompositor_029`

If SteamVR ships a future incompatible function-table version, update `OpenVRNative.SystemInterfaceVersions`, `OpenVRNative.CompositorInterfaceVersions`, and the corresponding function-table layouts from Valve's official `openvr_capi.h`.
