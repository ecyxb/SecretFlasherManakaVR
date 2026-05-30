# SecretFlasherManakaVR Package Manifest

Package root:

```text
SecretFlasherManakaVR_Package
```

This directory contains the VR mod source, the ring-menu long-press helper source, external runtime DLLs, current configuration, documentation, and a ready-to-copy install layout. It intentionally does not include the game files or the BepInEx runtime.

## Contents

```text
SecretFlasherManakaVR_Package/
├─ README_VR.md
├─ PACKAGE_MANIFEST.md
├─ src/
│  └─ SecretFlasherManakaVR/
├─ scripts/
│  ├─ build.ps1
│  └─ install.ps1
├─ dependencies/
│  └─ openvr_api.dll
├─ config/
│  └─ com.codex.secretflashermanaka.vr.cfg
├─ docs/
│  └─ CHANGELOG_OR_PROGRESS.md
├─ third_party/
│  └─ openvr/
│     └─ README.md
└─ dist/
   ├─ BepInEx/
   │  ├─ plugins/
   │  │  ├─ SecretFlasherManakaVR.dll
   │  │  └─ openvr_api.dll
   │  └─ config/
   │     └─ com.codex.secretflashermanaka.vr.cfg
   └─ debug_symbols/
      └─ SecretFlasherManakaVR.pdb
```

## Source Mapping

- `src/SecretFlasherManakaVR/`: current C# plugin source. `bin/` and `obj/` are excluded.
- `src/SecretFlasherManakaRingMenuLongPress/`: separate C# plugin source for the ring-menu long-press threshold helper. `bin/` and `obj/` are excluded.
- `src/SecretFlasherManakaVR/OpenVR/Valve/openvr_api.cs`: Valve OpenVR C# binding used by `OpenVRBridge`.
- `scripts/build.ps1`: package-adapted build script; by default it builds both `src/SecretFlasherManakaVR` and `src/SecretFlasherManakaRingMenuLongPress`.
- `scripts/install.ps1`: package-adapted install script; by default it installs both plugin DLLs and the VR plugin dependencies.
- `dependencies/openvr_api.dll`: copied from `VRModSrc/Dependencies/openvr_api.dll`.
- `config/com.codex.secretflashermanaka.vr.cfg`: current VR config template.
- `config/com.codex.secretflashermanaka.ringmenulongpress.cfg`: current ring-menu long-press config template.
- `README_VR.md`: current development and test notes.
- `docs/CHANGELOG_OR_PROGRESS.md`: working progress log from this development thread.
- `third_party/openvr/README.md`: OpenVR third-party note kept with the package.

## Ready Install Layout

`dist/BepInEx/` mirrors the files that should be merged into a game install:

```text
dist/BepInEx/plugins/SecretFlasherManakaVR.dll
dist/BepInEx/plugins/SecretFlasherManakaRingMenuLongPress.dll
dist/BepInEx/plugins/openvr_api.dll
dist/BepInEx/config/com.codex.secretflashermanaka.vr.cfg
dist/BepInEx/config/com.codex.secretflashermanaka.ringmenulongpress.cfg
```

Do not copy the package's `src/`, `scripts/`, `docs/`, or `dependencies/` directories into the game unless you are developing.

## Excluded On Purpose

- Game executable and game data files.
- `BepInEx/core`.
- `BepInEx/interop`.
- `BepInEx/unity-libs`.
- Existing `BepInEx/plugins/SecretFlasherManakaMod.dll`.
- ReShade files.
- Build intermediates such as `bin/` and `obj/` under the source tree.

## Current Stable Baseline

The packaged build is the current camera-only mirror mitigation baseline:

- Mirror models remain visible.
- Reflection/targetTexture cameras are disabled while VR is active.
- Reflection `Camera.Render()` calls are blocked while VR is active.
- Reflection camera re-enable attempts are blocked while VR is active.
- Full-scene reflection renderer scanning has been removed.
- `SceneTransitionVrPauseSeconds = 1.5` remains enabled for first-scene transition stability.
