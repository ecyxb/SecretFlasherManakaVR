# Steam Link 三点身体追踪

VR 插件 0.2.0 接入真实的头显、左手柄、右手柄。三者使用同一帧 SteamVR compositor 追踪数据，身体 IK 在左右眼渲染前应用。桌面模拟插件需要停用。

当前版本已按模式隔离旧功能，参见 [追踪模式与相机规则](TRACKING_MODES.zh-CN.md)。`Legacy` 保留身体追踪改动前的原版行为；`ThreePoint` 使用独立配置。

## 安装

关闭游戏后，在仓库根目录运行：

```powershell
scripts\vr-body.ps1 -GameRoot "E:\SecretFlasherManaka v1.1.3" -Action Install -Mode ThreePoint
```

脚本构建并安装 VR 插件，开启 SteamVR 和三点身体控制，备份后移除独立 Pose Lab DLL。首次从桌面实验切换时恢复桌面实验之前的 VR 配置。备份在 `BepInEx/config/ManakaVRBody/backup`；重复安装保留最初备份。

## 首次体验

1. 在头显内通过 Steam Link 连接电脑，确保 SteamVR 能追踪头显和左右手柄。
2. 启动游戏，进入实际场景，等待角色加载。建议先在静止场景测试。
3. 面朝前方站直，双手自然下垂，握住手柄，按 **F6** 校准并启用。头显位置和朝向提供共同坐标原点；手柄位置根据它相对头显的位置求解，不会对齐到游戏当时的环抱姿势。手腕以从手指骨骼推导的下垂、掌心朝腿部姿态校准。
4. 缓慢转头、抬起一只手、旋转手腕，再轻微下蹲。头、手使用真实设备位姿；胯部和脚的位置由头部位移推算。

| 操作 | 按键 |
| --- | --- |
| 校准并启用 / 释放身体控制 | F6 |
| 释放，等待动画恢复后重新校准 | F7 |
| 原有 VR 视角重置；会释放身体控制，需要再按 F6 | F12 |

身体、头显、光标的追踪距离和左右眼间距共用校准后的比例，避免缩放后立体视觉中的手柄高度和距离不一致。

头显内部的相机基点固定在角色根节点下，不跟随被 IK 修改的头骨，避免头动引发相机和骨骼反复叠加位移。激活身体控制时替代原有头/颈/胸旋转叠加；释放时恢复动画、RigBuilder、DynamicBone 等状态。

## 追踪和校准限制

- 三个设备都有效才允许校准。丢失任意设备时最多保持上一次身体目标 0.75 秒，随后释放并恢复动画；重新连接后按 F6。头显无效时不把零位姿当作有效输入渲染。
- 场景切换、玩家对象更换、SteamVR 关闭后释放控制，需要重新校准。
- 腰、脚、膝、肘没有真实追踪器。此版本没有自动步态、落脚、碰撞和身体转向求解；横向移动时推算的脚会平移。不是 6 点或 11 点真实动捕。
- 头显和双手共用一个位置基准。默认根据角色眼高与站姿头显高度做统一缩放，但尚无精细臂长拟合、控制器安装偏移配置或手指动捕；手柄握持键只驱动简单握拳。
- 校准后的设备位移按角色朝向映射；当前身体位移范围约为校准位置周围 3 米。手脚超出骨长可达范围会留下误差。先验证小范围动作。
- `07 Tracking Mode` 中选择 `TrackingMode=ThreePoint`。`08 Three Point` 的 `AutoScale` 默认开启；`TrackingScale` 默认 1，在自动比例上额外乘以 0.5–1.5 的系数。更改配置后重启并重新校准。旧三个参数只用于首次迁移。

## 诊断与电脑端校准

`BepInEx/config/ManakaVRBody/status.json` 每秒记录状态、三个设备的有效性、身体目标、实际位置和 IK 误差。身体求解激活时 `TrackingPoints` 为 3，否则为 0；`PhysicalIpdMetres`、`RenderedEyeSeparationMetres`、`ExpectedEyeSeparationMetres` 用于验证双眼间距与追踪比例是否一致。目标数组仍按 `Head / Hips / Left hand / Right hand / Left foot / Right foot` 排列，其中 Hips 和双脚是推算值。

写入 `BepInEx/config/ManakaVRBody/control.json` 可以从电脑触发一次操作：

```json
{ "Action": "calibrate" }
```

或写入 `{ "Action": "release" }`。每次修改文件时触发一次；请在设备和站姿准备好后写入。该文件只接受操作命令，不接受伪造设备位姿。

恢复安装前状态（先关闭游戏）：

```powershell
scripts\vr-body.ps1 -GameRoot "E:\SecretFlasherManaka v1.1.3" -Action Restore
```

如果安装前处于桌面实验模式，此命令会恢复桌面实验。再使用 `pose-lab.ps1 -Action Restore` 才会返回桌面实验之前的环境；按安装的相反顺序恢复。

数学验证：`dotnet run --project tests/PoseMath.Tests/PoseMath.Tests.csproj -c Release`。新增测试覆盖不同初始朝向、位移缩放、头手非零初始旋转和追踪原点偏移。

追踪坐标接口依据 [Valve OpenVR tracking 文档](https://github.com/ValveSoftware/openvr/wiki/IVRSystem%3A%3AGetDeviceToAbsoluteTrackingPose)，延用项目的 standing origin 和 OpenVR → Unity 坐标转换。


### 坐姿进入后站起的光标校正

原有普通 VR 视角配置 `IgnoreHeadPositionForVrCamera=true` 会把头显相机位移限制在配置范围内（本机为 ±5 cm）。现在三点模式在菜单和校准前也忽略旧限位，完整使用头显位移，让射线和头显保持共同坐标系。Legacy 刻意保留原版行为；详见模式规则表。

`HeadTrackingAlignmentErrorMetres` 比较控制器坐标系映射的头显位置和实际双眼中点，用于检查两条坐标路径是否一致。三点身体追踪启用后使用完整位移，没有相机限制补偿；校准身体比例时仍应站直。
