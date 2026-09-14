# 追踪模式与相机规则

`Legacy` 指今天加入身体追踪之前的原版 VR Mod 行为：包括原有头显视角、头/颈/胸联动、移动时身体转向和旧相机设置，并非禁止头显追踪。

在 `BepInEx/config/com.codex.secretflashermanaka.vr.cfg` 中选择：

```ini
[07 Tracking Mode - 追踪模式]
TrackingMode = ThreePoint
```

| 模式 | 身体输入 | 当前状态 | 独立参数区 |
| --- | --- | --- | --- |
| `Legacy` | 原版头部联动与游戏动画 | 可用 | 原来的 02/04 配置项 |
| `ThreePoint` | 头显、双手柄；下半身推算 | 可校准 | 08 Three Point |
| `SixPoint` | 头、双手、胯、双脚 | 预留，尚未接入真实追踪器 | 09 Six Point |
| `EightPoint` | 六点 + 双膝 | 预留 | 10 Eight Point |
| `TenPoint` | 八点 + 双肘 | 预留 | 11 Ten Point |
| `ElevenPoint` | 十点 + 胸部 | 预留 | 12 Eleven Point |

预留模式显示 `UnsupportedTrackingMode`，不会自动改成三点或用推算位置冒充设备。桌面 Pose Lab 的模拟 6/8/10/11 点不等于真实 VR 设备接入。

## 相机及旧功能审视结果

| 功能 | Legacy | ThreePoint，包括未校准/释放/设备丢失时 |
| --- | --- | --- |
| `CameraHeightOffset` | 保留原值和行为 | 忽略；校准眼位使用独立配置 |
| `IgnoreHeadPositionForVrCamera` 及 XYZ 范围 | 保留原值和限制 | 忽略，完整使用头显位移 |
| 原相机位置/朝向平滑 | 保留 | 忽略，避免头手使用不同时间的基准 |
| 旧头/颈/胸旋转叠加及权重/角度限制 | 按原开关运行 | 禁用；不能因 F6 释放又自动接管 |
| 旧“移动时头朝向带动身体” | 按原开关运行 | 禁用，尚无新的自动身体转向求解 |
| 相机位置来源 | 原游戏相机 | 校准前：游戏相机 + 完整 HMD 位移；校准后：角色根节点下的固定眼位 + HMD 位移 |
| 骨骼与相机反馈 | 原逻辑 | 相机不读取已被 IK 移动的头骨作为逐帧基点 |
| 手柄射线 | 原映射和 L3 + R3 切换 | 同一追踪空间；相机更新后刷新射线几何，组合键保持原样 |
| 双眼间距 | 原比例 | 校准后与头手位移使用同一比例 |
| 第一人称头颈遮挡处理 | 原配置 | 独立 `HideHeadInFirstPerson`，只在眼部渲染期间修改并恢复 |
| Mode0 右摇杆上下视角 | 原抑制条件 | 身体追踪激活时抑制；光标模式不受影响 |
| 镜子、渲染分辨率、后处理、UI 面板、NPC 标记 | 共用原配置 | 共用原配置；这些不负责骨骼或追踪原点 |

传统模式刻意保留原来的相机和射线映射，包括有限位移下从坐姿站起可能出现的偏差。三点模式从菜单开始就使用完整头显位移，避免同一问题。三点身体校准仍应站直；坐姿标定会改变眼高比例。

每个追踪参数区分别保存 `TrackingScale`、`AutoScale`、`EyeHeightOffset`、`EyeForwardOffset` 和 `HideHeadInFirstPerson`。眼位偏移同时进入头显与双手的共同坐标映射；它不是只抬高相机的补丁。比例和眼位修改后需要重启并重新校准。预留模式的参数目前不参与求解。

## 切换与恢复

运行中修改 `BepInEx/config/ManakaVRBody/control.json`：

```json
{ "Action": "mode", "Mode": "Legacy" }
```

或将 `Mode` 改成 `ThreePoint`。模式写入主配置并保留到下次启动；旧命令文件不会在启动时重放。每次切换都会释放 IK、恢复动画、清除校准和旧相机平滑状态。在三点模式中按 F6 校准；F6 释放仍留在三点模式，要回原版必须明确选择 Legacy。

F7 释放并重新校准。F12 或手柄原有的重置操作重置视角并释放身体控制，需要再按 F6。切场景、玩家替换、离开第一人称进入游戏识别的第三人称/物品视角、设备持续丢失也会释放。相机模式识别基于游戏已有状态，仍需实测特殊过场和 GameOver 相机。

安装时可指定模式，重复安装不指定则保留现有选择：

```powershell
scripts\vr-body.ps1 -GameRoot "E:\SecretFlasherManaka v1.1.3" -Action Install -Mode ThreePoint
```

旧版 `EnableThreePointBody`、`ThreePointTrackingScale`、`ThreePointAutoScale` 仅用于首次迁移。新参数已存在后，旧值不再控制追踪。原版相机参数保留在原位置，不覆盖用户数值。

`status.json` 包含 `SelectedMode`、`ModeSupported`、`CameraPolicy`、`LegacyHeadControlAllowed`；只有身体求解实际激活时 `TrackingPoints=3`，否则为 0。无追踪输入时不能把旧的目标或比例当成当前测量。

验证：纯数学测试覆盖模式切换、旧相机限位与新模式完整位移、校准、IK 和双眼比例；C# 构建不能代替头显内对高度、过场和转向的主观/运行时验证。
