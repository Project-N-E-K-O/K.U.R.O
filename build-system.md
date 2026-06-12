# 构筑系统具体实现文档

## 一、系统概述

构筑系统分为两个独立层次，互相补充：

| 层次 | 名称 | 获取方式 | 作用 | 
|---|---|---|---|
| 构筑效果 | Build Effect | 分数三选一 / 宝箱 | 数值加成 + 推动等级进度 |
| 构筑等级 | Build Level | 构筑效果点数累积自动解锁 | 行为改变（击退减免、霸体、护盾等） |

数据流：

```
击杀敌人 → 分数增加 → ScoreThresholdCurve 触发 → 三选一弹窗
  → 玩家选择 BuildEffectDefinition
  → ApplyEffectBonuses（数值加成）+ AddBuildEffectPoints（构筑点数）
  → PlayerBuildController 累积点数 → 达到阈值  → 自动激活 Build Level 效果
```

---

## 二、核心文件

### 2.1 数据定义

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/systems/BuildEffectDefinition.cs` | Resource (`[GlobalClass]`) | 三选一弹窗中的可选构筑效果 |
| `scripts/systems/ScoreThresholdCurve.cs` | Resource (`[GlobalClass]`) | 分数阈值曲线计算器 |
| `scripts/actors/heroes/BuildLevelEffectEntry.cs` | Resource | 构筑等级条目的配置 |

### 2.2 运行时逻辑

| 文件 | 类型 | 说明 |
|---|---|---|
| `scripts/managers/BuildSelectionManager.cs` | Node（场景节点） | 监听分数，触发三选一弹窗 |
| `scripts/ui/BuildSelectionWindow.cs` | Control + tscn | 三选一卡牌 UI |
| `scripts/actors/heroes/PlayerBuildController.cs` | Node（玩家子节点） | 统计构筑点数，管理 Build Level 效果 |

### 2.3 场景

| 文件 | 说明 |
|---|---|
| `scenes/managers/BuildSelectionManager.tscn` | 挂载到战斗场景，配置 ThresholdCurve + EffectPool |
| `scenes/ui/windows/BuildSelectionWindow.tscn` | 三选一弹窗 UI（900x380 横向三卡牌） |

---

## 三、BuildEffectDefinition

`[GlobalClass]` Resource，定义三选一弹窗中每一张卡牌的内容。

### 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `EffectId` | string | 唯一标识 |
| `DisplayName` | string | 卡牌标题 |
| `Description` | string (MultilineText) | 卡牌描述 |
| `BuildClass` | string | 所属构筑类别（Guard / Machine / Banquet ...） |
| `LevelCount` | int (1-10) | 选择后贡献的构筑点数，通常为 1 |
| `StatBonuses` | Dictionary<string, float> | 选择后直接施加的属性修正 |

### StatBonuses 支持的 Key

| Key | 效果 |
|---|---|
| `attack_damage` | 玩家基础攻击力 +Value |
| `speed` | 玩家移动速度 +Value |
| `max_health` | 最大生命值 +Value（同时恢复等量 HP） |

### 编辑器配置方式

1. 在 `BuildSelectionManager` 节点的 `EffectPool` 数组中添加内联 Resource
2. 或创建独立 `.tres` 文件（`resources/builds/` 下）

---

## 四、ScoreThresholdCurve

`[GlobalClass]` Resource，用曲线公式计算分数触发阈值。

### 公式

```
LevelScore = Bxp × (Level + Offset) ^ Scale ^ Power + Add
累计阈值 = Σ(LevelScore from 1 to N)
```

### 参数

| 参数 | 默认值 | 说明 |
|---|---|---|
| `Bxp` | 100 | 基础分数需求 |
| `Offset` | 0 | 等级偏移，>0 提高初期需求 |
| `Scale` | 1 | 陡峭程度，>1 增长加快 |
| `Power` | 1.5 | 幂指数，>1 加速增长 |
| `Add` | 0 | 修正常数 |

### 默认曲线示例

| 触发次数 | 累计分数阈值 |
|---|---|
| 第 1 次 | ~100 |
| 第 2 次 | ~382 |
| 第 3 次 | ~902 |
| 第 4 次 | ~1703 |

---

## 五、BuildSelectionManager

场景节点，挂载到战斗场景中使用。核心逻辑：

1. `_Process` 中自动绑定玩家 `SamplePlayer`
2. 监听 `SamplePlayer.StatsUpdated` 事件
3. 当前分数达到 `ThresholdCurve` 计算的下一个累计阈值时触发
4. 从 `EffectPool` 中随机抽取 3 个不同的 `BuildEffectDefinition`
5. 实例化 `BuildSelectionWindow` 展示三张卡牌
6. 玩家选择后：`ApplyEffectBonuses` + `PlayerBuildController.AddBuildEffectPoints`

### 导出字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `ThresholdCurve` | ScoreThresholdCurve? | 分数阈值曲线 |
| `EffectPool` | Array<BuildEffectDefinition> | 可选构筑效果池 |
| `DebugTrigger` | bool | 调试开关，勾选后立即弹窗（自动复位） |

### 开关/条件

- `EffectPool` 为空时不会触发弹窗
- `ThresholdCurve` 为 null 时不会触发弹窗
- `_isSelectionActive` 防止弹窗叠加（正在选择时跨越新阈值也不会弹第二个窗）

---

## 六、BuildSelectionWindow

### UI 结构

```
BuildSelectionWindow (Control, 全屏)
├── Overlay (ColorRect, 55%黑遮罩)
├── Panel (PanelContainer, 居中 900×380)
│   └── MainVBox
│       ├── TitleLabel ("选择一个强化效果")
│       ├── HSeparator
│       ├── Cards (HBoxContainer, 横向三列)
│       │   ├── Card0 (VBoxContainer)
│       │   │   ├── KeyLabel ("[1]")
│       │   │   ├── NameLabel
│       │   │   ├── BuildClassLabel ([安保协议])
│       │   │   ├── DescLabel
│       │   │   └── ProgressLabel (●/◐/○ Lv1/Lv2/Lv3)
│       │   ├── VSeparator
│       │   ├── Card1 (同上)
│       │   ├── VSeparator
│       │   └── Card2 (同上)
│       ├── HSeparator
│       └── HintLabel ("按 1/2/3 或 ←→ + Enter 选择")
```

### 等级进度预览

每张卡牌底部显示选中后对 Build Level 的推动效果：

| 符号 | 含义 | 示例 |
|---|---|---|
| ● | 已解锁 | `● Lv1 已激活` |
| ◐ | 选中后解锁 | `◐ Lv2 ← 选中即解锁` |
| ○ | 未解锁 | `○ Lv3 (3/6)` |

进度数据从 `PlayerBuildController.BuildCountByClass` 实时读取。

### 交互

| 按键 | 动作 |
|---|---|
| 1/2/3 | 直接选择对应卡牌 |
| ←→ / A/D | 移动高亮 |
| Enter / Space | 确认当前高亮 |

### 暂停管理

- 窗口打开时 `PauseManager.PushPause()`
- 窗口关闭后 0.15s 延迟 `PopPause()`，防止确认按键泄漏到游戏

---

## 七、PlayerBuildController

玩家子节点，管理构筑效果计数和 Build Level 效果的生命周期。

### 构筑点数来源

| 来源 | 存储位置 | 说明 |
|---|---|---|
| 武器 BuildClass + LevelCount | 自动统计（inventory 物品） | 武器上的构筑值 |
| 投掷中武器 | `_inFlightThrowItems` | 飞行期间点数不丢失 |
| 三选一/宝箱获取 | `_pickedEffectCountByClass` | `AddBuildEffectPoints()` 写入 |

### 关键方法

| 方法 | 说明 |
|---|---|
| `AddBuildEffectPoints(buildClass, count)` | 外部添加构筑效果点数，自动 `RefreshBuildState()` |
| `RefreshBuildState()` | 重新计算各构筑类型的点数和等级，同步效果 |

### 等级阈值

构筑等级效果的激活条件由 `LevelEntries` 中的 `BuildLevelEffectEntry` 控制：

| 字段 | 说明 |
|---|---|
| `BuildClass` | 所属构筑类别 |
| `Level` | 等级（1/2/3） |
| `RequiredPoints` | 所需点数 |
| `EffectId` / `EffectScript` | 对应的 ActorEffect |

### 现有 Build Level 效果

| EffectId | 显示名 | 等级 | 行为 |
|---|---|---|---|
| `Build_Machine_level1` | 机械I | Lv1 | 减少受到攻击时的击退力度 |
| `Build_Machine_level2` | 机械II | Lv2 | 攻击中及攻击后短时间内不会被非控制技能打断 |
| `Build_Machine_level3` | 机械III | Lv3 | 连续攻击命中时按百分比提升伤害 |
| `Build_Guard_level1` | 安保I | Lv1 | 受到伤害时减免固定值 |
| `Build_Guard_level2` | 守护II | Lv2 | 受到伤害后无敌时间增加 |
| `Build_Guard_level3` | 守护III | Lv3 | 周期性充能抵消下次伤害 |

### 活跃构筑类别

`_activeBuildClasses` 决定哪些构筑类别被统计。来源：
1. 装备中武器的 `BuildClass`
2. `_pickedEffectCountByClass` 中有点数的类别

---

## 八、场景配置步骤

1. 将 `scenes/managers/BuildSelectionManager.tscn` 拖入战斗场景
2. 配置 `ThresholdCurve`（可创建 `ScoreThresholdCurve.tres` 或使用内联 Resource）
3. 配置 `EffectPool`（至少 3 个 `BuildEffectDefinition`）
4. 确认玩家节点下有 `BuildController` 子节点
5. 可选：勾选 `DebugTrigger` 测试弹窗
