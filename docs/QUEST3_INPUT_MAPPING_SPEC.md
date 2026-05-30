# Quest 3 输入映射 SPEC

本文档定义 `SecretFlasherManakaVR` 后续输入适配的目标行为。Quest 3 作为默认能力基准；如果某项能力普通一体机或常见 VR 设备无法稳定支持，实现前需要单独标注。

## 目标

将 Quest 3 手柄输入映射为游戏可理解的手柄、鼠标和 `InputManager.InputType` 语义输入。

输入系统需要支持：

- 普通手柄映射模式。
- 光标模式，用右手柄射线模拟鼠标点击 UI。
- 根据特定 UI 状态自动改变部分按键或摇杆映射。
- 可配置的长按阈值。

## Quest 3 到 PS 布局约定

Quest 3 ABXY 按 PS 图形位置理解：

| Quest 3 | PS 图形 | 常见语义 |
| --- | --- | --- |
| A | Cross / `×` | 下方键 |
| B | Circle / `○` | 右方键 |
| X | Square / `□` | 左方键 |
| Y | Triangle / `△` | 上方键 |

Quest 3 trigger / grip 按 PS 肩键理解：

| PS 输入 | Quest 3 输入 |
| --- | --- |
| L1 | 左 Grip |
| R1 | 右 Grip |
| L2 | 左 Trigger |
| R2 | 右 Trigger |
| L3 | 左摇杆按下 |
| R3 | 右摇杆按下 |

Quest 3 Meta/Oculus 键不纳入映射，因为通常由系统保留，游戏侧不能稳定读取。

## 全局规则

### Start / Select

Quest 菜单键不纳入映射，因为通常由系统保留，游戏侧无法稳定读取。

`mode0` 下使用 `L1`（左 Grip）承担 `Start / Select`：

- 短按：映射为 `Start`。
- 长按：映射为 `Select`。

在 `mode1` / `mode2` 下，`L1` 不承担 `Start / Select`，而是映射为原本的 `L1`。

长按阈值：

- 配置项控制。
- 默认值：`0.5s`。

### L2

`L2` 默认不直接映射为原本的 `L2` 输入。

`L2` 用于手柄模式的子模式切换。

例外：进入 `mode2` 后，游戏侧应持续收到“原本 L2 正在按住”的状态，直到退出 `mode2`。

### 光标模式切换

`L3 + R3` 同时点击用于切换光标模式。

- 从手柄模式进入光标模式。
- 从光标模式退出时，固定回到手柄模式 `mode0`。

## 手柄模式

手柄模式包含三个子模式：

- `mode0`
- `mode1`
- `mode2`

### mode0：默认正常手柄

默认进入 `mode0`。

映射：

- ABXY 映射为 PS 图形键：`× ○ □ △`。
- 十字键缺失，不由 ABXY 提供。
- `L1`（左 Grip）短按映射为 `Start`，长按映射为 `Select`。
- `R3` 映射为原本 `L1`。
- `L2` 不映射为原本 `L2`。

切换：

- 点按 `L2`：切到 `mode1`。
- 长按 `L2` 后放开：切到 `mode2`。

### mode1：ABXY 映射为十字键

映射：

| Quest 3 | PS 图形 | 输出 |
| --- | --- | --- |
| Y | `△` | DPad Up |
| A | `×` | DPad Down |
| X | `□` | DPad Left |
| B | `○` | DPad Right |

其他：

- `L2` 不映射为原本 `L2`。
- `L1` 保持原本 `L1` 映射。
- `R3` 保持原本 `R3` 映射。
- `R1` 保持原本 `R1` 映射。
- `R2` 映射为 `InputManager.InputType.DrinkWater`。

切换：

- 点按 `L2`：切回 `mode0`。

### mode2：持续 L2 + 特殊动作

映射：

- 游戏侧持续收到原本 `L2` 按住。
- ABXY 映射为 PS 图形键：`× ○ □ △`。
- `L1` 保持原本 `L1` 映射。
- `R3` 保持原本 `R3` 映射。
- `R1` 保持原本 `R1` 映射。
- `R2` 映射为 `InputManager.InputType.EyeMask`。

切换：

- 点按 `L2`：切回 `mode0`。

## POP 弹窗规则

当某些 `POP` 弹窗出现时，部分按键进入弹窗导航映射。

当前确认规则：

| Quest 3 | 输出 |
| --- | --- |
| Y | DPad Up |
| B | DPad Down |
| A | Circle / `○` |
| X | Cross / `×` |

说明：

- 该规则优先级高于普通 ABXY 映射，仅用于非光标手柄模式。
- `POP` 的识别方式暂未确定，需要后续通过 UI 诊断确认具体 GameObject 名称、路径或组件类型。
- 在未确认识别方式前，不应写死模糊判断。

## Circle 界面规则

在 `mode1` 和 `mode2` 下，如果某个 `Circle` 界面打开，并且 ABXY 中恰好只有一个键按下，则启用摇杆处理修正规则。

规则：

- 如果按下的是 `A` 或 `B`：左摇杆和右摇杆输入对换。
- 如果按下的是 `X` 或 `Y`：摇杆正常处理。
- 如果 ABXY 同时按下多个键：该特殊规则无效。

说明：

- 该规则只在 `mode1` / `mode2` 下生效。
- `Circle` 的识别方式暂未确定，需要后续通过 UI 诊断确认具体 GameObject 名称、路径或组件类型。

## 光标模式

光标模式用于用右手柄模拟鼠标点击 UI。

进入 / 退出：

- `L3 + R3` 同时点击进入。
- 再次 `L3 + R3` 同时点击退出，并固定回到手柄模式 `mode0`。

### 左手柄规则

光标模式下，左手柄仅保留：

- 左摇杆：正常映射成摇杆输入。
- `L1`：左切换语义。
- `L2`：鼠标右键。

左手柄其他按键不反映到游戏输入。

### 右手柄光标

右手柄用于指向 UI：

- 使用右手柄 pose 作为射线方向。
- 需要显示可见直线。
- 优先查找是否存在可用官方 API 或游戏已有 UI raycast 机制。
- 如果没有官方 API，则在 Unity 中自行绘制射线。
- 初版可以不处理 world-space UI，只处理普通屏幕 UI / overlay UI。

### 鼠标模拟

光标模式下：

| Quest 3 | 输出 |
| --- | --- |
| R2 | 鼠标左键 |
| L2 | 鼠标右键 |
| L1 | `UiRingLeft` / `TabLeft` / `Tab2Left` |
| R1 | `UiRingRight` / `TabRight` / `Tab2Right` |
| A | `Cancel` / `SystemMenu` |
| B | `Interact` / `Accept` |

### 右摇杆规则

右摇杆映射：

| 方向 | 输出 |
| --- | --- |
| 左 | 无 |
| 右 | 无 |
| 上 | 鼠标滚轮上 / `UIUp` |
| 下 | 鼠标滚轮下 / `UIDown` |

限制：

- 同一时间只能产生一个方向输入。
- 摇杆斜角附近各 `15°` 范围不作为有效输入，防止误触。
- 必须存在死区，防止轻微漂移误触。

建议配置项：

- 右摇杆死区，默认可先设 `0.35`。
- 斜角无效角度，默认 `15°`。
- 滚轮步进强度，默认后续根据游戏 UI 调整。

## UI 状态识别

需要识别的 UI 状态：

- `POP` 弹窗。
- `Circle` 界面。

识别方式待定，建议实现前先增加诊断模式：

```text
LogActiveUiOnInput = true
LogInputConsumers = true
LogCurrentSelectedUi = true
```

诊断信息至少包含：

- 当前 scene。
- 当前 active Canvas 列表。
- 当前 active panel / popup 的 GameObject 路径。
- `EventSystem.current.currentSelectedGameObject`。
- 输入发生时的调用栈或输入名。

确认具体对象后，再实现稳定判断，例如：

- 按 GameObject 名称。
- 按完整层级路径。
- 按组件类型。
- 按 Canvas / Panel active 状态组合。

## 模式状态机

```text
手柄模式 mode0
    点按 L2 -> mode1
    长按 L2 -> mode2
    L3+R3 -> 光标模式

手柄模式 mode1
    点按 L2 -> mode0
    L3+R3 -> 光标模式

手柄模式 mode2
    点按 L2 -> mode0
    L3+R3 -> 光标模式

光标模式
    L3+R3 -> 手柄模式 mode0
```

## 待确认 / 实现前注意

1. `POP` 弹窗的稳定识别方式。
2. `Circle` 界面的稳定识别方式。
3. 光标模式 UI raycast 是用游戏已有 UI 事件系统、Unity `GraphicRaycaster`，还是模拟 OS 鼠标。
4. `Start / Select / PS 图形键 / DPad / L2` 最终映射到游戏侧哪套输入接口：Unity legacy input、SteamVR Input action，或直接 patch 游戏输入逻辑。
5. Quest 3 以外设备如 Vive Wand 没有 ABXY，若要支持需要单独绑定策略。

## 实现建议

推荐先实现诊断，再实现映射：

1. 保持 HMD pose 现有 `IVRCompositor.WaitGetPoses` 路径不变。
2. 增加 SteamVR Input action manifest，优先用 `IVRInput` 获取手柄按钮、摇杆、pose 和 haptic。
3. 用旧式 `IVRSystem.GetControllerState` 仅做诊断或 fallback。
4. 增加 UI 诊断，确认 `POP` / `Circle`。
5. 实现手柄模式状态机。
6. 实现光标模式射线和 UI 点击。

核心原则：

```text
HMD 渲染姿态继续用 compositor pose；
交互输入优先用 SteamVR Input；
UI 上下文决定映射；
Quest 3 是默认能力基准。
```

## 已确认 UI 定义：POP / Circle

本节把前文的 `POP` / `Circle` 缩写固定到当前已定位的游戏 UI。实现时应优先按组件类型识别，运行时路径和对象名只作为诊断或兜底。

### Circle：环形快捷菜单

`Circle` 指截图中的 4/8 分区环形快捷菜单，即游戏本体的 `RingMenu`。

稳定识别：

- 父组件类型：`ExposureUnnoticed2.ObjectUI.InGame.RingMenu.RingMenuParentView`
- 圆环组件类型：`ExposureUnnoticed2.ObjectUI.InGame.RingMenu.RingMenuView`
- 扇区项类型：`ExposureUnnoticed2.ObjectUI.InGame.RingMenu.RingItemView`

已观察到的运行时层级：

```text
InGameManager
└─ InGameCanvas
   └─ MiddleLayer
      └─ RingMenuParent
         ├─ RingParent
         ├─ LeftRightLabelParent
         └─ DescriptionParent
```

关键字段和方法：

- `RingMenuParentView.IsOpenRing`
- `RingMenuParentView.CurrentRingIndex()`
- `RingMenuParentView.CurrentShortcutIndex()`
- `RingMenuView.currentAngle`
- `RingMenuView.currentSelectView`
- `RingMenuView.targetKey`
- `RingMenuView.SetShow(bool)`
- `RingMenuView.AngleUpdate()`

输入相关枚举：

- `InputManager.InputType.CommonRingMenu = 14`
- `InputManager.InputType.SkillRingMenu = 23`
- `InputManager.InputType.SkillRingMenu3 = 27`
- `InputManager.InputType.SkillRingMenu4 = 28`
- `InputManager.InputType.UIUp = 1001`
- `InputManager.InputType.UIDown = 1002`
- `InputManager.InputType.UiRingLeft = 1010`
- `InputManager.InputType.UiRingRight = 1011`
- `InputManager.InputType.SkillRingMenu5..8 = 1018..1021`

判定建议：

- 强判定：场景中存在 active `RingMenuParentView`，且 `IsOpenRing == true`。
- 兜底判定：active 对象路径包含 `InGameCanvas/MiddleLayer/RingMenuParent`，并且存在可见的 `RingMenuView`。
- 不要把 `CircleGauge`、`NpcUiView.subCircleGauge` 或其他圆形量表当作本 spec 中的 `Circle`。

### POP：放置/回收弹窗

`POP` 指截图中的 `地面 / 面前的物体 / 取消` 放置选择弹窗，以及 `InteractMenuPanel` 交互菜单弹窗。

稳定识别：

- 组件类型：`ExposureUnnoticed2.ObjectUI.ChooseDildoPanelView.ChooseDildoPanelView`
- 基类：`Common.Scripts.UI.BasePanelView`
- prefab 引用：`ExposureUnnoticed2.Object3D.IngameManager.AssetReferencer.ChooseDildoPanel`
- 面板特征：active UI 路径或对象名包含 `InteractMenuPanel`

关键字段和方法：

- `ChooseDildoPanelView.Open()`
- `ChooseDildoPanelView.LateUpdate()`
- `ChooseDildoPanelView.buttonGroupManager`
- `ChooseDildoPanelView.currentSelectIndex`
- `ChooseDildoPanelView.OnClickPutFloor()`
- `ChooseDildoPanelView.OnClickPutWall()`
- `ChooseDildoPanelView.OnClickCancel()`
- `BasePanelView.IsActivePanel()`

按钮语义：

- `地面` => `OnClickPutFloor()`
- `面前的物体` => `OnClickPutWall()`
- `取消` => `OnClickCancel()`

运行时挂载：

- 由 `InGameUiManager.Open(BasePanelView view, bool isFront = false)` 打开和管理。
- 实例通常挂在 `InGameCanvas` 的 panel/front layer 下；具体路径可用于日志诊断，但不要作为唯一判定条件。

判定建议：

- 强判定：场景中存在 active `ChooseDildoPanelView`，且 `IsActivePanel() == true`。
- 交互菜单判定：场景中存在 active `InteractMenuPanel`。
- 兜底判定：`InGameUiManager.GetCurrentBasePanelView()` 返回或可转换为 `ChooseDildoPanelView`，或者 active 对象名/路径包含 `ChooseDildoPanel`。
- 不再泛化 `ExposureUnnoticed2.ObjectUI.CommonPopup.CommonPopupView`；如果后续需要覆盖其他弹窗，优先添加明确面板特征。

### UI 优先级

1. `POP` 优先级高于 `Circle`。
2. 光标模式优先级高于手柄模式上下文映射，但 `POP` / `Circle` 仍用于决定 ABXY、DPad 和摇杆修正语义。
3. 如果 `POP` 和 `Circle` 同时 active，按 `POP` 处理，并记录诊断日志。
4. 如果没有命中上述强判定，回退到原有 `mode0/mode1/mode2` 映射。

### 实现提示

- UI 识别逻辑应集中在一个上下文探测层，输出类似 `None / NormalPanel / POP / Circle / Cursor` 的状态，输入映射层只消费状态，不直接遍历 UI。
- 诊断日志建议同时输出组件类型、active 状态、对象路径、当前选中索引和触发来源。
- 对 `Circle`，优先复用游戏现有的 ring input/selection 逻辑；除非必须，不直接改 `currentAngle`。
- 对 `POP`，优先通过面板的按钮选择和确认路径触发，不绕过 `OnClickPutFloor()` / `OnClickPutWall()` / `OnClickCancel()` 的原始行为。
