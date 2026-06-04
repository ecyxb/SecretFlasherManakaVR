# Quest 3 Input Mapping Summary

## 中文

### 总体说明

当前输入系统把 Quest 3 手柄输入转换为三类输出：

- 虚拟 Unity InputSystem `Gamepad` 按键。
- 游戏的 `InputManager.InputType` 语义输入。
- 光标模式下的虚拟鼠标输入。

系统优先读取 SteamVR Input actions；如果 action 不可用，会回退到 legacy OpenVR controller state。输入系统每帧更新一次，当前手柄模式会显示在 VR UI 左下角：

- `normal mode`
- `action14 mode`
- `action58 mode`

### 基础按键约定

Quest 3 ABXY 按当前 PS 图形语义理解：

| Quest 3 | PS 图形 | 位置 |
| --- | --- | --- |
| A | Cross | 下 |
| B | Circle | 右 |
| Y | Square | 左 |
| X | Triangle | 上 |

Quest 3 肩键约定：

| Quest 3 | 代码/文档简称 |
| --- | --- |
| Left Grip | L1 |
| Right Grip | R1 |
| Left Trigger | L2 |
| Right Trigger | R2 |
| Left Stick Click | L3 |
| Right Stick Click | R3 |

### 模式切换

| 操作 | 结果 |
| --- | --- |
| L3 单独按下并释放 | 重置视角 |
| L3 + R3 | 切换光标模式 |
| 退出光标模式 | 固定回到 `mode0` |
| 非菜单 `mode0` 下按住 L2，再按 `ABXY` 或 `R2` | 进入 `mode1` |
| 非菜单 `mode0` 下按住 L1，再按 `ABXY` 或 `R2` | 进入 `mode2` |
| 松开进入临时模式的 L2 / L1 | 回到 `mode0` |
| 非菜单下 L2 / L1 未组成组合且按住小于 `1.5s` 后松开 | 分别输出 `Start` / `Select` |
| 非菜单下 L2 / L1 未组成组合且按住超过 `1.5s` 后松开 | 不输出 `Start` / `Select` |

菜单面板打开时不进入 `mode1` / `mode2`，L1 / L2 使用菜单 UI 特殊处理。
如果 L3 与 R3 在同一次按住期间发生重叠，则按组合键处理，只切换光标模式，不再触发 L3 单独重置视角或 R3 的 L1 输出。

### mode0: normal mode

| Quest 3 输入 | 输出 |
| --- | --- |
| A | Cross |
| B | Circle |
| Y | Square |
| X | Triangle |
| Left Trigger 短按松开 | Start |
| Left Trigger 长按超过 `1.5s` 后松开 | 不输出 |
| Left Trigger + `ABXY` / `R2` | 进入 `mode1` |
| Left Grip 短按松开 | Select |
| Left Grip 长按超过 `1.5s` 后松开 | 不输出 |
| Left Grip + `ABXY` / `R2` | 进入 `mode2` |
| Right Grip | R1 |
| Right Trigger | R2 |
| Right Stick Click | L1 |

### mode1: action14 mode

ABXY 在该模式下映射为十字键，方向按当前 PS 图形位置决定：

| Quest 3 输入 | PS 图形 | 输出 |
| --- | --- | --- |
| X | Triangle | DPadUp |
| A | Cross | DPadDown |
| Y | Square | DPadLeft |
| B | Circle | DPadRight |

其他输入：

| Quest 3 输入 | 输出 |
| --- | --- |
| Left Grip 短按松开 | Select |
| Right Grip | R1 |
| Right Stick Click | L1 |
| Right Trigger 按下 | DrinkWater |
| 松开 Left Trigger | 回到 `mode0` |

### mode2: action58 mode

| Quest 3 输入 | 输出 |
| --- | --- |
| 模式保持期间 | 持续输出 L2 |
| A | Cross |
| B | Circle |
| Y | Square |
| X | Triangle |
| Right Grip | R1 |
| Right Stick Click | L1 |
| Right Trigger 按下 | EyeMask |
| 松开 Left Grip | 回到 `mode0` |

### POP UI 覆盖

当输入系统识别到 POP UI 时，ABXY 使用 POP 导航映射。该覆盖优先于普通 mode 的 ABXY 映射。

| Quest 3 输入 | 输出 |
| --- | --- |
| Y | DPadUp |
| X | DPadDown |
| A | Cross |
| B | Circle |

当前 POP 识别包含：

- active `ChooseDildoPanelView`。
- active `BasePanelView` 层级中包含 `ChooseDildoPanel`。
- active UI 对象或层级中包含 `InteractMenuPanel`。

### Circle UI 覆盖

当输入系统识别到 Circle UI，也就是游戏的 ring menu 时，会启用特殊规则。

当前 Circle 识别包含：

- active `RingMenuParentView.Instance` 且 `IsOpenRing == true`。
- 任意 active `RingMenuParentView` 且 `IsOpenRing == true`。

规则：

| 条件 | 结果 |
| --- | --- |
| `mode1` / `mode2` 下，Circle UI 打开，且只有 A 或 B 一个 ABXY 按键按住 | 交换移动摇杆和镜头摇杆 |
| `mode1` / `mode2` 下，ABXY 按下后到抬起前从未检测到 Circle UI | 抬起时自动回到 `mode0` |
| `mode1` / `mode2` 下，ABXY 按住期间检测到 Circle UI | 抬起时保留当前 mode |

### 光标模式

L3 + R3 会进入或退出光标模式。退出时固定回到 `mode0`。

光标模式下不执行普通 mode 按键映射，而是使用右手射线和虚拟鼠标。

| Quest 3 输入 | 输出 |
| --- | --- |
| Right Trigger | 鼠标左键 + `InputManager.InputType.LeftClick` |
| Left Trigger | 非菜单 = Start；菜单 = L2 |
| Left Grip | `UiRingLeft` / `TabLeft` / `Tab2Left` |
| Right Grip | `UiRingRight` / `TabRight` / `Tab2Right` |
| Right Stick Click | L1 |
| A | `Interact` / `Accept` |
| B | `Cancel` / `SystemMenu` |
| Y | Square |
| X | Triangle |
| Left Stick | 默认保留虚拟左摇杆；衣橱/任务清单下用于子级滚动和衣橱 Slider |
| Right Stick | 保留虚拟右摇杆，不做滚轮或 `UIScrollA/B` 特殊映射 |

左摇杆方向输入使用死区和斜向保护：

- 死区由 `Quest3RightStickDeadzone` 控制。
- 斜向保护角度由 `Quest3RightStickDiagonalGuardDegrees` 控制。

### 菜单 UI 特殊处理

非光标模式下，菜单面板打开时 `L1` / `L2` 直接输出原本的 `L1` / `L2`，不触发 `Select` / `Start`，也不进入 `mode1` / `mode2`。左摇杆映射为 DPad。子级滚动面板目前是 `ClosetMenuView` 和 `MissionMenuPanelView`，左摇杆上下会寻找子级 `ScrollRect` 并发送滚轮事件，滚轮值为 `60`，不额外发送 DPad 上下。子级 Slider 面板目前只有 `ClosetMenuView`；光标模式下左摇杆左右只在从回中变成左右输入时查找并锁定一次可用 `Slider`，步长为 `0.005`，重复间隔从 `0.33s` 加速到最低 `0.06s`，回中、关闭衣橱、退出光标模式或 Slider 不可用时重置锁定。

## English

### Overview

The current input system maps Quest 3 controller input to three output layers:

- Virtual Unity InputSystem `Gamepad` buttons.
- Game `InputManager.InputType` semantic input.
- Virtual mouse input in cursor mode.

The system prefers SteamVR Input actions. If action input is unavailable, it falls back to legacy OpenVR controller state. The mapper updates once per frame, and the active controller mode is shown at the lower-left of the VR UI:

- `normal mode`
- `action14 mode`
- `action58 mode`

### Base Button Convention

Quest 3 ABXY is interpreted as the current PlayStation face-button shape layout:

| Quest 3 | PS shape | Position |
| --- | --- | --- |
| A | Cross | Bottom |
| B | Circle | Right |
| Y | Square | Left |
| X | Triangle | Top |

Quest 3 shoulder/stick-click naming:

| Quest 3 | Code/doc name |
| --- | --- |
| Left Grip | L1 |
| Right Grip | R1 |
| Left Trigger | L2 |
| Right Trigger | R2 |
| Left Stick Click | L3 |
| Right Stick Click | R3 |

### Mode Switching

| Input | Result |
| --- | --- |
| L3 press and release without R3 overlap | Recenter view |
| L3 + R3 | Toggle cursor mode |
| Exit cursor mode | Always return to `mode0` |
| Outside menu UI, hold L2 in `mode0` and press `ABXY` or `R2` | Enter `mode1` |
| Outside menu UI, hold L1 in `mode0` and press `ABXY` or `R2` | Enter `mode2` |
| Release the L2 / L1 that entered the temporary mode | Return to `mode0` |
| Outside menu UI, release L2 / L1 without a chord after a hold shorter than `1.5s` | Output `Start` / `Select` |
| Outside menu UI, release L2 / L1 without a chord after a hold longer than `1.5s` | Output nothing |

Menu panels do not enter `mode1` / `mode2`; L1 / L2 use menu-specific UI handling.
If L3 and R3 overlap during the same press, the mapper treats it as the combo only: it toggles cursor mode and suppresses L3-only recenter plus R3-as-L1 for that combo press.

### mode0: normal mode

| Quest 3 input | Output |
| --- | --- |
| A | Cross |
| B | Circle |
| Y | Square |
| X | Triangle |
| Left Trigger short release | Start |
| Left Trigger release after a hold longer than `1.5s` | No output |
| Left Trigger + `ABXY` / `R2` | Enter `mode1` |
| Left Grip short release | Select |
| Left Grip release after a hold longer than `1.5s` | No output |
| Left Grip + `ABXY` / `R2` | Enter `mode2` |
| Right Grip | R1 |
| Right Trigger | R2 |
| Right Stick Click | L1 |

### mode1: action14 mode

ABXY maps to D-pad directions in this mode. Direction follows the current PlayStation shape position:

| Quest 3 input | PS shape | Output |
| --- | --- | --- |
| X | Triangle | DPadUp |
| A | Cross | DPadDown |
| Y | Square | DPadLeft |
| B | Circle | DPadRight |

Other inputs:

| Quest 3 input | Output |
| --- | --- |
| Left Grip short release | Select |
| Right Grip | R1 |
| Right Stick Click | L1 |
| Right Trigger down | DrinkWater |
| Release Left Trigger | Return to `mode0` |

### mode2: action58 mode

| Quest 3 input | Output |
| --- | --- |
| While mode is active | Hold virtual L2 |
| A | Cross |
| B | Circle |
| Y | Square |
| X | Triangle |
| Right Grip | R1 |
| Right Stick Click | L1 |
| Right Trigger down | EyeMask |
| Release Left Grip | Return to `mode0` |

### POP UI Override

When POP UI is detected, ABXY uses POP navigation mapping. This takes priority over normal mode-specific ABXY mapping.

| Quest 3 input | Output |
| --- | --- |
| Y | DPadUp |
| X | DPadDown |
| A | Cross |
| B | Circle |

Current POP detection includes:

- An active `ChooseDildoPanelView`.
- An active `BasePanelView` hierarchy containing `ChooseDildoPanel`.
- An active UI object or hierarchy containing `InteractMenuPanel`.

### Circle UI Override

When Circle UI is detected, meaning the game's ring menu is open, special handling is enabled.

Current Circle detection includes:

- Active `RingMenuParentView.Instance` with `IsOpenRing == true`.
- Any active `RingMenuParentView` with `IsOpenRing == true`.

Rules:

| Condition | Result |
| --- | --- |
| In `mode1` / `mode2`, Circle UI is open, and exactly one ABXY button is held, and it is A or B | Swap movement and camera sticks |
| In `mode1` / `mode2`, ABXY was pressed and Circle UI was never detected before release | Return to `mode0` on release |
| In `mode1` / `mode2`, Circle UI was detected while ABXY was held | Keep the current mode on release |

### Cursor Mode

L3 + R3 toggles cursor mode. Exiting cursor mode always returns to `mode0`.

Cursor mode skips normal controller-mode button mapping and uses the right-hand ray plus virtual mouse input.

| Quest 3 input | Output |
| --- | --- |
| Right Trigger | Left mouse button + `InputManager.InputType.LeftClick` |
| Left Trigger | Outside menus = Start; menus = L2 |
| Left Grip | `UiRingLeft` / `TabLeft` / `Tab2Left` |
| Right Grip | `UiRingRight` / `TabRight` / `Tab2Right` |
| Right Stick Click | L1 |
| A | `Interact` / `Accept` |
| B | `Cancel` / `SystemMenu` |
| Y | Square |
| X | Triangle |
| Left Stick | Default virtual left stick; drives child scrolling and Closet sliders in supported panels |
| Right Stick | Virtual right stick; no mouse-wheel or `UIScrollA/B` special mapping |

Left-stick directional input uses a deadzone and diagonal guard:

- Deadzone is controlled by `Quest3RightStickDeadzone`.
- Diagonal guard angle is controlled by `Quest3RightStickDiagonalGuardDegrees`.

### Menu UI Special Handling

When a menu panel is open outside cursor mode, `L1` / `L2` output the original `L1` / `L2`, do not trigger `Select` / `Start`, and do not enter `mode1` / `mode2`. The left stick maps to DPad directions. The current child-scroll panel set is `ClosetMenuView` and `MissionMenuPanelView`; left-stick up/down searches child objects for a usable `ScrollRect` and sends mouse-wheel events with a delta of `60`, without extra DPad up/down. The current child-slider panel set is only `ClosetMenuView`; in cursor mode, left-stick left/right searches and locks one usable `Slider` only when the stick moves from neutral to horizontal input. Slider step is `0.005`; repeat starts at `0.33s`, accelerates to a minimum of `0.06s`, and the lock resets when the stick returns to neutral, the closet closes, cursor mode exits, or the Slider becomes unusable.
