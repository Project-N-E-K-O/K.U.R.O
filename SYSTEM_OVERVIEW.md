# 项目系统概览

本文件用于快速了解当前核心系统的代码入口、主要职责与典型用法，便于团队成员或 AI 协作者定位资源。

## 1. 效果 / 属性系统

- `scripts/core/GameActor.cs`  
  - 所有玩家/敌人实体的基类，统一持有 `EffectController` 与 `StatProfile`。  
  - `_Ready()` 中自动挂载 `EffectController`，并通过 `ApplyStatProfile()` 将 `CharacterStatProfile` 中的基础属性与常驻效果套用到角色。  
  - 公开 `ApplyEffect(ActorEffect effect)` / `RemoveEffect(string effectId)` 方法供外部添加/移除效果，并在 `TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null)` 中提供 `DamageIntercepted` 事件（`DamageEventArgs` 给出攻击方向/来源），供格挡、减伤等效果在受击前干预伤害；可选的 `attacker` 参数会透传到 `DamageEventBus.Publish(attacker, target, damage)`，用于记录伤害来源供效果（如眩晕/反击）引用。

- `scripts/core/effects/EffectController.cs`、`ActorEffect.cs`、`FreezeEffect.cs`、`SimpleSpeedEffect.cs`、`DirectionalBlockEffect.cs`、`StunEnemiesEffect.cs`  
  - `EffectController` 承担角色效果的生命周期管理：`AddEffect()`、`AddEffectFromScene()`、`RemoveEffect()`、`ClearAll()`。  
  - `ActorEffect` 为 Buff/Debuff 抽象基类，内置持续时间、叠加、Tick 等机制；`FreezeEffect`、`SimpleSpeedEffect` 展示如何扩展。`DirectionalBlockEffect` 通过订阅 `DamageIntercepted` 实现扇形格挡；`StunEnemiesEffect` 则在前方一定角度/距离内批量施加 `FreezeEffect`，常被武器主动技能复用。  
  - 在 Godot 中可将 `ActorEffect` 子场景直接拖入 `EffectController` 或通过 `ItemDefinition`/`StatProfile` 动态实例化。

- `scripts/effects/RestoreHealthEffect.cs`、`RestoreFullHealthEffect.cs`、`IncreaseMaxHealthEffect.cs`、`PeriodicItemGrantEffect.cs`、`TeleportStrikeEffect.cs`、`ExecuteTaggedEnemiesEffect.cs`、`YaomengDualSynergyEffect.cs`、`HealAttackTargetsEffect.cs`  
  - `RestoreHealthEffect` 用于食品等一次性回复：被 `ItemDefinition` 以 `OnConsume` 触发时立即调用 `GameActor.RestoreHealth()`，随后自我移除。  
  - `RestoreFullHealthEffect` 是对全量治疗的封装（直接将 `CurrentHealth` 与 `MaxHealth` 置为同一值），供稀有食物/药剂引用。  
  - `IncreaseMaxHealthEffect` 用于永久提升 `MaxHealth` 并附带少量即时治疗，常被“可永久增益”的食品绑定。  
  - `PeriodicItemGrantEffect` 在 `OnEquip` 等触发后常驻，自带计时器循环向拥有者背包发放指定物品，可通过 `PropertyOverrides` 调整间隔、物品路径、数量与是否需要空槽。  
  - `TeleportStrikeEffect` 在触发瞬间移除碰撞层、将玩家传送至前方距离并对经过路径的敌人造成伤害。  
  - `ExecuteTaggedEnemiesEffect` 目前提供秒杀框架导出属性，后续可接入实际的敌人标签系统。  
  - `YaomengDualSynergyEffect` 监听玩家背包，只有当 `weapon_a0_yaomeng1` 与 `weapon_a0_yaomeng2` 同时存在时才设置 `PlayerWeaponSkillController` 的冷却缩放并同步缩短 `GameActor.AttackCooldown`。  
  - `HealAttackTargetsEffect` 订阅 `DamageEventBus`，当效果拥有者命中目标时为目标回复指定生命值，可通过武器被动技能复用实现“反向治疗”类玩法。

- `scripts/core/stats/CharacterStatProfile.cs`、`StatModifier.cs`  
  - 角色属性系统（效果子类的特例），可在 tscn 指定 `StatProfile`。  
  - `StatModifier` 目前支持对 `max_health`、`attack_damage`、`speed` 等基础数值执行加法或乘法，`CharacterStatProfile` 也可附带一组常驻 `ActorEffect` 场景。

- `scripts/items/effects/ItemEffectEntry.cs`、`scripts/items/ItemDefinition.cs`  
  - 物品可配置 `EffectEntries`，指定触发时机（`OnPickup` / `OnEquip` / `OnConsume` / `OnBreak`）与对应 `ActorEffect` 场景。  
  - `PropertyOverrides` 允许在不复制 tscn 的前提下覆盖效果脚本的导出属性（例如为不同武器设置 SpeedMultiplier），`ItemDefinition.ApplyEffects()` 在实例化后会自动写入这些覆盖值。  
  - `ItemDefinition.ApplyEffects(actor, trigger)` 封装了统一的施加逻辑，`WorldItemEntity`、背包、武器技能等模块在需要时调用即可；`OnBreak` 目前用于耐久耗尽时触发一次性奖励（如 `resources/effects/BreakRewardCoins.tscn` + `GrantCoinsEffect` 为玩家补偿金币，或附加临时 Buff）。

## 2. 物品 / 背包 / 武器系统

- `scripts/items/ItemDefinition.cs` + 相关子目录  
  - 描述物品的基础信息、堆叠规则、标签、属性条目 (`ItemAttributeEntry`)、效果条目 (`ItemEffectEntry`)、默认世界场景路径。  
  - `GetAttributeValues()` / `TryResolveAttribute()` 用于查询属性贡献；`ResolveWorldScenePath()` 约定 tscn 资源位置。

- `scripts/systems/inventory/InventoryContainer.cs`、`InventoryItemStack.cs`、`InventoryContainer` 相关文件  
  - 提供通用背包容器，支持栈叠、信号通知、属性聚合 (通过 `ItemAttributeAccumulator`)。  
  - `InventoryItemStack` 封装单个栈的数量、属性查询、标签判定。

- `scripts/actors/heroes/PlayerInventoryComponent.cs` + `PlayerItemInteractionComponent.cs`  
  - `PlayerInventoryComponent` 负责维护背包指针 `SelectedBackpackSlot`（通过 `ActiveBackpackSlotChanged` 广播），所有拾取/放下/投掷操作都围绕该槽位进行：拾取会尝试把物品直接放入当前槽位，放下/投掷则从该槽位抽出整栈；不再包含任何 Held 槽或额外日志。事件 `ItemPicked` / `ItemRemoved` 仍负责驱动骨骼附件与 UI。  
  - `PlayerItemInteractionComponent` 监听 `take_up`/`put_down`/`throw`，只有当当前选中栏位为空时才允许拾取，当栏位存在物品时才允许放下/投掷；`item_select_left` / `item_select_right` 仅改变指针位置（不会搬运物品或输出日志），直接影响后续操作的目标栏位。如缺少 `Throw` 状态则跳过动画直接执行投掷；`item_use` 则调用 `PlayerInventoryComponent.TryConsumeSelectedItem()` —— 仅持有 `tag_food` 标签的物品会被消耗，触发其 `OnConsume` 效果、扣减耐久/数量并在必要时触发 `OnBreak` 行为。

- `scripts/items/world/WorldItemEntity.cs`、`WorldItemSpawner.cs`  
  - `WorldItemEntity` 继承 `CharacterBody2D`，负责地面物品的触发检测、拾取、属性/效果传播、投掷阻尼。  
  - `WorldItemSpawner.SpawnFromStack()` 将 `InventoryItemStack` 实例化为场景节点；拾取成功会调用 `ApplyItemEffects()` 把配置效果赋给拾取者。
- `scripts/systems/loot/LootDropEntry.cs`、`LootDropTable.cs`、`LootDropSystem.cs` + `resources/loot/*.tres`  
  - 敌人/角色通用的掉落表框架：`GameActor` 现在导出 `LootTable` 属性，Inspector 中引用任意 `LootDropTable` 资源即可。`LootDropEntry` 支持配置掉落概率、数量范围、堆叠次数、散布半径与抛洒力度，`LootDropSystem.SpawnLootForActor()` 会在 `GameActor.OnDeathFinalized()` 中自动调用，生成 `WorldItemEntity` 并应用随机偏移/冲量。  
  - 示例：`resources/loot/SampleEnemyDrops.tres` 将普通掉落（`Supplies_watermelon`）与稀有掉落（`Supplies_pudding`）组合在同一表内，并被 `scenes/actors/enemies/SampleEnemyUnit.tscn` 引用，展示最小化配置即可完成掉落的用法。

- `scripts/items/tags/ItemTagIds.cs`、`ItemAttribute*`  
  - 定义常用标签/属性集合，背包和逻辑可通过标签快速筛选（如食物、武器）。  
  - `ItemAttributeIds.AttackPower` + `ItemAttributeEntry` 可用于配置攻击力加成，`PlayerBasicMeleeAttack` 会通过 `PlayerInventoryComponent.GetSelectedAttributeValue()` 将当前指针槽位的攻击力属性叠加到基础伤害上。
- `scripts/items/durability/*`  
  - 通过 `ItemDurabilityConfig`、`DurabilityBreakBehavior`、`ItemDurabilityState` 描述和跟踪物品/武器的耐久度。`ItemDefinition.DurabilityConfig` 允许在 Inspector 中为任意物品配置最大耐久、破损行为与修复特性，`InventoryItemStack` 自动实例化状态对象并提供 `HasDurability`、`ApplyDurabilityDamage()`（返回是否新近损坏）、`RepairDurability()` 等入口。  
  - 当 `ApplyDurabilityDamage()` 令耐久降至 0 时，可在调用处传入拥有该物品的 `GameActor`，`InventoryItemStack` 会自动调用 `ItemDefinition.ApplyEffects(actor, ItemEffectTrigger.OnBreak)` 触发损坏属性，实现一次性奖励/惩罚效果。

- `resources/items/Supplies_watermelon.tres` + `resources/items/skills/SuppliesWatermelonPassiveSkill.tres`  
  - 多用途物品示例：既打上 `tag_food` 又打上 `tag_weapon`，`ItemAttributeEntry` 将 `attack_power` 设为 -1，配合被动技能 `supplies_watermelon_passive` 调用 `HealAttackTargetsEffect`，使攻击目标回血。  
  - `EffectEntries` 配置 `OnConsume` + `RestoreHealthEffect` 恢复 3 点生命，`ItemDurabilityConfig` 设为 1 点耐久并配置 `DamagePerUse`/`DamagePerHit`，耗尽即消失（`BreakBehavior = Disappear`），展示食品/武器共存的典型写法。
- `scripts/systems/inventory/InventoryItemStack.cs`  
  - 支持通过 `AddRuntimeAttributeValue` / `SetRuntimeAttributeValue` 为单个物品堆叠写入运行时属性（目前用于武器逐渐成长的攻击力加成）；`TryGetAttribute`、`GetAllAttributes` 会把这些临时属性与原始配置一起返回，确保 `PlayerInventoryComponent.GetSelectedAttributeValue()` 等调用能读取最新数值。
- `resources/items/Supplies_cake.tres`  
  - 另一种食品/武器双属性示例：攻击力 3，使用 `RestoreFullHealthEffect` 做到食用即满血，耐久 1 且配置 `DamagePerUse`/`DamagePerHit = 1`，体现与 `item_use` 输入绑定的高阶回复物品。
- `resources/items/Supplies_pudding.tres`  
  - 攻击力 2 的布丁武器/食品：`OnConsume` 触发 `IncreaseMaxHealthEffect` 永久 +1 最大生命并即时回复 1 点血量，同样采用耐久 1 配置，展示可永久增益的食品实现方式。
- `resources/items/Weapon_A0_lengyue.tres` + `resources/items/skills/WeaponLengyuePassiveSkill.tres`  
  - 攻击力 7 的冷月武器，附带被动技能 `weapon_lengyue_passive`。技能引用 `PeriodicItemGrantEffect`，每 15 秒检查背包是否满载，若存在可用槽位则自动放入一个 `Supplies_watermelon`，帮助玩家维持补给。
- `resources/items/Weapon_A0_yaomeng1.tres` + `WeaponYaomeng1ActiveSkill.tres`  
  - 攻击力 11，主动技能使用 `TeleportStrikeEffect` 实现瞬移伤害，冷却 3 秒；同时挂载 `WeaponYaomengSynergyPassiveSkill.tres` 以加入双武器被动联动。
- `resources/items/Weapon_A0_yaomeng2.tres` + `WeaponYaomeng2ActiveSkill.tres`  
  - 攻击力 4，主动技能引用 `ExecuteTaggedEnemiesEffect` 作为秒杀框架，冷却 4 秒；同样附带 `WeaponYaomengSynergyPassiveSkill.tres`，当与壹式同时存在时通过 `YaomengDualSynergyEffect` 将攻击/技能冷却减半。
- `resources/items/Weapon_A0_plate.tres` + `scenes/items/Weapon_A0_plate_WorldItem.tscn` + `scripts/items/world/ThrowableDamageItemEntity.cs`  
  - 轻型瓷盘武器：攻击力 1、耐久 1。该世界实体脚本继承 `WorldItemEntity`，在 `PlayerItemInteractionComponent` 以“投掷”方式抛出后，会在碰撞 `GameActor` 时根据 `ItemAttributeIds.AttackPower` 造成等额伤害并立即销毁，实现“投掷命中造成伤害”的被动。
- `resources/items/Weapon_A0_neiku.tres` + `resources/items/skills/WeaponNeikuPassiveSkill.tres` + `resources/effects/IncrementAttackPowerEffect.tscn`  
  - “内库”武器：初始攻击力 0，耐久无限。被动技能应用 `IncrementAttackPowerEffect`，该效果订阅 `DamageEventBus` 并在玩家使用此武器命中敌人后调用 `InventoryItemStack.AddRuntimeAttributeValue()`，让该武器的 `attack_power` 永久 +1（数据保存在堆叠的运行时属性中，即使暂时卸下也会保留强化）。
- `resources/items/Weapon_A0_manto.tres` + `resources/effects/HealSelfOnHitEffect.tscn`  
  - “披风”武器：基础攻击 1、无限耐久。被动效果在装备时附加 `HealSelfOnHitEffect`，当玩家使用该武器命中敌人后立即回复 1 点生命值，可通过 `HealAmount` 调整回复值。
- `resources/items/Weapon_A0_computer_A.tres` + `resources/items/skills/WeaponComputerAActiveSkill.tres` + `resources/effects/ComputerAAnnihilationEffect.tscn`  
  - 终端·A：基础攻击 5，只有在命中敌人时才消耗这唯一的耐久。主动技能绑定输入 `weapon_skill_computer_a`，瞬间清除全屏“enemies”组目标；累计释放 3 次后通过 `ComputerAAnnihilationEffect` 弹出报错对话框，玩家确认即调用 `SceneTree.Quit()`，模拟“闪退”演出。
- `scripts/effects/HealSelfOnHitEffect.cs`  
  - 通用被动：监听 `DamageEventBus`，若拥有者为攻击方则按次回复自身生命值，适合需要“命中回血”类武器/护符。
- `scripts/effects/ComputerAAnnihilationEffect.cs`  
  - 主动技能效果：枚举 `enemies` 组逐一 `TakeDamage(int.MaxValue)`，同时在拥有者的 `Meta` 上累计使用次数，超过阈值后生成 `AcceptDialog` 警告并在玩家确认/关闭时强制退出游戏。

- 交互/拾取/放下/投掷流程：  
  - 地图物品：`WorldItemEntity` 挂在 tscn 中，`TryTransferToActor()` 只会把物品写入玩家当前选中栏位（若不可用则直接拒绝），随后触发 `PlayerInventoryComponent.ItemPicked` 并按 `ItemDefinition.EffectEntries` 应用拾取效果。  
  - 快捷键：`PlayerItemInteractionComponent` 监听 `put_down` / `throw`，仅当当前栏位存在物品时才会通过 `WorldItemSpawner` 生成实体；`item_select_left` / `item_select_right` 循环调整指针但不会移动物品。  
  - 骨骼绑定：`PlayerItemAttachment` 订阅 `ItemPicked`/`ItemRemoved` 以及 `ActiveBackpackSlotChanged`，始终展示当前指针对应物品，放下/投掷时自动清除。  
- 拾取/投掷动画链路：  
  - `PlayerItemInteractionComponent` 会在 `take_up` 输入时切入 `PlayerPickUpState`，播放 `animations/pickup`（Spine/AnimationPlayer），动画结束后才实际执行拾取。  
  - 投掷流程同理：按下 `throw` 时先切换到 `PlayerThrowState` 播放投掷动画，动画完成后 `TryTriggerThrowAfterAnimation()` 生成并抛出物品。

## 3. 战斗系统

- `scripts/actors/heroes/SamplePlayer.cs`、`scripts/actors/enemies/*.cs`  
  - 玩家/敌人均继承 `GameActor`，围绕 `StateMachine` 与 `AttackArea` 实现攻击、受击、死亡等流程。  
  - 敌人可通过 `EffectController` 应用特殊效果（如 `FreezeEffect`）。
- `scripts/actors/enemies/EnemyC1WaiterA.cs` + `scenes/actors/enemies/Enemy_C1_waiterA.tscn`  
  - C1 侍者敌人：4 点生命值，0.5 秒转身与受击硬直，距离阈值外投掷 `Weapon_A0_plate`（依赖 `WorldItemSpawner` 和 `ThrowableDamageItemEntity`），距离内则触发 1 点伤害的范围击退攻击；冷却、距离、投掷力度等均通过导出属性可调，示范了敌人脚本直接驱动远程/近战双攻逻辑的实现方式。
- `scripts/actors/enemies/EnemyC1WaiterB.cs` + `scenes/actors/enemies/Enemy_C1_waiterB.tscn`  
  - C1 侍者 B：30 点生命值，转身 / 受击各 0.5 秒僵直，依赖 `StateMachine + EnemyAttackController` 调度三段攻击（激光 20%、远程齐射 50%、突刺 30%），并通过 `metadata/guarantee_interval` / `metadata/guarantee_priority` 配置“激光每 4 次、齐射每 3 次必出（冲突时激光优先）”的保底机制。
- `scripts/actors/enemies/attacks/EnemyLaserSweepAttack.cs` + `scenes/actors/enemies/attacks/Laser1Attack.tscn`  
  - “Laser1” 攻击模板：在多组锚点间随机腾空，依序落下 3 段横向激光。每段包含 1.5 秒警示（`Polygon2D` 提示）、1 秒激光判定与可选间隔，期间敌人通过 `DamageIntercepted` 事件获得无敌。锚点、激光尺寸/伤害、段数、前/后摇和位移均可在资源中配置，可直接挂到 `EnemyAttackController` 作为子攻击。
- `scripts/actors/enemies/attacks/EnemyVolleyRangedAttack.cs` + `scenes/actors/enemies/attacks/RangedVolleyAttack.tscn`  
  - 远程齐射模板：可配置齐射次数、每轮射数、射击/齐射间隔以及三种瞄准模式（整段/每轮/每发重新锁定）。支持简单的直接伤害，也可指定 `ItemDefinition` 让敌人投射现有物品（如 `Weapon_A0_plate`），复用 `WorldItemSpawner` 提供的投掷逻辑，适用于构建多段远程攻击。
- `scripts/actors/enemies/attacks/EnemyThrustStrikeAttack.cs` + `scenes/actors/enemies/attacks/ThrustStrikeAttack.tscn`  
  - 简化突刺模板：记录玩家位置后高速突进，抵达后根据 `StrikeDelaySeconds` 延迟判定，最终以常规 `PerformAttack` 输出伤害。可调最小/最大突进距离、冲刺速度与伤害，适合需要“冲到面前→短停→挥击”节奏的敌人。

- `scripts/actors/enemies/attacks/*`、`EnemyAttackController.cs`  
  - 包含敌人的攻击定义与攻势调度逻辑；效果系统可与之协作（如冻结后重置攻击队列）。`EnemyAttackController` 读取子节点的 `metadata/attack_weight`、`metadata/guarantee_interval` 与 `metadata/guarantee_priority`，可在 Inspector 中直接配置概率与“几次攻击后必出”的保底顺序。

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

- 音频：`scripts/audio/AudioManager.cs` + `AudioCue*.cs`  
  - `AudioCue` 资源定义单个音效/BGM 的 AudioStream、音量、音高随机、播放总线等参数；`AudioCueLibrary` 则在 Inspector 中集中维护一组 Cue，运行时可通过 `GetCue(id)` 取出。  
  - `AudioManager` 负责统一播放，内部维护 BGM 播放器与一组可复用的 `AudioStreamPlayer` / `AudioStreamPlayer2D` 池，提供 `PlaySfx(string cueId, Vector2? position)`、`PlayCue(AudioCue cue)`、`PlayBgm()`、`StopBgm(fade)` 等接口；建议作为 Autoload 节点挂在根场景，使任意地方都能调用。  
  - 使用方式：在 Inspector 中给 `AudioManager` 设置 `AudioCueLibrary` 资源，并在资源中配置 CueId → AudioStream 的映射，之后只需要调用 `AudioManager.PlaySfx("player_attack")` 或 `PlayBgm("battle_theme")` 即可；如需 3D/2D 方位感，传入世界坐标即可由 `AudioStreamPlayer2D` 播放。

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
  - `WeaponSkillDefinition` 描述主动/被动技能的动画、伤害倍率、附带效果（复用 `ItemEffectEntry`），通过 `ItemDefinition.WeaponSkillResources` 在 Inspector 中配置引用，再由 `GetWeaponSkillDefinitions()` 进行强类型访问；`ActivationAction` 可与 Project Settings 的输入（如 `weapon_skill_block`、`weapon_skill_stun`）绑定，实现手动触发。  
  - `PlayerInventoryComponent` 在装备/卸下武器时触发 `WeaponEquipped`/`WeaponUnequipped`；`PlayerWeaponSkillController` 监听该事件，加载技能、施加被动效果并为攻击系统提供动画/伤害覆盖、冷却管理与输入触发接口（`TriggerDefaultSkill()` / `TryTriggerActionSkill()`）。  
  - `PlayerBasicMeleeAttack` 调用控制器以使用武器技能的动画与数值，并在攻击过程中触发默认技能。示例技能包括 `resources/items/skills/ExampleSlashSkill.tres`（示例攻击）、`resources/items/skills/WeaponBlockSkill.tres`（配合 `DirectionalBlockEffect.cs` 实现格挡）与 `resources/items/skills/WeaponStunSkill.tres`（配合 `StunEnemiesEffect.cs`，释放前方眩晕冲击），可按需挂载到各武器。

- 工具与日志：`scripts/utils/GameLogger.cs` 等提供调试输出、通用辅助函数。

---

### 使用建议

1. **定位代码**：根据功能模块到对应目录查找（如效果系统集中在 `scripts/core/effects`）。使用 `ItemDefinition`/`CharacterStatProfile` 时，可直接在 Godot Inspector 中拖拽资源。
2. **扩展属性/效果**：新增属性时扩展 `StatModifier`/`ApplyStatModifier`；新增效果时继承 `ActorEffect` 并在物品或角色配置中引用对应 scene。
3. **拾取流程**：地面物品挂 `WorldItemEntity`；玩家通过 `PlayerItemInteractionComponent` 操作背包指针拾取/放下；拾取成功会把栈写入当前选中槽位并触发 `PlayerInventoryComponent` 事件，放下/投掷则从该槽位取出后生成实体。
4. **状态机与动画**：新状态继承已有状态基类，并在相应 `StateMachine` 节点下注册；动画通过 Spine 或 Godot AnimationPlayer 统一驱动，与 `GameActor.FlipFacing()` 保持兼容。

如需更新本概览，请同步维护系统路径与简介，确保团队成员快速了解代码架构。*** End Patch
