# SecretFlasherManaka VR Package

语言：[English](README.md) | 简体中文

这个仓库打包了两个独立的 SecretFlasherManaka BepInEx IL2CPP 插件：

- `SecretFlasherManakaVR.dll`：把游戏画面输出到 SteamVR/OpenVR，用于 PCVR。
- `SecretFlasherManakaRingMenuLongPress.dll`：把环形菜单长按阈值做成可配置项。

这个包不包含游戏本体、BepInEx，也不会覆盖 `SecretFlasherManakaMod.dll`。它面向已经安装好 BepInEx IL2CPP 的游戏目录。

## 功能说明

- 使用注入的左右眼相机输出 SteamVR/OpenVR 立体画面。
- 桌面窗口可显示源相机、左眼、右眼，或关闭镜像输出。
- 通过 SteamVR Input 支持 Quest 3 手柄映射，并保留 OpenVR legacy controller state 兜底。
- 支持 HMD 驱动相机姿态、玩家头/颈/胸骨骼姿态，以及移动时由 HMD 朝向带动身体转向。
- 支持把部分屏幕空间 UI 捕获到 VR 面板，包括 HUD 位置、全屏特效覆盖层和 NPC 头顶标记重投影。
- 保留镜子模型，但阻止镜子/反射相机递归渲染，降低卡死和闪烁风险。
- 环形菜单长按阈值作为单独 BepInEx 插件提供。

## 安装方式

把 `dist/BepInEx` 下的文件复制或合并到游戏根目录：

```text
dist/BepInEx/plugins/SecretFlasherManakaVR.dll
dist/BepInEx/plugins/SecretFlasherManakaRingMenuLongPress.dll
dist/BepInEx/plugins/openvr_api.dll
dist/BepInEx/plugins/SecretFlasherManakaVR_Input/actions.json
dist/BepInEx/plugins/SecretFlasherManakaVR_Input/bindings_oculus_touch.json
dist/BepInEx/config/com.codex.secretflashermanaka.vr.cfg
dist/BepInEx/config/com.codex.secretflashermanaka.ringmenulongpress.cfg
```

最终位置应该是：

```text
<游戏根目录>/BepInEx/plugins/SecretFlasherManakaVR.dll
<游戏根目录>/BepInEx/plugins/SecretFlasherManakaRingMenuLongPress.dll
<游戏根目录>/BepInEx/plugins/openvr_api.dll
<游戏根目录>/BepInEx/plugins/SecretFlasherManakaVR_Input/actions.json
<游戏根目录>/BepInEx/plugins/SecretFlasherManakaVR_Input/bindings_oculus_touch.json
<游戏根目录>/BepInEx/config/com.codex.secretflashermanaka.vr.cfg
<游戏根目录>/BepInEx/config/com.codex.secretflashermanaka.ringmenulongpress.cfg
```

不要删除或覆盖 `SecretFlasherManakaMod.dll`。

## 依赖

运行依赖：

- SecretFlasherManaka v1.1.3。
- 游戏目录已经安装 BepInEx 6 IL2CPP。
- `BepInEx/interop` 下已经生成 interop assemblies。如果没有，先带 BepInEx 启动一次游戏。
- SteamVR/OpenVR，用于 PCVR 输出。
- `openvr_api.dll` 需要和 `SecretFlasherManakaVR.dll` 放在同一插件目录。

构建依赖：

- 带 `dotnet` 的 .NET SDK。
- 构建脚本需要传入游戏根目录，用于引用 `BepInEx/core` 和 `BepInEx/interop`。

## 从源码构建

在包根目录运行：

```powershell
scripts\build.ps1 -GameRoot "<游戏根目录>" -Configuration Release
scripts\install.ps1 -GameRoot "<游戏根目录>" -Configuration Release
```

默认情况下，`build.ps1` 会构建两个项目：

- `src/SecretFlasherManakaVR/SecretFlasherManakaVR.csproj`
- `src/SecretFlasherManakaRingMenuLongPress/SecretFlasherManakaRingMenuLongPress.csproj`

两个插件仍然会输出为两个独立 DLL。`install.ps1` 会复制两个插件 DLL、VR 插件的 OpenVR 依赖，以及 SteamVR Input JSON 文件。

## 配置

安装后的配置文件位于 `BepInEx/config`。

### VR 配置

文件：

```text
com.codex.secretflashermanaka.vr.cfg
```

公开配置项刻意保持较少。投影、反射、SteamVR action 细节等容易相互影响的高级参数已经固定在代码里，普通用户不需要维护一大组脆弱配置。

核心：

```ini
EnableVR = true
AutoStartSteamVR = true
MirrorMode = MainCamera
```

立体渲染：

```ini
CameraHeightOffset = 0
RenderScale = 0.5
```

输入：

```ini
RecenteringKey = F12
EnableQuest3InputMapping = true
Quest3RightStickDeadzone = 0.35
```

身体与视角：

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
PlayerHeadPoseChestWeight = 0.15
PlayerHeadPoseNeckWeight = 0.3
PlayerHeadPoseHeadWeight = 0.55
PlayerHeadPoseSmoothFactor = 18
```

VR UI 面板：

```ini
EnableVrUiBridge = true
ConvertOverlayCanvasToWorldSpace = true
VrUiFollowMode = HeadLocked
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

NPC 标记 UI：

```ini
FixNpcWorldSpaceUi = true
NpcWorldSpaceUiVerticalOffset = 0.25
NpcWorldSpaceUiScale = 0.0015
NpcWorldSpaceUiMinScaleDistance = 3
NpcWorldSpaceUiMaxScaleDistance = 7
```

### 环形菜单长按配置

文件：

```text
com.codex.secretflashermanaka.ringmenulongpress.cfg
```

配置项：

```ini
EnablePatch = true
RingMenuLongPressCount = 20
```

原版环形菜单长按计数更短。提高这个值可以降低误开环形菜单的概率。

## JSON 配置和 VR 输入系统

Quest 3 输入路径依赖安装在这里的 SteamVR Input JSON 资源：

```text
BepInEx/plugins/SecretFlasherManakaVR_Input/
```

文件：

- `actions.json`：声明 `/actions/quest3` action set，以及按钮、扳机、摇杆、手柄姿态等 action。
- `bindings_oculus_touch.json`：为 Oculus Touch / Quest 手柄提供默认绑定。

当 `EnableQuest3InputMapping = true` 时，VR 插件会调用 SteamVR 的 `SetActionManifestPath`，传入 `SecretFlasherManakaVR_Input/actions.json`。这个 manifest 再引用 `bindings_oculus_touch.json`。

如果缺少 `actions.json`，SteamVR action input 无法初始化。如果缺少 `bindings_oculus_touch.json`，manifest 可能能加载，但 Quest/Oculus Touch 的默认绑定不可用。插件有 legacy OpenVR controller state 兜底，但它表达能力更弱，不应该作为主要输入路径。

## 功能的大致实现方式

VR 运行时：

- `Plugin.cs` 是 BepInEx 入口，负责绑定配置、注册 Harmony patch、创建 runtime host。
- `VrRunnerHost.cs` 把 BepInEx 配置转换成 runtime settings。
- `Runtime/VrRuntimeManager.cs` 管理 OpenVR 初始化、HMD 姿态、重置视角、场景切换、镜子防护和帧提交。
- `Runtime/VrCameraRig.cs` 创建左右眼相机、RenderTexture 和桌面镜像输出。
- `OpenVR/OpenVRBridge.cs` 通过 Valve C# binding 调用 OpenVR，并把左右眼纹理提交给 compositor。

UI 和输入：

- `Runtime/VrUiBridge.cs` 把支持的屏幕空间 Canvas 捕获到 VR 面板，并处理全屏特效覆盖层。
- `Runtime/NpcWorldSpaceUiFixer.cs` 把 NPC 头顶标记重投影到 VR 视角下更合理的位置。
- `InputMapping/Quest3InputSystem.cs` 及相关文件把 Quest 3 手柄状态转换成游戏输入。
- `InputMapping/Quest3OpenVrInputSource.cs` 加载 SteamVR action manifest，并读取按钮、摇杆、扳机和手柄姿态 action。

兼容性 patch：

- `CameraRenderPatch.cs` 阻止反射相机或 VR 渲染期嵌套的非 VR 相机渲染。
- `ReflectionReenablePatch.cs` 阻止镜子/反射对象在 VR 激活时重新启用被拦截的渲染路径。
- `ReflectionBlocker.cs` 判断镜子、反射、target-texture 和 probe 对象。
- `InputMouseAxisPatch.cs` 在 HMD 姿态接管视角时屏蔽 legacy 鼠标视角输入。
- `BlackCensorControllerPatch.cs` 压制一个游戏侧会重复出现的空引用路径。

环形菜单插件：

- `SecretFlasherManakaRingMenuLongPress` 是独立 BepInEx 插件。
- 它 patch `RingMenuParentView.GetLongDown`，并把长按计数切换到 `RingMenuLongPressCount`。

## 测试清单

1. 启动 SteamVR，并连接头显的 PCVR 串流。
2. 从 PC 启动游戏。
3. 通过桌面游戏窗口进入实际游戏场景。
4. 检查 `BepInEx/LogOutput.log` 是否出现：

```text
OpenVR initialized via Valve binding
VR runtime initialized.
OpenVR texture submit succeeded for both eyes.
```

5. 在游戏内检查视角稳定性、重置视角、UI 面板、NPC 标记、Quest 输入和场景切换。

## 包目录结构

```text
SecretFlasherManakaVR_Package/
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

