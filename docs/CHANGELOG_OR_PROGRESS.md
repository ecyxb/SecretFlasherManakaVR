# SecretFlasherManaka VR Mod Progress

Workspace: `E:\erogame\SecretFlasherManaka v1.1.3`
Date: 2026-05-24
Coordinator: parent Codex agent

## Goal

Build a separate BepInEx IL2CPP plugin named `SecretFlasherManakaVR.dll` that attempts native PCVR output through SteamVR/OpenVR without repackaging the game. First version supports HMD pose plus keyboard/mouse/gamepad input. No VR controller interaction in v1.

## Known Facts

- Game: Unity 2022.3.62f2, IL2CPP, Windows x64.
- Mod loader: BepInEx 6 IL2CPP already installed through Doorstop.
- Existing plugin: `BepInEx\plugins\SecretFlasherManakaMod.dll`; do not overwrite it.
- Graphics: D3D11 swapchain observed in `ReShade.log`.
- Existing ReShade/Depth3D is present but should not be required for native VR.
- Relevant game types found in `BepInEx\interop\Assembly-CSharp.dll`:
  - `ExposureUnnoticed2.Object3D.Camera.BaseCameraController`
  - `ExposureUnnoticed2.Object3D.Camera.FPCameraController`
  - `ExposureUnnoticed2.Object3D.Camera.TPCameraController`
  - `ExposureUnnoticed2.Object3D.Player.Scripts.PlayerController`
  - `ExposureUnnoticed2.Scripts.InGame.InGameManager`

## Non-Negotiables

- Do not modify or repackage original game data.
- Do not overwrite `SecretFlasherManakaMod.dll`.
- Keep changes in a new plugin/source folder and copied build artifacts only.
- If SteamVR/OpenVR is missing, fail soft and leave the normal game playable.
- Multiple agents may work in parallel; do not revert or overwrite another agent's edits.

## Step Breakdown

1. `A-interop`: Define OpenVR/OpenVRBridge surface and dependency approach.
2. `B-plugin-shell`: Create BepInEx IL2CPP plugin project shell, config binding, logging, Harmony bootstrap, and runner lifecycle.
3. `C-vr-runtime`: Implement Unity-side VR runtime manager: camera discovery, stereo camera rig, render textures, pose/recenter flow, mirror handling.
4. `D-build-install`: Add deterministic build/install scripts and minimal documentation for testing with SteamVR/Quest streaming.
5. `E-integration`: Coordinator reviews agent results, resolves seams, builds plugin, installs it under `BepInEx\plugins`, and runs non-headset fallback tests.

## Dependency Order

- Phase 1 can run in parallel:
  - Agent A drafts the OpenVR bridge API and dependency notes.
  - Agent B drafts the plugin shell and config surface.
  - Agent D drafts build/install/docs around the expected project layout.
- Phase 2 depends on Phase 1 interfaces:
  - Agent C runtime should compile against Agent A's bridge API and Agent B's config/runner expectations.
- Phase 3 is coordinator-only integration:
  - Reconcile APIs, fix compile errors, build Release, install to `BepInEx\plugins\SecretFlasherManakaVR\`, then run no-headset fallback validation.

## Agent Assignments

- Agent A / Fermat / `019e5754-4a7a-78e0-8e56-8019d05c4c96`: owns OpenVR bridge files only.
- Agent B / Bohr / `019e5754-664c-76e0-a6c6-1e8e0d3731f5`: owns plugin shell/config files only.
- Agent C / Cicero / `019e5754-8264-7753-be55-663d92ca737b`: owns VR runtime/camera files only.
- Agent D / Confucius / `019e5754-9e4d-7930-995e-c5d6b7434d18`: owns build/install/docs files only.

## Status Log

- 2026-05-24: Progress file created. Agents not yet spawned.
- 2026-05-24: Spawned Agent A/B/C/D with disjoint write scopes. Coordinator will integrate results after they report back.
- 2026-05-24: Agent D / Confucius completed build/install/docs files: `VRModSrc\build.ps1`, `VRModSrc\install.ps1`, `VRModSrc\README_VR.md`.
- 2026-05-24: Agent B / Bohr completed plugin shell/config files and reported a temporary build pass.
- 2026-05-24: Agent C / Cicero completed runtime/camera rig files and reported a temporary compile pass.
- 2026-05-24: Agent A / Fermat completed OpenVR bridge files and reported bridge/full-source temporary compile pass.
- 2026-05-24: Coordinator integration is next: inspect seams, update runner/runtime wiring if needed, build Release, install, then run no-headset fallback validation.
- 2026-05-24: Coordinator wired `VrRunnerHost` to create `OpenVRBridge`, map `ModConfig` to `VrRuntimeSettings`, and pass a BepInEx logger adapter to runtime.
- 2026-05-24: Fixed `build.ps1` / `.csproj` path handling for game roots with spaces and trailing backslashes.
- 2026-05-24: Build succeeded: `VRModSrc\SecretFlasherManakaVR\bin\Release\SecretFlasherManakaVR.dll`.
- 2026-05-24: Fixed install location to `BepInEx\plugins\SecretFlasherManakaVR.dll`; this BepInEx build did not load the nested plugin directory.
- 2026-05-24: Installed plugin with `-AllowMissingOpenVR` for fallback validation because no local `openvr_api.dll` was found.
- 2026-05-24: Short launch validation passed: BepInEx loaded 2 plugins, `SecretFlasherManaka VR` soft-failed on missing `openvr_api.dll`, and the normal game continued.
- 2026-05-24: Found official SteamVR x64 `openvr_api.dll` at `D:\software\steam\steamapps\common\SteamVR\bin\win64\openvr_api.dll`, copied it to `VRModSrc\Dependencies\openvr_api.dll` and `BepInEx\plugins\openvr_api.dll`, then reran install successfully.
- 2026-05-24: User reported Quest/SteamVR showed only a normal big screen. Log showed `OpenVR init failed: Hmd Not Found (108)`, meaning SteamVR started but did not expose a PCVR HMD before plugin initialization.
- 2026-05-24: Added OpenVR initialization retry in `VrRuntimeManager`: after HMD-not-ready failures it retries every 5 seconds instead of disabling VR permanently. Rebuilt and reinstalled successfully.
- 2026-05-24: User reported Quest detects the game but SteamVR stays at preparing `SecretFlasherManaka`. Log shows OpenVR init, camera rig, and render textures succeed, but `OpenVR texture submit failed` repeats.
- 2026-05-24: Added submit failure detail logging via `OpenVRBridge.LastError` and ensured RenderTextures are created before each eye render. Rebuilt and reinstalled successfully. Next test should capture the new `LastError` text.
- 2026-05-24: New log showed `OpenVR Submit(Right) failed: AlreadySubmitted`, indicating SteamVR thinks an eye was submitted twice before the next frame. Added `BeginFrame()` / `ClearLastSubmittedFrame()` before eye submission. Rebuilt and reinstalled successfully.
- 2026-05-24: `AlreadySubmitted` persisted. Identified likely root cause: `IVRCompositorFnTable` layout was wrong, with extra `GetSubmitTexture` / `SubmitWithArrayIndex` slots before `ClearLastSubmittedFrame`. Removed those slots so `Submit`, `ClearLastSubmittedFrame`, and `PostPresentHandoff` align with Valve `openvr_capi.h`. Rebuilt and reinstalled successfully.
- 2026-05-24: User reported the corrected fn-table build caused a game crash. Reverted `IVRCompositorFnTable` to the previous non-crashing layout and rebuilt/reinstalled the safe build. Next implementation should avoid hand-maintained function tables and use Valve's official `openvr_api.cs` binding instead.
- 2026-05-24: Downloaded Valve official `openvr_api.cs` C# binding and added it under `VRModSrc\SecretFlasherManakaVR\OpenVR\Valve\openvr_api.cs`.
- 2026-05-24: Replaced `OpenVRBridge` internals with Valve official `Valve.VR.OpenVR` / `CVRSystem` / `CVRCompositor` APIs while preserving the local bridge interface used by the runtime.
- 2026-05-24: Changed `BeginFrame()` to a no-op because `WaitGetPoses` is now the frame boundary; stopped calling `ClearLastSubmittedFrame` before submit. Added per-frame duplicate-eye submit guards.
- 2026-05-24: Build and install succeeded with the Valve binding build. Installed `BepInEx\plugins\SecretFlasherManakaVR.dll` and `BepInEx\plugins\openvr_api.dll`.
- 2026-05-24: Rewired runtime to call `IOpenVRBridge` directly instead of reflective dispatch. This removed the latest SteamVR `submitted two left/right scene textures` warnings, so the duplicate-submit path is no longer the active blocker.
- 2026-05-24: Latest short run reached OpenVR initialization and stereo texture submission, but both eyes failed with `TextureUsesUnsupportedFormat`. Next fix changes eye RenderTextures from `ARGB32` to `BGRA32` for D3D11/OpenVR compatibility.
- 2026-05-24: `BGRA32` and explicit descriptor with `B8G8R8A8_SRGB` still produced `TextureUsesUnsupportedFormat`. Temporarily disabling ReShade by moving `dxgi.dll` to `dxgi.dll.disabled-by-codex-vrtest` did not change the error, so ReShade is not the current blocker. Next attempt forces `B8G8R8A8_UNorm`.
- 2026-05-24: Added D3D11 texture diagnostics. Unity eye RenderTextures were `dxgiFormat=90` with `misc=0x0`, confirming they were not shared resources.
- 2026-05-24: Added D3D11 shared submit textures and per-frame `CopyResource` from Unity eye RenderTextures. Shared textures are created with `misc=0x2`, but `B8G8R8A8_UNorm` still hit `TextureUsesUnsupportedFormat`. Next attempt switches color format to `R8G8B8A8_UNorm` / DXGI format 28.
- 2026-05-24: `R8G8B8A8_UNorm` still produced a typeless source texture (`dxgiFormat=27`), which OpenVR rejects. Shared texture creation now converts typeless formats to typed submit formats (`27 -> 28`, `90 -> 87`) before `CreateTexture2D`.
- 2026-05-24: `DXGISharedHandle` submit path also returned `TextureUsesUnsupportedFormat`; OpenVR scene texture submit likely wants the `ID3D11Texture2D*`, while shared handles are mainly useful for overlays. Next attempt submits the shared texture pointer as DirectX again, but converts source `27 -> 29` (`R8G8B8A8_UNORM_SRGB`).
- 2026-05-24: Changed OpenVR submit color space to `Auto` and converted typeless R8G8B8A8 source textures to `R8G8B8A8_UNORM` (`27 -> 28`). Short run PID 32452 initialized OpenVR and created shared submit textures without any new `TextureUsesUnsupportedFormat` / submit-failed log entries.
- 2026-05-24: Added a one-time success log for both-eye compositor submit. Short run PID 25948 logged `OpenVR texture submit succeeded for both eyes.` Build/install succeeded and `BepInEx\plugins\SecretFlasherManakaVR.dll` is updated.
- 2026-05-24: User reported headset still stuck and game can hang/crash. Windows logs show `AppHangB1` plus NVIDIA `nvwgf2umx.dll` access violations; SteamVR compositor shows many `Timed out` presents even after submit succeeds. Next mitigation removes explicit compositor timing / `PostPresentHandoff`, removes submit throttling, and exposes `RenderScale` with a safer 0.75 default.
- 2026-05-24: Mitigation build still crashed in `nvwgf2umx.dll` after successful submit, so the likely trigger is the D3D11 shared-resource submit path. Next attempt changes the typed copy target from `MISC_SHARED` to a private same-device typed texture (`misc=0`) and continues submitting it as `ETextureType.DirectX`.
- 2026-05-24: Private typed D3D11 copy target still crashed in the same NVIDIA driver path. Next attempt removes the custom D3D11 copy/submit texture path entirely and creates Unity-owned `R16G16B16A16_SFloat` eye RenderTextures for direct OpenVR submit.
- 2026-05-24: Unity-owned `R16G16B16A16_SFloat` direct submit avoided a new Windows/NVIDIA crash in short run PID 7660, but SteamVR dropped all frames and reported very high frame time. Lowered `RenderScale` from 0.75 to 0.5 for the next stability test.
- 2026-05-24: Short run PID 29560 at `RenderScale=0.5` still avoided new Windows/NVIDIA crash and logged submit success, but SteamVR dropped all frames. Added `GL.Flush()` after stereo camera renders before OpenVR submit to force queued eye rendering work toward the driver before compositor consumption.
- 2026-05-24: Rebuilt and installed the `GL.Flush()` mitigation build to `BepInEx\plugins\SecretFlasherManakaVR.dll`. Next user headset test should check whether SteamVR leaves the preparing/loading state; if not, inspect compositor stats for PID after the run.
- 2026-05-24: User reported no crash/no hang but the Quest view was solid blue. Added `UseOpenVRProjection` config defaulting to `false`, so VR eyes now use the source game camera projection instead of OpenVR projection matrices while stabilizing rendering. Added source camera and candidate camera diagnostics to identify blue-only/UI-only camera binding. Build and install succeeded.
- 2026-05-24: Short run PID 21280 logged source camera `Main Camera` with `clearFlags=SolidColor`, blue background `RGBA(0.192, 0.302, 0.475, 0.000)`, only one camera candidate, and successful OpenVR submit. This suggests a title/menu Screen Space Overlay phase can look blue in headset because overlay UI is not included in the eye cameras; enter gameplay via the normal desktop window for the next headset test.
- 2026-05-24: User confirmed headset remains blue after entering gameplay. Added a Harmony postfix for `ExposureUnnoticed2.Scripts.InGame.InGameManager.OnLateUpdate` so VR frames render after the game's own late camera update instead of from the runner's ordinary `LateUpdate`. Added `LateTickFromGame` frame guarding, BaseCameraController camera preference, dynamic active-camera diagnostics, and changed stereo eye camera state copying to `Camera.CopyFrom(source)` while preserving target textures.
- 2026-05-24: Rebuilt and installed the in-game-late-update build. Short startup PID 28204 still initializes and submits successfully; the next real gameplay headset test should check whether this fixes the blue view and whether logs contain `VR active camera update` once the in-game manager is active.
- 2026-05-24: User clarified the blue view was caused by testing only in the main menu; gameplay has an image. Rolled back the blue-screen investigation changes: removed `UseOpenVRProjection`, source camera/candidate diagnostics, `InGameManager.OnLateUpdate` Harmony postfix, `LateTickFromGame`, BaseCameraController preference, frame guard, and `Camera.CopyFrom(source)`. Kept stability changes: Valve binding, direct Unity `R16G16B16A16_SFloat` submit textures, `RenderScale=0.5`, `GL.Flush()`, and the custom D3D11 copy path disabled. Build/install succeeded.
- 2026-05-24: User reported gameplay VR image is upside down in third person, and the rollback build only renders the player over black while the pre-rollback build rendered the scene. Added two targeted fixes without restoring the blue-screen diagnostics/hooks: `FlipSubmitV=true` flips OpenVR texture bounds vertically, and `UseOpenVRProjection=false` defaults eye cameras to the source game camera projection to avoid scene culling/black background from the OpenVR projection matrix. Build/install succeeded and short startup logged source-camera projection plus successful submit.
- 2026-05-24: User reported the source-camera projection build restores the scene but left/right eyes do not fuse, like an SBS image. Changed the default stereo path back to `UseOpenVRProjection=true` for correct per-eye asymmetric VR projection, and added `UseSourceProjectionForCulling=true` so Unity culls scene objects using the game camera projection while rendering with OpenVR projection. Kept `FlipSubmitV=true`. Build/install succeeded; short startup initialized OpenVR and submitted both eyes successfully.
- 2026-05-24: User reported the scene disappeared again with `UseOpenVRProjection=true` plus source-projection culling. Changed OpenVR projection acquisition from Valve's direct compositor matrix to `GetProjectionRaw` and rebuilt it with Unity `Matrix4x4.Frustum`, keeping `FlipSubmitV=true` and `UseSourceProjectionForCulling=true`. Build/install succeeded; short startup PID 21396 logged `OpenVR raw projection converted for Unity` and `OpenVR texture submit succeeded for both eyes`.
- 2026-05-24: Added `OpenVRProjectionMode` config with `SourceCamera`, `ValveMatrix`, `RawSwapVertical`, `RawNoSwap`, and `RawInvertVertical` so projection variants can be tested from config without rebuilding. Default remains `RawSwapVertical`. Build/install succeeded; short startup PID 23152 generated the config entry and logged `Using OpenVR projection mode: RawSwapVertical`.
- 2026-05-24: User reported the VR view is now correct for scene/upright/stereo, but both first- and third-person cameras are about two heads too high. Added `AutoRecenterOnStart=true` and auto-recenter on the first valid HMD pose so SteamVR standing-space HMD height is subtracted before adding pose to the game camera. Build/install succeeded; short startup PID 27660 logged `VR view auto-recentered at first valid HMD pose`.
- 2026-05-24: User reported a possible freeze when moving the mouse while keyboard input is usually fine. Added `SuppressMouseLookInput=true`, Harmony postfixes for legacy `Input.GetAxis/GetAxisRaw("Mouse X/Y")` that zero mouse-look axes only while VR is active, and `SourceRotationMode=SourceYawOnly` so the VR rig follows only source camera yaw by default instead of full mouse-driven pitch/roll. Build/install succeeded; short startup PID 5580 loaded Harmony patches, generated the config entries, initialized OpenVR, and submitted both eyes.
- 2026-05-24: User reported repeated `BlackCensorController.OnChange` null-reference exceptions from the game's delayed option-change scheduler. Added `SuppressBlackCensorOnChangeNullRefs=true` and a Harmony finalizer for `ExposureUnnoticed2.Object3D.Player.Scripts.Other.BlackCensorController.OnChange(OptionChangeEvent)` to suppress plain and IL2CPP-wrapped null-reference exceptions with rate-limited logging. Build succeeded and install succeeded after clearing a stale game process that held `SecretFlasherManakaVR.dll`.
- 2026-05-24: User suspected the first-scene mirror/reflection may be causing freezes through recursive or re-entrant rendering. Added `DisableReflectionCameras=true` and `ReflectionCameraNameKeywords=mirror,reflect,reflection,planar,water`. During each VR eye render frame, the runtime now temporarily disables enabled non-source/non-VR cameras whose camera name or target texture name matches those keywords, then restores them immediately after `rig.Render()`. Build and install succeeded; runtime validation is still pending.
- 2026-05-24: User suspected the previous reflection-camera disable did not actually catch the mirror. Latest log confirmed no `reflection-camera-disabled` line appeared. Broadened the experiment with `DisableTargetTextureCameras=true` so all non-source/non-VR cameras rendering to a `RenderTexture` are temporarily disabled during VR eye rendering, even if their names do not contain mirror keywords. Added `LogReflectionCameraDiagnostics=true` to log a one-shot camera/candidate list per scene. Build and install succeeded.
- 2026-05-24: Latest test showed the broadened camera scan did run, but only found `FaceCamera` and `BodyCamera` as RenderTexture candidates; no explicit mirror camera appeared before VR eye rendering. Windows logs still showed AppHang plus an NVIDIA `nvwgf2umx.dll` crash. Added `BlockNestedCameraRenderDuringVrRender=true` and a Harmony patch for `UnityEngine.Camera.Render()` that skips manual renders from non-VR-eye cameras only while the plugin is rendering the left/right VR eyes. This targets reflection scripts that create or invoke their camera inside render callbacks after the pre-scan. Build and install succeeded.
- 2026-05-24: User reported the mirror is stable while white/unrendered but freezes when the mirror starts rendering. Logs showed repeated temporary disables of `FaceCamera` and no `Blocked nested Camera.Render...` hit, meaning the old per-VR-frame disable still allowed target-texture cameras to resume between frames. Added `KeepReflectionCamerasDisabledWhileVrActive=true`, which persistently disables reflection/target-texture cameras for the active VR scene and restores them on scene change/shutdown. Build and install succeeded.
- 2026-05-24: User reported hangs still occur even after `FaceCamera` and `BodyCamera` are persistently disabled. Latest logs confirmed both target-texture cameras are disabled and no nested `Camera.Render` blocker fires. Added `DisableReflectionRenderers=true`: while VR is active the runtime scans scene renderers every 0.5 seconds and persistently disables renderers whose object/material/shader/texture names match reflection keywords or whose material references a `RenderTexture`. This targets mirror surfaces or reflection shaders directly. Build and install succeeded.
- 2026-05-24: User asked whether the current changes can stop scripts that check mirror visibility every frame and then re-enable/render the mirror. Answer: prior builds could not guarantee that, because a script could re-enable the camera/renderer after the VR scan. Added `PreventReflectionReenableWhileVrActive=true` and `BlockReflectionCameraRenderWhileVrActive=true`. New Harmony patches block `Behaviour.set_enabled(true)` for reflection/target-texture cameras, block `Renderer.set_enabled(true)` for reflection renderers, and block manual `Camera.Render()` from reflection/target-texture cameras while VR is active. Also narrowed renderer detection so a plain `RenderTexture` material no longer disables `Body` unless the names look reflective. Build and install succeeded.
- 2026-05-24: User reported mirror models disappearing is acceptable and in-scene hangs are rarer, but switching from other scenes back to the first scene can still hang. Logs confirmed the mirror script is actively blocked (`Mirror camera for ...` hundreds of times), so the remaining issue is likely a scene-transition timing window before LateUpdate. Moved scene-change handling and reflection sanitization into `Tick()`/Update so reflection cameras/renderers are disabled before the VR eye render path and sooner after scene activation. Added `ReflectionRendererExcludeKeywords=body,face,player,manaka,hair,cloth,skirt` to stop player/character renderers from being hidden by broad reflective material/shader names. Build and install succeeded.
- 2026-05-24: User tested three scene transitions: one crash, one severe frame drop, one successful. Logs show `Blocked reflection Camera.Render...` repeatedly and Windows recorded both AppHang and an NVIDIA `nvwgf2umx.dll` crash around scene transition. Added `SceneTransitionVrPauseSeconds=1.5`: after a scene change the runtime pauses VR eye rendering/submission briefly while continuing high-frequency reflection cleanup (`0.05s` scan interval during the pause). This trades a short frozen headset frame during transitions for less GPU/main-thread pressure while the scene initializes. Build and install succeeded.
- 2026-05-24: User asked to disable `DisableReflectionRenderersWhileVrActive` to test whether the periodic full-scene renderer scan is causing stutter/hangs. Changed installed config `BepInEx/config/com.codex.secretflashermanaka.vr.cfg` to `DisableReflectionRenderers=false`. No rebuild was needed. Reflection camera disabling, reflection camera render blocking, and reflection camera/renderer re-enable blocking remain enabled.
- 2026-05-24: Cleaned the current stable baseline. Removed the reflection-renderer full-scene scan path, the renderer re-enable Harmony patch, `DisableReflectionRenderers`, and `ReflectionRendererExcludeKeywords` from source/config mapping. Current mirror mitigation is camera-only: persistently disable reflection/targetTexture cameras, block reflection `Camera.Render`, block reflection camera re-enable, and pause VR eye rendering briefly after scene changes.
- 2026-05-24: Replaced the garbled `VRModSrc\README_VR.md` with a clean current-baseline development document covering build/install, known-good stereo config, mirror strategy, known limitations, and next development directions.
- 2026-05-24: Build and install succeeded after cleanup. Installed `BepInEx\plugins\SecretFlasherManakaVR.dll`; `openvr_api.dll` was unchanged. Removed stale renderer-scan entries from `BepInEx\config\com.codex.secretflashermanaka.vr.cfg`.
- 2026-05-24: Created `SecretFlasherManakaVR_Package\` as a self-contained handoff/development package. It includes cleaned source under `src\`, package-adapted build/install scripts, `dependencies\openvr_api.dll`, current config, docs/progress notes, third-party OpenVR note, and `dist\BepInEx\` with the ready-to-copy plugin/config layout. Game files, BepInEx runtime files, interop assemblies, ReShade files, and `SecretFlasherManakaMod.dll` are intentionally excluded.

## Current Validation Notes

- `openvr_api.dll` is now installed from the local SteamVR install. Real SteamVR testing can proceed.
- BepInEx logs Il2CppInterop warnings for unsupported method signatures on `VrRunnerHost`; the type still registers and the plugin starts. These are noisy but not currently blocking.
- Current build uses Valve official binding instead of the hand-maintained fn-table bridge. Next test should check whether logs show `OpenVR initialized via Valve binding` and whether `OpenVR Submit(Right) failed: AlreadySubmitted` disappears.
- `AlreadySubmitted` is no longer the latest observed error after direct bridge calls. Current validation target is whether `BGRA32` eye RenderTextures remove `TextureUsesUnsupportedFormat`.
- ReShade proxy is currently disabled for testing as `dxgi.dll.disabled-by-codex-vrtest`; restore by renaming it back to `dxgi.dll` if needed.
- Real headset visual/pose behavior is the next validation step. Start/connect Quest PCVR first or leave game running until logs show `VR runtime initialized` and `OpenVR texture submit succeeded for both eyes`.
- Current config keeps `MirrorMode = MainCamera` so the desktop window remains usable for menus. Current stereo experiment defaults: `UseOpenVRProjection=true`, `UseSourceProjectionForCulling=true`, `FlipSubmitV=true`, `OpenVRProjectionMode=RawSwapVertical`.
- Current config also has `AutoRecenterOnStart=true`; if the view still feels slightly high/low after this fix, tune `CameraHeightOffset` in small increments such as `-0.2` or `0.2`.
- Mouse-freeze mitigation config defaults: `SuppressMouseLookInput=true`, `SourceRotationMode=SourceYawOnly`. If mouse movement still freezes the game, try `SourceRotationMode=RecenterYaw` next, then `HmdOnly`; if menus need mouse-look axes for debugging, set `SuppressMouseLookInput=false`.
- Current compatibility mitigation defaults: `SuppressBlackCensorOnChangeNullRefs=true`. If the same issue is fixed, logs should show at most rate-limited `Suppressed BlackCensorController.OnChange NullReferenceException` warnings instead of repeated full IL2CPP exception stacks.
- Current installed config: `DisableReflectionCameras=true`, `DisableTargetTextureCameras=true`, `KeepReflectionCamerasDisabledWhileVrActive=true`, `PreventReflectionReenableWhileVrActive=true`, `BlockReflectionCameraRenderWhileVrActive=true`, `BlockNestedCameraRenderDuringVrRender=true`, `SceneTransitionVrPauseSeconds=1.5`, `LogReflectionCameraDiagnostics=true`, `ReflectionCameraNameKeywords=mirror,reflect,reflection,planar,water`. Reflection renderer scanning has been removed from the codebase. During transitions it should still log `Pausing VR eye rendering during scene transition...`; if a reflection camera manually renders, it should log `Blocked reflection Camera.Render while VR is active: ...`.
- If `RawSwapVertical` still shows black/missing scene in headset gameplay, edit `BepInEx\config\com.codex.secretflashermanaka.vr.cfg` and try `OpenVRProjectionMode = RawInvertVertical` next, then `ValveMatrix`. `SourceCamera` is the known fallback that restores scene but causes SBS-like stereo.
