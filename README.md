# SecretFlasherManaka VR Package

Language: English | [简体中文](README.zh-CN.md)

This repository packages two separate BepInEx IL2CPP plugins for SecretFlasherManaka:

- `SecretFlasherManakaVR.dll`: sends the game view to SteamVR/OpenVR for PCVR play.
- `SecretFlasherManakaRingMenuLongPress.dll`: makes the ring-menu long-press threshold configurable.

The package does not include game files, BepInEx, or `SecretFlasherManakaMod.dll`. It is meant to be copied into an existing game install that already has BepInEx IL2CPP set up.

## Known Issues

- Use the latest BepInEx 6 IL2CPP release for this game before installing this mod package. Older BepInEx builds can cause plugin loading, interop, or initialization failures.
- The VR cursor may not line up with the hand pose. Resetting the in-game view to first person usually fixes it.
- There are unknown causes that may lead to game freezes or crashes. If this happens, try disabling post-processing or other rendering-related options in `com.codex.secretflashermanaka.vr.cfg`.
- Mirrors and reflections are disabled. There is no current plan to fix or restore them.

## Risk Warning

This package has not been adapted for or tested with any other mods. It also uses many hooks and special-case runtime handling functions, so compatibility with other mods is not guaranteed.

This package has not gone through a full end-to-end playthrough test.

Input mapping is mainly tested for Quest 3 / Oculus Touch controllers. Other SteamVR controllers may require custom bindings and are not guaranteed to work fully.

During scene transitions, special cutscene cameras, or GameOver cameras, the VR view may briefly black out, misalign, or jitter. Waiting for the transition to finish or recentering the view usually restores it.

## Features

- SteamVR/OpenVR stereo output using injected left/right eye cameras.
- Desktop mirror output using the source camera, left eye, right eye, or disabled mirror mode.
- Quest 3 controller mapping through SteamVR Input, with a legacy OpenVR controller-state fallback.
- HMD-driven camera pose, optional player head/neck/chest pose adjustment, and optional body yaw turning while moving.
- VR UI capture for supported screen-space canvases, including HUD positioning, fullscreen-effect overlay separation, and NPC marker reprojection.
- Reflection and mirror stability guards that keep mirror models visible while blocking recursive reflection rendering paths.
- Ring-menu long-press threshold helper as a separate BepInEx plugin.

## Install

Copy or merge the files under `dist/BepInEx` into the game root:

```text
dist/BepInEx/plugins/SecretFlasherManakaVR.dll
dist/BepInEx/plugins/SecretFlasherManakaRingMenuLongPress.dll
dist/BepInEx/plugins/openvr_api.dll
dist/BepInEx/plugins/SecretFlasherManakaVR_Input/actions.json
dist/BepInEx/plugins/SecretFlasherManakaVR_Input/bindings_oculus_touch.json
dist/BepInEx/config/com.codex.secretflashermanaka.vr.cfg
dist/BepInEx/config/com.codex.secretflashermanaka.ringmenulongpress.cfg
```

Expected final locations:

```text
<GameRoot>/BepInEx/plugins/SecretFlasherManakaVR.dll
<GameRoot>/BepInEx/plugins/SecretFlasherManakaRingMenuLongPress.dll
<GameRoot>/BepInEx/plugins/openvr_api.dll
<GameRoot>/BepInEx/plugins/SecretFlasherManakaVR_Input/actions.json
<GameRoot>/BepInEx/plugins/SecretFlasherManakaVR_Input/bindings_oculus_touch.json
<GameRoot>/BepInEx/config/com.codex.secretflashermanaka.vr.cfg
<GameRoot>/BepInEx/config/com.codex.secretflashermanaka.ringmenulongpress.cfg
```

Do not remove or overwrite `SecretFlasherManakaMod.dll`.

## Dependencies

Runtime requirements:

- SecretFlasherManaka v1.1.3.
- BepInEx 6 IL2CPP installed for the game.
- Generated BepInEx interop assemblies under `BepInEx/interop`. Launch the game once with BepInEx if they are missing.
- SteamVR/OpenVR for PCVR output.
- `openvr_api.dll` beside `SecretFlasherManakaVR.dll`.

Build requirements:

- .NET SDK with `dotnet`.
- The game install path passed to the build script so the project can reference `BepInEx/core` and `BepInEx/interop`.

## Build From Source

From the package root:

```powershell
scripts\build.ps1 -GameRoot "<GameRoot>" -Configuration Release
scripts\install.ps1 -GameRoot "<GameRoot>" -Configuration Release
```

By default, `build.ps1` builds both plugin projects:

- `src/SecretFlasherManakaVR/SecretFlasherManakaVR.csproj`
- `src/SecretFlasherManakaRingMenuLongPress/SecretFlasherManakaRingMenuLongPress.csproj`

The two plugins remain separate DLLs. `install.ps1` copies both plugin DLLs, the VR plugin's OpenVR dependency, and the SteamVR Input JSON files.

## Configuration

The installed configuration files live under `BepInEx/config`.

### VR Config

File:

```text
com.codex.secretflashermanaka.vr.cfg
```

The snippets below match the shipped config templates. If an older BepInEx config already exists, missing keys are added the next time the plugin loads, but manually edited values are preserved.

Projection, reflection, and SteamVR action tuning internals are fixed in code so normal users do not have to maintain a fragile advanced configuration set.

Core:

```ini
EnableVR = true
AutoStartSteamVR = true
MirrorMode = MainCamera
```

Stereo rendering:

```ini
CameraHeightOffset = 0
RenderScale = 0.5
EnableVrCameraPostProcessing = true
VrCameraPostProcessingWhitelist = UB.VignettesPE,UB.ExposuresPE,UB.BleachsBypassPE,UB.VintagesPE
VrPp2EffectWhitelist =
VrPp2VolumeLayer = 30
```

Input:

```ini
RecenteringKey = F12
EnableQuest3InputMapping = true
Quest3RightStickDeadzone = 0.35
```

Body and view:

```ini
EnablePlayerHeadPoseControl = true
PlayerHeadPoseFollowDistance = 0.75
PlayerHeadPoseYawLimitDegrees = 70
PlayerHeadPosePitchLimitDegrees = 55
PlayerHeadPoseRollLimitDegrees = 20
PlayerHeadPoseYawTurnsBodyWhileMoving = true
PlayerHeadPoseBodyYawTurnSpeedDegreesPerSecond = 180
IgnoreHeadPositionForVrCamera = true
HeadPositionCameraOffsetMinX = -0.05
HeadPositionCameraOffsetMaxX = 0.05
HeadPositionCameraOffsetMinY = -0.05
HeadPositionCameraOffsetMaxY = 0.05
HeadPositionCameraOffsetMinZ = -0.05
HeadPositionCameraOffsetMaxZ = 0.05
VrCameraBasePositionSmoothFactor = 18
VrCameraBaseRotationSmoothFactor = 24
PlayerHeadPoseChestWeight = 0.15
PlayerHeadPoseNeckWeight = 0.3
PlayerHeadPoseHeadWeight = 0.55
PlayerHeadPoseSmoothFactor = 18
HidePlayerNeckInVrFirstPerson = true
```

`VrCameraBasePositionSmoothFactor` and `VrCameraBaseRotationSmoothFactor` smooth the source game camera before the raw HMD pose is applied. Higher values follow faster; `0` disables smoothing.

VR UI panel:

```ini
EnableVrUiBridge = true
ConvertOverlayCanvasToWorldSpace = true
VrUiFollowMode = HeadLocked
IgnoreHeadRollForVrUi = true
VrUiDistance = 1.4
VrUiVerticalOffset = -0.1
VrUiPanelScale = 1.08
VrUiPanelPixelOffsetY = -150
VrUiStatusInfoOffsetX = -250
VrUiStatusInfoOffsetY = 150
EnableVrFullscreenEffectLayer = true
VrFullscreenEffectPanelScale = 2.7
VrFullscreenEffectCurveDegrees = 36
VrFullscreenEffectDepthOffset = 0.01
VrFullscreenEffectHeartBeatAlphaBoost = 8
VrUiFaceRtOffsetX = -100
VrUiFaceRtOffsetY = 300
VrUiFaceRtScale = 1
VrUiBodyRtOffsetX = 100
VrUiBodyRtOffsetY = 0
VrUiBodyRtScale = 1
VrUiMaxScanInterval = 1
VrUiCanvasNameWhitelist =
VrUiCanvasNameBlacklist =
```

NPC UI:

```ini
FixNpcWorldSpaceUi = true
NpcWorldSpaceUiVerticalOffset = 0.25
NpcWorldSpaceUiScale = 0.0015
NpcWorldSpaceUiMinScaleDistance = 3
NpcWorldSpaceUiMaxScaleDistance = 7
```

### Ring Menu Long Press Config

File:

```text
com.codex.secretflashermanaka.ringmenulongpress.cfg
```

Settings:

```ini
EnablePatch = true
RingMenuLongPressCount = 20
```

The vanilla ring menu uses a shorter long-press count. Raising the value makes accidental ring-menu opens less likely.

## JSON Configuration And VR Input System

The Quest 3 input path uses SteamVR Input JSON resources installed under:

```text
BepInEx/plugins/SecretFlasherManakaVR_Input/
```

Files:

- `actions.json`: declares the `/actions/quest3` action set and named actions such as buttons, triggers, sticks, and controller poses.
- `bindings_oculus_touch.json`: provides the default Oculus Touch / Quest controller binding for those actions.

When `EnableQuest3InputMapping = true`, the VR plugin calls SteamVR's `SetActionManifestPath` with `SecretFlasherManakaVR_Input/actions.json`. The manifest then refers to `bindings_oculus_touch.json`.

If `actions.json` is missing, SteamVR action input cannot initialize. If `bindings_oculus_touch.json` is missing, the manifest may load but Quest/Oculus Touch default bindings are not available. The plugin can fall back to legacy OpenVR controller state, but that fallback is less expressive and should not be treated as the primary input path.

### Quest 3 Input Mapping

`mode1` and `mode2` are temporary modifier modes. Outside menu UI, hold `L2` and press `ABXY` or `R2` to enter `mode1`; hold `L1` and press `ABXY` or `R2` to enter `mode2`. If `L2` / `L1` is released without forming an `ABXY` / `R2` chord and the hold lasted less than `1.5s`, it outputs `Start` / `Select`; releases after a hold longer than `1.5s` do not output `Start` / `Select`. Entering a mode does not suppress input. Exiting a mode suppresses currently held `ABXY` until release, `R2` until release, and the right stick until it returns to neutral.

| Quest 3 input | mode0 / normal | mode1 / L2 temporary mode | mode2 / L1 temporary mode | Cursor mode |
| --- | --- | --- | --- | --- |
| A | Cross; with L2 held enters mode1, with L1 held enters mode2 | DPadDown | Cross | `Interact` + `Accept` |
| B | Circle; with L2 held enters mode1, with L1 held enters mode2 | DPadRight | Circle | `Cancel` + `SystemMenu` |
| X | Triangle; with L2 held enters mode1, with L1 held enters mode2 | DPadUp | Triangle | Triangle |
| Y | Square; with L2 held enters mode1, with L1 held enters mode2 | DPadLeft | Square | Square |
| L2 / Left Trigger | Outside menus: short release = Start; holds over 1.5s output nothing; chord with ABXY/R2 enters mode1 | Release L2 to return to mode0 | Short release = Start | Outside menus = Start; menus = L2 |
| L1 / Left Grip | Outside menus: short release = Select; holds over 1.5s output nothing; chord with ABXY/R2 enters mode2 | Short release = Select | Release L1 to return to mode0 | `UiRingLeft` / `TabLeft` / `Tab2Left` |
| R2 / Right Trigger | R2; enters mode1 with L2 held, or mode2 with L1 held | `DrinkWater` | `EyeMask` | Left mouse button + `LeftClick` |
| Right Grip | R1 | R1 | R1 | `UiRingRight` / `TabRight` / `Tab2Right` |
| Left Stick | Virtual left stick; maps to DPad in menu panels | Virtual left stick; maps to DPad in menu panels | Virtual left stick; maps to DPad in menu panels | Virtual left stick; drives child scrolling and Closet sliders in supported panels |
| Right Stick | Virtual right stick; Y may be suppressed in some HMD-driven view states | Virtual right stick | Virtual right stick | Virtual right stick; no mouse-wheel or `UIScrollA/B` special mapping |
| L3 / Left Stick Click | Press and release alone = recenter view; with R3 = toggle cursor mode | Same as mode0 | Same as mode0 | With R3 = exit cursor mode and return to mode0 |
| R3 / Right Stick Click | L1; with L3, only toggles cursor mode | L1 | L1 | L1; with L3, exits cursor mode |
| Left Menu | Read by the input source, but not currently mapped to output | Read but unmapped | Read but unmapped | Read but unmapped |

When POP UI is open, `ABXY` is overridden before normal mode mapping: `Y = DPadUp`, `X = DPadDown`, `A = Cross`, and `B = Circle`. When Circle UI / the ring menu is open, holding exactly one of `A` or `B` in `mode1` / `mode2` temporarily swaps the movement and camera sticks.

When a menu panel is open outside cursor mode, the mapper uses separate UI handling: `L1` / `L2` output the original `L1` / `L2`, do not trigger `Select` / `Start`, and do not enter `mode1` / `mode2`. The left stick no longer outputs a normal analog stick and is mapped to DPad directions instead. If the panel uses child `ScrollRect` scrolling, left-stick up/down only sends mouse-wheel scroll with a delta of `60`, without extra DPad up/down.

The current child-scroll panel set is `ClosetMenuView` and `MissionMenuPanelView`. In these panels, in both cursor and non-cursor mode, left-stick up/down searches the panel children for a usable `ScrollRect` and sends mouse-wheel events; the right stick is not used for scroll mapping.

The current child-slider panel set is only `ClosetMenuView`. In cursor mode with the closet open, left-stick left/right drives a child `Slider`: the mapper searches and locks a Slider only once when the left stick moves from neutral to horizontal input, then keeps using that Slider until the stick returns to neutral, the closet closes, cursor mode exits, or the Slider becomes unusable. If the locked Slider becomes unusable, the lock is cleared and no new Slider is searched until the stick returns to neutral. Slider step is `0.005`; repeat starts at `0.33s`, accelerates while held, and bottoms out at `0.06s`.

## Implementation Overview

VR runtime:

- `Plugin.cs` is the BepInEx entry point. It binds config, registers Harmony patches, and creates the runtime host.
- `VrRunnerHost.cs` converts BepInEx config into runtime settings.
- `Runtime/VrRuntimeManager.cs` owns OpenVR initialization, HMD pose updates, recentering, scene transition handling, mirror safeguards, and frame submission.
- `Runtime/VrCameraRig.cs` creates left/right eye cameras, render textures, and desktop mirror output.
- `OpenVR/OpenVRBridge.cs` calls OpenVR through the Valve C# binding and submits eye textures to the compositor.

UI and input:

- `Runtime/VrUiBridge.cs` captures supported screen-space canvases into VR-visible panels and handles fullscreen-effect overlays.
- `Runtime/NpcWorldSpaceUiFixer.cs` reprojections NPC marker UI for VR viewing.
- `InputMapping/Quest3InputSystem.cs` and related files translate Quest 3 controller state into game-facing virtual input.
- `InputMapping/Quest3OpenVrInputSource.cs` loads the SteamVR action manifest and reads button, stick, trigger, and pose actions.

Compatibility patches:

- `CameraRenderPatch.cs` blocks reflection or nested non-VR camera renders during VR rendering.
- `ReflectionReenablePatch.cs` prevents mirror/reflection objects from reviving blocked render paths while VR is active.
- `ReflectionBlocker.cs` classifies mirror, reflection, target-texture, and probe objects.
- `InputMouseAxisPatch.cs` suppresses legacy mouse look while HMD pose owns the view.
- `BlackCensorControllerPatch.cs` suppresses a repeated game-side null reference path.

Ring menu plugin:

- `SecretFlasherManakaRingMenuLongPress` is a separate BepInEx plugin.
- It patches `RingMenuParentView.GetLongDown` and routes the long-press count through `RingMenuLongPressCount`.

## Test Checklist

1. Start SteamVR and connect the headset PCVR stream.
2. Start the game from the PC.
3. Enter gameplay from the desktop game window.
4. Check `BepInEx/LogOutput.log` for:

```text
OpenVR initialized via Valve binding
VR runtime initialized.
OpenVR texture submit succeeded for both eyes.
```

5. In game, check view stability, recentering, UI panels, NPC markers, Quest input, and scene transitions.

## License And Distribution

This project is licensed under the MIT License. See `LICENSE`.

The bundled Valve OpenVR files are covered by Valve's BSD 3-Clause License. See `THIRD_PARTY_NOTICES.md`.

This project is an unofficial mod package. It does not distribute SecretFlasherManaka game files, BepInEx, Harmony, generated interop assemblies, or Unity runtime files. Players must provide their own game installation and runtime dependencies.

## Package Layout

```text
SecretFlasherManakaVR_Package/
├─ LICENSE
├─ THIRD_PARTY_NOTICES.md
├─ README.md
├─ README.zh-CN.md
├─ src/
│  ├─ SecretFlasherManakaVR/
│  └─ SecretFlasherManakaRingMenuLongPress/
├─ scripts/
│  ├─ build.ps1
│  └─ install.ps1
├─ dependencies/
│  └─ openvr_api.dll
├─ input/
│  ├─ actions.json
│  └─ bindings_oculus_touch.json
├─ config/
│  ├─ com.codex.secretflashermanaka.vr.cfg
│  └─ com.codex.secretflashermanaka.ringmenulongpress.cfg
└─ dist/
   ├─ BepInEx/
   │  ├─ plugins/
   │  │  ├─ SecretFlasherManakaVR.dll
   │  │  ├─ SecretFlasherManakaRingMenuLongPress.dll
   │  │  ├─ openvr_api.dll
   │  │  └─ SecretFlasherManakaVR_Input/
   │  └─ config/
   └─ debug_symbols/
```
