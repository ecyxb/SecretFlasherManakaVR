# SecretFlasherManakaVR 开发说明

这是一个独立的 BepInEx IL2CPP 插件，输出 PCVR 画面到 SteamVR/OpenVR compositor。它不重打包游戏本体，也不覆盖原有的 `SecretFlasherManakaMod.dll`。

## 当前基线

- 插件 DLL: `BepInEx\plugins\SecretFlasherManakaVR.dll`
- Native OpenVR: `BepInEx\plugins\openvr_api.dll`
- 配置文件: `BepInEx\config\com.codex.secretflashermanaka.vr.cfg`
- 目标玩法: PC 运行游戏，Quest 通过 Virtual Desktop / Steam Link / SteamVR 串流。
- 输入范围: 第一版只保留键鼠/手柄操作，HMD 只负责视角姿态。
- 当前镜子策略: 保留镜子模型，阻止镜子/RenderTexture 相机渲染反射。

当前已知可用的视觉配置：

```ini
RenderScale = 0.5
UseOpenVRProjection = true
OpenVRProjectionMode = RawSwapVertical
UseSourceProjectionForCulling = true
FlipSubmitV = true
AutoRecenterOnStart = true
SourceRotationMode = SourceYawOnly
```

镜子稳定性相关配置：

```ini
DisableReflectionCameras = true
DisableTargetTextureCameras = true
KeepReflectionCamerasDisabledWhileVrActive = true
PreventReflectionReenableWhileVrActive = true
BlockReflectionCameraRenderWhileVrActive = true
BlockNestedCameraRenderDuringVrRender = true
SceneTransitionVrPauseSeconds = 1.5
ReflectionCameraNameKeywords = mirror,reflect,reflection,planar,water
```

以前试过 `DisableReflectionRenderersWhileVrActive` 这类全场 renderer 扫描。它会造成镜子模型消失、误伤角色/材质，也可能带来场景切换抖动；当前代码已经移除此路径。后续不要把“扫描并禁用 Renderer”作为默认方案加回来。

## 代码结构

- `Plugin.cs`: BepInEx 入口，绑定配置，注册 Harmony patch，创建 runner。
- `VrRunnerHost.cs`: Unity `MonoBehaviour` runner，把 BepInEx 配置转换成 runtime settings。
- `Runtime\VrRuntimeManager.cs`: VR 生命周期、相机绑定、HMD pose、场景切换、镜子相机防护。
- `Runtime\VrCameraRig.cs`: 左右眼相机、RenderTexture、mirror 输出。
- `OpenVR\OpenVRBridge.cs`: 使用 Valve 官方 C# binding 调 OpenVR。
- `CameraRenderPatch.cs`: 拦截反射相机或 VR 渲染期间的嵌套 `Camera.Render()`。
- `ReflectionReenablePatch.cs`: 阻止脚本在 VR 活跃时重新启用反射/targetTexture 相机。
- `ReflectionBlocker.cs`: 判断某个相机是否属于镜子/反射/targetTexture 相机。
- `InputMouseAxisPatch.cs`: VR 活跃时把 legacy `Mouse X/Y` 置零，避免鼠标看向和 HMD 姿态打架。
- `BlackCensorControllerPatch.cs`: 限流并吞掉游戏 `BlackCensorController.OnChange` 的 NullReferenceException。

## 构建和安装

在 `VRModSrc` 目录运行：

```powershell
cd "E:\erogame\SecretFlasherManaka v1.1.3\VRModSrc"
.\build.ps1
.\install.ps1
```

从任意目录运行时显式传入游戏根目录：

```powershell
& "E:\erogame\SecretFlasherManaka v1.1.3\VRModSrc\build.ps1" -GameRoot "E:\erogame\SecretFlasherManaka v1.1.3"
& "E:\erogame\SecretFlasherManaka v1.1.3\VRModSrc\install.ps1" -GameRoot "E:\erogame\SecretFlasherManaka v1.1.3"
```

`install.ps1` 会复制：

- `SecretFlasherManakaVR.dll` 到 `BepInEx\plugins\`
- `openvr_api.dll` 到 `BepInEx\plugins\`

如果缺少 `openvr_api.dll`，可从 SteamVR 安装目录复制：

```text
...\Steam\steamapps\common\SteamVR\bin\win64\openvr_api.dll
```

## 测试流程

1. 先启动 SteamVR，并确认 Quest 已通过串流进入 PCVR 环境。
2. 启动 `SecretFlasherManaka.exe`。
3. 在电脑窗口进入实际游戏场景。主菜单里的 Screen Space Overlay UI 不保证进入 VR 双眼画面。
4. 查看 `BepInEx\LogOutput.log`，确认出现：

```text
OpenVR initialized via Valve binding
VR runtime initialized.
OpenVR texture submit succeeded for both eyes.
```

5. 在第一个场景重点测试镜子区域和场景切换。正常稳定路径下应看到类似日志：

```text
Persistently disabling reflection camera while VR is active
Blocked reflection Camera.Render while VR is active
Pausing VR eye rendering during scene transition
```

## 已知限制

- UI 暂时主要靠电脑窗口操作，VR 内优先保证 3D 世界画面。
- 镜子反射被禁用，只保留镜子模型本身。
- VR 控制器暂不映射游戏操作。
- 场景切换到第一个场景仍可能有压力峰值，所以当前保留 `SceneTransitionVrPauseSeconds = 1.5`。
- ReShade 当前测试中保持禁用，文件名为 `dxgi.dll.disabled-by-codex-vrtest`。

## 后续开发建议

优先方向：

1. 稳定第一个场景的切换和镜子脚本交互。
2. 降低场景切换暂停时间，观察是否还会掉帧或卡死。
3. 做一个更温和的相机候选过滤，而不是禁用所有 targetTexture camera。
4. 再考虑 VR 内 UI 或手柄射线。

调试时尽量只改一个变量：

- 画面倒置: 看 `FlipSubmitV`。
- 左右眼无法融合: 看 `UseOpenVRProjection` 和 `OpenVRProjectionMode`。
- 场景消失: 看 `UseSourceProjectionForCulling`。
- 高度不对: 看 `AutoRecenterOnStart` 和 `CameraHeightOffset`。
- 鼠标导致异常: 看 `SuppressMouseLookInput` 和 `SourceRotationMode`。
- 镜子导致卡死: 看反射相机相关日志，不要先恢复 renderer 扫描。
