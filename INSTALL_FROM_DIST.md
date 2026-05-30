# Install From This Package

This package is meant to be copied into an existing game install that already has BepInEx set up.

## Install

Copy or merge these files into the game root:

```text
SecretFlasherManakaVR_Package\dist\BepInEx\plugins\SecretFlasherManakaVR.dll
SecretFlasherManakaVR_Package\dist\BepInEx\plugins\SecretFlasherManakaRingMenuLongPress.dll
SecretFlasherManakaVR_Package\dist\BepInEx\plugins\openvr_api.dll
SecretFlasherManakaVR_Package\dist\BepInEx\config\com.codex.secretflashermanaka.vr.cfg
SecretFlasherManakaVR_Package\dist\BepInEx\config\com.codex.secretflashermanaka.ringmenulongpress.cfg
```

Expected final locations:

```text
<GameRoot>\BepInEx\plugins\SecretFlasherManakaVR.dll
<GameRoot>\BepInEx\plugins\SecretFlasherManakaRingMenuLongPress.dll
<GameRoot>\BepInEx\plugins\openvr_api.dll
<GameRoot>\BepInEx\config\com.codex.secretflashermanaka.vr.cfg
<GameRoot>\BepInEx\config\com.codex.secretflashermanaka.ringmenulongpress.cfg
```

Do not remove or overwrite `SecretFlasherManakaMod.dll`.

## Build From Source

The package scripts are adapted to this folder layout:

```powershell
cd "SecretFlasherManakaVR_Package\scripts"
.\build.ps1 -GameRoot "<GameRoot>"
.\install.ps1 -GameRoot "<GameRoot>"
```

By default, the scripts build and install both `SecretFlasherManakaVR.dll` and `SecretFlasherManakaRingMenuLongPress.dll`. They still remain separate BepInEx plugins. The VR plugin uses `..\dependencies` for `openvr_api.dll`.

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
