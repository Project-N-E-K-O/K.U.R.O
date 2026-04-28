# P2 同伴系统实现计划

本文档用于沉淀 `P2.tscn` 作为主角 `main_character.tscn` 同伴的实现方案，便于后续按阶段开发、回顾和继续扩展。

## 目标

P2 需要具备以下能力：

1. 作为玩家同伴跟随主角并保持漂浮感
2. 在战斗或探索中弹出提示，向玩家传达游戏信息
3. 根据当前游戏状态 `GameState` 做出辅助判断
4. 在满足安全条件时，协助玩家使用道具或触发辅助行为

本阶段重点不是让 P2 变成第二个完整玩家角色，而是先把它做成一个稳定、可观察、可扩展的“支持型同伴系统”。

## 当前项目内已确认的基础

项目里已经存在一些可直接复用的能力：

### 1. 已有 AI 状态采集链路

- `GameStateProvider` 已能采集玩家、敌人、快捷栏和同伴信息
- `GameState` 已有 `companions` 结构
- 这意味着 P2 后续可以被纳入统一状态观测

### 2. 已有 AI 决策框架

- 已存在 `AiDecisionBridge`
- 已存在 `AiDecisionExecutor`
- 但当前执行器主要面向“接管玩家输入”而不是驱动独立同伴

### 3. 已有正式对话系统

- 已存在 `DialogueManager`
- 已存在 `DialogueWindow`
- 适合做剧情对话、NPC 对话、暂停式信息展示

### 4. 已有物品与消耗基础

- `PlayerInventoryComponent` 已有消耗物品入口
- 但当前现成入口更偏向背包选中槽位，不适合直接做“同伴自动智能用道具”

## 已确认的实现约束

这些约束会直接影响 P2 的设计方式：

1. 现有 `DialogueWindow` 会通过 `DialogueManager.ShouldBlockPlayerInput()` 阻断玩家输入
2. 因此战斗中的即时提示不应直接复用正式对话窗口
3. `GameStateProvider` 目前能识别同伴，但更偏向 `GameActor` 或分组查找
4. P2 当前只是一个轻量 `CharacterBody2D` 场景，尚未接入同伴数据接口
5. 自动用道具如果依赖当前“UI 选中槽位”会不稳定，因此应补单独的 support item API

## 推荐总体架构

建议把 P2 实现为“独立同伴节点 + 本地规则脑 + 非阻塞提示 UI + 可选 AI 扩展”的结构。

### 架构分层

#### 1. 表现层：P2 Companion Node

由 `P2.tscn` 承担：

- 跟随玩家
- 漂浮运动
- 朝向同步
- 播放提示气泡或简单特效

这一层只负责看起来像一个同伴，不负责复杂决策。

#### 2. 控制层：P2CompanionController

建议新增脚本：

- `scripts/companions/P2CompanionController.cs`

职责：

1. 绑定玩家引用
2. 计算跟随目标点
3. 做平滑插值与漂浮偏移
4. 维护简单状态：`Idle`、`Follow`、`Assist`、`Talk`

#### 3. 决策层：P2SupportBrain

建议新增脚本：

- `scripts/companions/P2SupportBrain.cs`

职责：

1. 定时从 `GameStateProvider` 拉取 `GameState`
2. 根据规则判断当前是否需要提示或辅助
3. 输出结构化的 `SupportDecision`

第一版建议先不用大模型，直接用规则：

- 玩家低血量时提示恢复
- 敌人过近时提示后撤
- 场上没有敌人时提示拾取物品
- 玩家持续受击时尝试触发辅助行为

#### 4. 执行层：P2SupportExecutor

建议新增脚本：

- `scripts/companions/P2SupportExecutor.cs`

职责：

1. 接收 `SupportDecision`
2. 校验动作是否合法
3. 执行白名单行为
4. 记录执行结果，便于调试

建议只允许以下动作：

- `show_hint`
- `suggest_retreat`
- `suggest_pickup`
- `use_support_item`
- `trigger_support_skill`
- `hold`

#### 5. UI 层：P2HintBubble

建议新增轻量提示 UI：

- `scenes/ui/hud/P2HintBubble.tscn`

用途：

- 显示短句提示
- 自动淡出
- 不阻断玩家输入
- 可挂在 P2 头顶或 HUD 层

不建议在这个用途上直接使用正式 `DialogueWindow`。

## 推荐场景接入方式

### 主角场景内增加锚点

建议在 `main_character.tscn` 中增加一个同伴跟随锚点，例如：

- `CompanionAnchor`

作用：

- 提供 P2 的默认跟随参考位置
- 支持主角左右转向时改变同伴偏移侧
- 后续如果有多个同伴，也可以拓展多个锚点

### 在战斗场景中实例化 P2

建议将 P2 放在实际战斗场景中，而不是写死在主角资源内部。

例如在 `Stage_2.tscn` 中：

- `World/MainCharacter`
- `World/P2`

这样更适合：

- 单独调试 P2
- 控制它的加载和卸载
- 后续为不同关卡配置不同同伴

## GameState 接入方案

### 目标

让 P2 能读懂当前局势，同时也能被 AI 状态系统看见。

### 建议实现

#### 方案 A：让 P2 实现独立同伴状态接口

建议新增：

- `scripts/companions/ICompanionStateSource.cs`

接口可包含：

- `Name`
- `CurrentHp`
- `MaxHp`
- `IsAvailable`
- `GetCompanionRole()`

然后扩展 `GameStateProvider`：

1. 优先识别 `GameActor`
2. 其次识别 `ICompanionStateSource`

这是更稳妥的做法。

#### 方案 B：直接把 P2 做成完整 `GameActor`

不推荐第一阶段这样做。

原因：

- 成本更高
- 会过早引入受伤、碰撞、受击、状态机等复杂度
- 当前目标是支持型同伴，不是第二角色战斗体系

## 提示系统设计

P2 的提示应分成两类：

### 1. 非阻塞提示

用于战斗中即时提醒：

- “血量有点危险”
- “先拉开距离”
- “附近有掉落物”
- “我来帮你恢复一下”

实现方式：

- `P2HintBubble`
- 自动消失
- 不暂停游戏
- 不屏蔽输入
- 增加节流，避免刷屏

### 2. 正式说明对话

用于章节引导、教学说明或重要事件：

- 首次获得能力时
- 首次进入系统时
- 剧情提示时

这类内容才建议走：

- `DialogueManager`
- `DialogueData`
- `DialogueWindow`

## 辅助行为设计

### 第一阶段建议只做“轻辅助”

优先做这些：

1. 提醒玩家当前风险
2. 提醒玩家使用恢复物品
3. 提醒玩家附近可拾取物
4. 触发一个简单辅助技能或特效

### 第二阶段再做“主动辅助”

例如：

1. 自动给玩家恢复
2. 自动触发护盾或减伤辅助
3. 自动标记敌人或给出集火建议

### 第三阶段再做“AI 策略辅助”

例如：

1. 根据敌人数量判断该进攻还是拉扯
2. 根据快捷栏和背包信息建议切换资源
3. 根据 Build 状态给出战术提示

## 道具使用设计

### 当前问题

现有 `PlayerInventoryComponent.TryConsumeSelectedItem()` 更依赖当前背包选中槽位。

这会导致两个问题：

1. P2 的自动辅助逻辑会依赖 UI 当前选中了哪个背包槽位
2. 这不适合作为稳定的 AI / Companion 行为接口

### 推荐补充的接口

建议在 `PlayerInventoryComponent` 中新增明确接口，例如：

1. `TryConsumeBackpackSlot(int slotIndex, GameActor consumer)`
2. `TryConsumeFirstTaggedItem(string tagId, GameActor consumer)`
3. `TryUseBestSupportItem(string supportType, GameActor consumer)`

这样 P2 的辅助行为就可以做到：

- 与 UI 选中状态解耦
- 更安全
- 更容易测试
- 更容易扩展到 AI 输出执行

## 建议的 SupportDecision 结构

即使第一版先用规则，也建议把 P2 的输出收敛成结构化结果。

例如：

```json
{
  "intent": "show_hint",
  "target": "player",
  "item_tag": "healing",
  "urgency": "high",
  "duration": 2.0,
  "reason": "player hp below 30 percent and currently under attack"
}
```

可选字段建议：

- `intent`
- `target`
- `item_tag`
- `urgency`
- `duration`
- `reason`

这样做的好处是：

1. 后续规则输出和 AI 输出可以共用执行器
2. 更方便做调试面板显示
3. 更方便做日志、回放和评估

## 建议的开发顺序

### 阶段一：完成可见的同伴基础

目标：让 P2 真正出现在游戏里，并稳定跟随。

建议任务：

1. 在 `main_character.tscn` 中增加 `CompanionAnchor`
2. 在 `Stage_2.tscn` 中实例化 `P2.tscn`
3. 给 P2 挂 `P2CompanionController.cs`
4. 实现漂浮跟随、平滑插值、朝向同步

### 阶段二：完成非阻塞提示能力

目标：让 P2 可以用不打断玩家操作的方式提示信息。

建议任务：

1. 新增 `P2HintBubble.tscn`
2. 新增提示队列与节流逻辑
3. 先实现手动触发提示
4. 再实现根据规则自动触发提示

### 阶段三：接入 GameState

目标：让 P2 依据当前战况作出简单判断。

建议任务：

1. 新增 `P2SupportBrain.cs`
2. 定时读取 `GameStateProvider.CaptureGameState()`
3. 实现低血、贴脸、无敌人、掉落物等基础规则
4. 将 P2 纳入 `GameState.companions`

### 阶段四：实现辅助执行

目标：让 P2 不只是说，还能做有限的辅助动作。

建议任务：

1. 新增 `P2SupportExecutor.cs`
2. 定义 `SupportDecision`
3. 新增安全白名单执行逻辑
4. 先支持 `show_hint` 与简单辅助技能

### 阶段五：实现道具辅助

目标：让 P2 可以在合法条件下帮玩家使用恢复类或支援类资源。

建议任务：

1. 为 `PlayerInventoryComponent` 新增明确 support item API
2. 定义什么条件下允许自动使用
3. 增加冷却与节流
4. 记录执行结果与失败原因

### 阶段六：接入 AI 结构化输出

目标：让 P2 的战术辅助从规则扩展为 AI + 规则混合。

建议任务：

1. 新增 `P2SupportDecisionBridge`
2. 规定结构化 JSON 输出协议
3. 用本地规则层做最终校验
4. 将模型输出限制在建议层而非直接执行层

## 最适合作为近期切入点的任务

如果希望尽快做出可运行结果，建议先按以下顺序落地：

1. 先把 `P2.tscn` 接进 `Stage_2.tscn`
2. 给主角增加 `CompanionAnchor`
3. 实现 P2 漂浮跟随
4. 新增非阻塞提示气泡
5. 再接入 `GameState` 做简单规则提示

## 后续建议新增的文件

建议新增以下内容：

- `scripts/companions/P2CompanionController.cs`
- `scripts/companions/P2SupportBrain.cs`
- `scripts/companions/P2SupportExecutor.cs`
- `scripts/companions/ICompanionStateSource.cs`
- `scripts/companions/SupportDecision.cs`
- `scenes/ui/hud/P2HintBubble.tscn`

## 总结

P2 同伴系统的正确落地方向，不是直接复用玩家自动战斗控制，而是先建立一条独立的同伴支持链路：

- 先稳定跟随
- 再非阻塞提示
- 再读取 `GameState`
- 再做有限辅助执行
- 最后再引入结构化 AI 决策

这样可以最大程度降低复杂度，同时保证它未来可以继续演进到：

- 战斗提示助手
- 资源辅助助手
- Build 建议助手
- 战术型 AI 同伴
