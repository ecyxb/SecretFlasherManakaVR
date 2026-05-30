# OpenVR / SteamVR 输入接口整理

本文档用于说明 `SecretFlasherManakaVR` 这个 mod 可以从 SteamVR/OpenVR 拿到哪些 VR 数据，以及后续适配游戏输入时应该优先使用哪些接口。

## 名词关系

`SteamVR` 是运行时和平台。它负责连接头显、手柄、tracker，管理 tracking、compositor、驱动和输入绑定。

`OpenVR` 是程序调用 SteamVR 的 API。mod 通过 `openvr_api.dll` 和 Valve 的 C# binding 调用 OpenVR，再由 SteamVR 返回姿态、输入和渲染相关结果。

`SteamVR Input` 是 SteamVR 的新输入系统，在 OpenVR API 里对应 `IVRInput`。它使用 action manifest 和 binding 文件，让不同设备可以映射到同一组游戏动作。

当前调用链可以理解为：

```text
SecretFlasherManakaVR mod
        -> OpenVR API / openvr_api.dll
        -> SteamVR runtime / compositor
        -> Quest / Index / Vive / tracker 等设备
```

## 推荐接口总览

| 目标 | 推荐接口 | 说明 |
| --- | --- | --- |
| HMD 渲染姿态 | `IVRCompositor.WaitGetPoses` | 当前 mod 已经使用。适合每帧 VR 渲染，因为返回 compositor 预测姿态。 |
| HMD 普通 tracking 查询 | `IVRSystem.GetDeviceToAbsoluteTrackingPose` | 可查询 HMD 或其他 tracked device，但不是当前渲染主路径首选。 |
| 手柄按钮 | `IVRInput.GetDigitalActionData` | 推荐新接口。跨设备绑定更稳定。 |
| 摇杆/触摸板/扳机模拟量 | `IVRInput.GetAnalogActionData` | 推荐新接口。不要长期依赖旧式 `rAxis0..4` 设备布局。 |
| 手柄姿态 | `IVRInput.GetPoseActionDataForNextFrame` | 推荐新接口。可绑定 grip pose 或 aim pose。 |
| Tracker 姿态 | `IVRInput` pose action，或 `IVRSystem.GetDeviceToAbsoluteTrackingPose` fallback | 长期推荐 action；调试或扫描设备时可用旧式 tracked device 查询。 |
| 手部骨骼 | `IVRInput.GetSkeletalActionData` / `GetSkeletalBoneData` | 设备支持不稳定，优先级低。 |
| 震动 | `IVRInput.TriggerHapticVibrationAction` | 推荐新接口。旧式 `TriggerHapticPulse` 可作为 fallback。 |
| 旧式手柄状态 | `IVRSystem.GetControllerState` / `GetControllerStateWithPose` | 适合快速诊断或 fallback；不推荐作为最终主输入路径。 |

## 当前 mod 的头显姿态

当前头部姿态不是 SteamVR Input action，也不是 `IVRInput`。

当前路径是：

```text
IVRCompositor.WaitGetPoses
        -> poses[OpenVR.k_unTrackedDeviceIndex_Hmd]
        -> mDeviceToAbsoluteTracking
        -> Unity position / rotation
        -> VrRuntimeManager.ApplyPoseToRig
        -> 左右眼相机
```

相关代码：

- `OpenVRBridge.TryUpdatePoses()`
- `OpenVRBridge.TryGetHmdPose()`
- `VrRuntimeManager.TryGetHmdPose()`
- `VrRuntimeManager.ApplyPoseToRig()`
- `VrRuntimeState.SetHeadPose()`

这条路径是合理的，应当保留。渲染用 HMD pose 通常优先走 compositor pose，因为它和提交给 SteamVR compositor 的眼部画面同步。

## SteamVR Input 能提供什么

SteamVR Input 通过 action manifest 定义游戏动作，而不是直接绑定具体设备按钮。

常见动作类型：

| 类型 | OpenVR API | 可用于 |
| --- | --- | --- |
| Digital action | `GetDigitalActionData` | 确认、取消、交互、抓取、菜单、跳跃 |
| Analog action | `GetAnalogActionData` | 左摇杆移动、右摇杆转向、扳机力度、grip 力度 |
| Pose action | `GetPoseActionDataForNextFrame` | 左手 pose、右手 pose、tracker pose |
| Skeletal action | `GetSkeletalActionData` / `GetSkeletalBoneData` | 手指骨骼、手势 |
| Haptic action | `TriggerHapticVibrationAction` | 手柄震动 |

典型 action path 示例：

```text
/actions/main/in/move
/actions/main/in/turn
/actions/main/in/interact
/actions/main/in/cancel
/actions/main/in/submit
/actions/main/in/sprint
/actions/main/in/left_hand_pose
/actions/main/in/right_hand_pose
/actions/main/out/left_haptic
/actions/main/out/right_haptic
```

典型 input source 示例：

```text
/user/hand/left
/user/hand/right
/user/head
/user/treadmill
```

注意：HMD render pose 仍建议继续使用 `WaitGetPoses`，即使 SteamVR Input 理论上也可以定义 head pose action。

## 旧式 Controller State 能提供什么

旧式接口来自 `IVRSystem`：

```csharp
GetTrackedDeviceIndexForControllerRole
GetControllerState
GetControllerStateWithPose
TriggerHapticPulse
```

可以拿到：

- 左右手 controller role
- `ulButtonPressed`
- `ulButtonTouched`
- `rAxis0` 到 `rAxis4`
- 可选 controller pose
- 简单 haptic pulse

常见 button id：

```text
System
ApplicationMenu
Grip
DPad_Left / DPad_Up / DPad_Right / DPad_Down
A
Axis0 / SteamVR_Touchpad
Axis1 / SteamVR_Trigger
Axis2
Axis3
Axis4
```

这套接口的主要问题是不同设备的轴和按钮布局可能不一致。例如 Quest、Index、Vive Wand 的摇杆、触摸板、A/B/X/Y、trigger、grip 可能对应不同 axis/button bit。

因此它适合：

- 快速验证手柄是否接入
- 打印诊断日志
- SteamVR Input 尚未完成时作为 fallback

不适合作为最终主输入系统。

## 对本 mod 的推荐路线

### 1. HMD 姿态保持现状

保留：

```text
IVRCompositor.WaitGetPoses -> HMD pose -> VR camera rig
```

不要把 HMD 渲染姿态迁移到 SteamVR Input，除非后续有非常明确的同步或兼容问题。

### 2. 手柄输入改用 SteamVR Input

新增 action manifest，定义一组游戏动作：

```text
move
turn
interact
submit
cancel
sprint
left_hand_pose
right_hand_pose
left_haptic
right_haptic
```

每帧流程：

```text
SetActionManifestPath，一次性
GetActionSetHandle / GetActionHandle，一次性
UpdateActionState，每帧
GetDigitalActionData / GetAnalogActionData / GetPoseActionDataForNextFrame，每帧
```

然后将 action 结果映射到游戏现有输入。

### 3. 旧式 Controller State 只做诊断或 fallback

建议保留一个调试开关，用来打印：

```text
role
device index
pressed button bit
touched button bit
axis0..axis4
pose valid
```

这样可以快速确认 Quest/Index/Vive 实际按钮布局，但最终输入绑定应以 SteamVR Input action 为主。

## 需要映射到游戏的内容

游戏本身不是 VR 游戏，大概率仍然读取 Unity 输入，例如：

```csharp
Input.GetAxis("Horizontal")
Input.GetAxis("Vertical")
Input.GetKey(KeyCode.W)
Input.GetKeyDown(KeyCode.E)
Input.GetButton("Submit")
Input.GetMouseButton(0)
```

所以最终适配有两条路线：

1. 将 SteamVR Input action 映射成 Unity legacy input 返回值。
2. 找到游戏自己的输入/玩家控制类，直接 patch 游戏逻辑。

第一条侵入小，适合先跑通移动、交互、取消、确认。

第二条更精确，适合后续修正特殊动作、UI、相机或状态机问题。

## 建议的下一步

1. 保留当前 HMD pose 渲染路径。
2. 新增 SteamVR Input action manifest 和默认 bindings。
3. 加一个输入诊断日志模式，确认手柄 action 是否被 SteamVR 正确触发。
4. 将 `move/interact/submit/cancel/sprint` 映射到游戏现有输入。
5. 再根据实际游戏行为补充特殊动作映射。

一句话原则：

```text
HMD 渲染姿态继续用 compositor pose；
手柄、tracker、震动等交互输入优先用 SteamVR Input；
旧式 controller state 只作为诊断和 fallback。
```
