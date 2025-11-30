# 项目系统概览

本文件用于快速了解当前核心系统的代码入口、主要职责与典型用法，便于团队成员或 AI 协作者定位资源。

## 1. 效果 / 属性系统

- `scripts/core/GameActor.cs`  
  - 所有玩家/敌人实体的基类，统一持有 `EffectController` 与 `StatProfile`。  
  - `_Ready()` 中自动挂载 `EffectController`，并通过 `ApplyStatProfile()` 将 `CharacterStatProfile` 中的基础属性与常驻效果套用到角色。  
  - 公开 `ApplyEffect(ActorEffect effect)` / `RemoveEffect(string effectId)` 方法供外部添加/移除效果。

- `scripts/core/effects/EffectController.cs`、`ActorEffect.cs`、`FreezeEffect.cs`、`SimpleSpeedEffect.cs`  
  - `EffectController` 承担角色效果的生命周期管理：`AddEffect()`、`AddEffectFromScene()`、`RemoveEffect()`、`ClearAll()`。  
  - `ActorEffect` 为 Buff/Debuff 抽象基类，内置持续时间、叠加、Tick 等机制；`FreezeEffect`、`SimpleSpeedEffect` 展示如何扩展。  
  - 在 Godot 中可将 `ActorEffect` 子场景直接拖入 `EffectController` 或通过 `ItemDefinition`/`StatProfile` 动态实例化。

- `scripts/core/stats/CharacterStatProfile.cs`、`StatModifier.cs`  
  - 角色属性系统（效果子类的特例），可在 tscn 指定 `StatProfile`。  
  - `StatModifier` 目前支持对 `max_health`、`attack_damage`、`speed` 等基础数值执行加法或乘法，`CharacterStatProfile` 也可附带一组常驻 `ActorEffect` 场景。

- `scripts/items/effects/ItemEffectEntry.cs`、`scripts/items/ItemDefinition.cs`  
  - 物品可配置 `EffectEntries`，指定触发时机（如 `OnPickup`）与对应 `ActorEffect` 场景。  
  - 在 `WorldItemEntity` 或其他系统中，通过 `ItemDefinition.GetEffectEntries(trigger)` 迭代并实例化效果即可。

## 2. 物品 / 背包 / 武器系统

- `scripts/items/ItemDefinition.cs` + 相关子目录  
  - 描述物品的基础信息、堆叠规则、标签、属性条目 (`ItemAttributeEntry`)、效果条目 (`ItemEffectEntry`)、默认世界场景路径。  
  - `GetAttributeValues()` / `TryResolveAttribute()` 用于查询属性贡献；`ResolveWorldScenePath()` 约定 tscn 资源位置。

- `scripts/systems/inventory/InventoryContainer.cs`、`InventoryItemStack.cs`、`InventoryContainer` 相关文件  
  - 提供通用背包容器，支持栈叠、信号通知、属性聚合 (通过 `ItemAttributeAccumulator`)。  
  - `InventoryItemStack` 封装单个栈的数量、属性查询、标签判定。

- `scripts/actors/heroes/PlayerInventoryComponent.cs` + `PlayerItemInteractionComponent.cs`  
  - `PlayerInventoryComponent` 除了管理背包/主武器槽外，还会自动创建唯一的 “Held Item” 槽（`SpecialInventorySlotIds.CurrentHeldItem`），并通过 `TryAssignHeldItem()`、`TakeHeldItemStack()`、`TryReturnHeldItem()` 在拾取/放下之间搬运栈；事件 `ItemPicked` / `ItemRemoved` 仍同步通知骨骼附件与 UI。  
  - `PlayerItemInteractionComponent` 监听 `take_up`/`put_down`/`throw`，只有在 Held 槽为空时才允许拾取，只有槽内有物品时才允许放下/投掷；真正生成地面实体时直接从 Held 槽取出栈并交给 `WorldItemSpawner`，投掷则在动画完成后施加初速度。

- `scripts/items/world/WorldItemEntity.cs`、`WorldItemSpawner.cs`  
  - `WorldItemEntity` 继承 `CharacterBody2D`，负责地面物品的触发检测、拾取、属性/效果传播、投掷阻尼。  
  - `WorldItemSpawner.SpawnFromStack()` 将 `InventoryItemStack` 实例化为场景节点；拾取成功会调用 `ApplyItemEffects()` 把配置效果赋给拾取者。

- `scripts/items/tags/ItemTagIds.cs`、`ItemAttribute*`  
  - 定义常用标签/属性集合，背包和逻辑可通过标签快速筛选（如食物、武器）。

- 交互/拾取/放下/投掷流程：  
  - 地图物品：`WorldItemEntity` 挂在 tscn 中，`TryTransferToActor()` 只会把 `InventoryItemStack` 写入玩家的 Held 槽（若槽已占用则直接拒绝），随后触发 `PlayerInventoryComponent.ItemPicked` 并按 `ItemDefinition.EffectEntries` 应用拾取效果。  
  - 快捷键：`PlayerItemInteractionComponent` 监听 `put_down` / `throw`，仅当 Held 槽存在物品时才会通过 `WorldItemSpawner` 生成实体；槽为空时直接提示不可用。投掷流程依旧会在动画完成后来到真正释放阶段。  
  - 骨骼绑定：`PlayerItemAttachment` 订阅 `ItemPicked`/`ItemRemoved`，将最新拾取物品图标附着在指定骨骼或节点上，放下/投掷时自动清除。  
  - 快捷栏：`QuickSlotBar` 订阅 `InventoryContainer.InventoryChanged`，实时展示前几格物品与数量，便于 Debug 与 UI 集成。
- 拾取/投掷动画链路：  
  - `PlayerItemInteractionComponent` 会在 `take_up` 输入时切入 `PlayerPickUpState`，播放 `animations/pickup`（Spine/AnimationPlayer），动画结束后才实际执行拾取。  
  - 投掷流程同理：按下 `throw` 时先切换到 `PlayerThrowState` 播放投掷动画，动画完成后 `TryTriggerThrowAfterAnimation()` 生成并抛出物品。

## 3. 战斗系统

- `scripts/actors/heroes/SamplePlayer.cs`、`scripts/actors/enemies/*.cs`  
  - 玩家/敌人均继承 `GameActor`，围绕 `StateMachine` 与 `AttackArea` 实现攻击、受击、死亡等流程。  
  - 敌人可通过 `EffectController` 应用特殊效果（如 `FreezeEffect`）。

- `scripts/actors/enemies/attacks/*`、`EnemyAttackController.cs`  
  - 包含敌人的攻击定义与攻势调度逻辑；效果系统可与之协作（如冻结后重置攻击队列）。

- 投掷动画链路  
  - `PlayerItemInteractionComponent` 的 `ThrowStateName` 默认为 `PlayerThrowState`。按下投掷键会先切换状态，播放 `animations/throw`，动画结束后由 `TryTriggerThrowAfterAnimation()` 实际生成/投掷物品。  
  - `PlayerThrowState` 驻留在玩家状态机中，负责播放动画、等待结束并通知交互组件执行投掷，再回到 Idle。

## 4. 动画与骨骼系统

- Spine 相关资源位于 `addons/spine` 与 `animations/spine`，收拢骨骼动画。  
  - 玩家/敌人场景 (`scenes/actors/*`) 中一般含 `SpineCharacter`，配合 `GameActor` 的 `FlipFacing()` 统一处理左右翻转。  
  - 自定义插槽脚本 `addons/spine/插槽.gd` 辅助控制骨骼附件。

## 5. 状态机系统

- `scripts/systems/fsm/StateMachine.cs` 及 `scripts/actors/heroes/states/*`、`scripts/actors/enemies/states/*`  
  - `StateMachine` 负责切换、驱动角色状态；`GameActor` 在 `_Ready()` 中自动查找并初始化它。  
  - 每个状态作为独立节点存在，处理输入/物理/动画逻辑，示例：`PlayerIdleState`, `PlayerAttackState`, `PlayerHitState` 等。

## 6. 其他关键系统

- UI：`scripts/ui/*` 与 `scenes/ui/*` 管理 HUD、菜单、窗口。  
  - `UIManager`、`BattleHUD` 等类负责显示角色状态、战斗指令。

- 管理器与控制器：`scripts/controllers/*`、`scripts/managers/*`  
  - 如 `EnemySpawnController` 负责敌人生成；`CameraFollow`、`UIManager` 管理场景级别逻辑。

- 地图互动体系：`scripts/core/interactions/*`、`scripts/actors/npc/*.cs`  
  - `IInteractable` 定义统一交互接口；`InteractableArea` 可挂在场景中检测玩家进入、可选按键触发并支持高亮。  
  - `BaseInteractable` 封装交互开关、次数限制与对话触发，可在 Inspector 绑定 `DialogueSequence` 与实现了 `IDialoguePlayer` 的节点。  
  - `scripts/core/interactions/dialogue/*` 提供对白资源结构（`DialogueLine` / `DialogueSequence`）与 `IDialoguePlayer` 接口，方便接入 UI。  
  - 示例 `NpcDialogueInteractable`（`scripts/actors/npc/NpcDialogueInteractable.cs`）展示如何构建可对话 NPC；`scenes/ExampleBattle.tscn` 已实例化 `FriendlyNPC` 供测试。

- 武器技能系统：`scripts/items/weapons/*`、`scripts/actors/heroes/PlayerWeaponSkillController.cs`  
  - `WeaponSkillDefinition` 描述主动/被动技能的动画、伤害倍率、附带效果（复用 `ItemEffectEntry`），通过 `ItemDefinition.WeaponSkillResources` 在 Inspector 中配置引用，再由 `GetWeaponSkillDefinitions()` 进行强类型访问。  
  - `PlayerInventoryComponent` 在装备/卸下武器时触发 `WeaponEquipped`/`WeaponUnequipped`；`PlayerWeaponSkillController` 监听该事件，加载技能、施加被动效果并为攻击系统提供动画/伤害覆盖与技能触发接口。  
  - `PlayerBasicMeleeAttack` 调用控制器以使用武器技能的动画与数值，并在攻击过程中触发默认技能。示例武器参见 `resources/items/ExamplePotion.tres` + `resources/items/skills/ExampleSlashSkill.tres`。

- 工具与日志：`scripts/utils/GameLogger.cs` 等提供调试输出、通用辅助函数。

---

### 使用建议

1. **定位代码**：根据功能模块到对应目录查找（如效果系统集中在 `scripts/core/effects`）。使用 `ItemDefinition`/`CharacterStatProfile` 时，可直接在 Godot Inspector 中拖拽资源。
2. **扩展属性/效果**：新增属性时扩展 `StatModifier`/`ApplyStatModifier`；新增效果时继承 `ActorEffect` 并在物品或角色配置中引用对应 scene。
3. **拾取流程**：地面物品挂 `WorldItemEntity`；玩家通过 `PlayerItemInteractionComponent` 操作 Held 槽拾取/放下；拾取成功会把栈写入 Held 槽并触发 `PlayerInventoryComponent` 事件，放下/投掷则先清空 Held 槽再生成实体。
4. **状态机与动画**：新状态继承已有状态基类，并在相应 `StateMachine` 节点下注册；动画通过 Spine 或 Godot AnimationPlayer 统一驱动，与 `GameActor.FlipFacing()` 保持兼容。

如需更新本概览，请同步维护系统路径与简介，确保团队成员快速了解代码架构。*** End Patch
