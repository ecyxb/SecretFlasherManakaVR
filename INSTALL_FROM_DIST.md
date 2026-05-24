# Install From This Package

This package is meant to be copied into an existing game install that already has BepInEx set up.

## Install

Copy or merge these files into the game root:

```text
SecretFlasherManakaVR_Package\dist\BepInEx\plugins\SecretFlasherManakaVR.dll
SecretFlasherManakaVR_Package\dist\BepInEx\plugins\openvr_api.dll
SecretFlasherManakaVR_Package\dist\BepInEx\config\com.codex.secretflashermanaka.vr.cfg
```

Expected final locations:

```text
<GameRoot>\BepInEx\plugins\SecretFlasherManakaVR.dll
<GameRoot>\BepInEx\plugins\openvr_api.dll
<GameRoot>\BepInEx\config\com.codex.secretflashermanaka.vr.cfg
```

Do not remove or overwrite `SecretFlasherManakaMod.dll`.

## Build From Source

The package scripts are adapted to this folder layout:

```powershell
cd "SecretFlasherManakaVR_Package\scripts"
.\build.ps1 -GameRoot "<GameRoot>"
.\install.ps1 -GameRoot "<GameRoot>"
```

The scripts use `..\src\SecretFlasherManakaVR` as the default project directory and `..\dependencies` for `openvr_api.dll`.

## Test

1. Start SteamVR and connect the Quest PCVR stream.
2. Start the game on the PC.
3. Enter gameplay from the normal desktop window.
4. Check `BepInEx\LogOutput.log` for:

```text
OpenVR initialized via Valve binding
VR runtime initialized.
OpenVR texture submit succeeded for both eyes.
```
