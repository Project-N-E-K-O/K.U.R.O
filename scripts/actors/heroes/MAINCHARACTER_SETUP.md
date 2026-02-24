# MainCharacter 使用指南

## 概述

`MainCharacter` 是一个与 `StateMachine` 协同工作、集成 `WeaponSkillController` 功能的角色控制器，使用 Spine 动画而不是 AnimationPlayer。

## 功能特性

✅ **已集成功能**：
- ✅ 与 StateMachine 协同工作
- ✅ 集成 WeaponSkillController（伤害倍率、技能效果）
- ✅ 集成 InventoryComponent（攻击力加成）
- ✅ 使用 Spine 动画（通过 SpineController.gd）
- ✅ 可以被敌人检测和攻击
- ✅ 可以攻击敌人并造成伤害

## 场景设置

### 1. 基本节点结构

```
MainCharacter (CharacterBody2D)
├── SpineSprite (SpineSprite) - 挂载 SpineController.gd
├── CollisionShape2D
├── AttackArea (Area2D)
├── StateMachine (Node)
│   ├── Idle (MainCharacterIdleState) 或 (PlayerIdleState)
│   ├── Walk (MainCharacterWalkState) 或 (PlayerWalkState)
│   ├── Run (MainCharacterRunState) 或 (PlayerRunState)
│   ├── Attack (MainCharacterAttackState) 或 (PlayerAttackState)
│   │   └── BasicMeleeAttack (MainCharacterBasicMeleeAttack) 或 (PlayerBasicMeleeAttack)
│   ├── Hit (PlayerHitState)
│   ├── Dying (PlayerDyingState)
│   ├── PickUp (PlayerPickUpState)
│   └── Throw (PlayerThrowState)
├── Inventory (PlayerInventoryComponent)
├── WeaponSkillController (PlayerWeaponSkillController)
├── ItemInteraction (PlayerItemInteractionComponent)
└── ItemAttachment (PlayerItemAttachment)
```

### 2. 状态机配置

**选项 A：使用 MainCharacter 专用状态（推荐）**

在 StateMachine 下使用以下状态脚本：
- `Idle` → `MainCharacterIdleState.cs`
- `Walk` → `MainCharacterWalkState.cs`
- `Run` → `MainCharacterRunState.cs`
- `Attack` → `MainCharacterAttackState.cs`
  - `BasicMeleeAttack` → `MainCharacterBasicMeleeAttack.cs`

**选项 B：使用通用 PlayerState（自动适配）**

使用原有的 `PlayerState` 系列状态，它们会自动检测 `MainCharacter` 并使用 Spine 动画：
- `Idle` → `PlayerIdleState.cs`
- `Walk` → `PlayerWalkState.cs`
- `Run` → `PlayerRunState.cs`
- `Attack` → `PlayerAttackState.cs`
  - `BasicMeleeAttack` → `PlayerBasicMeleeAttack.cs` 或 `MainCharacterBasicMeleeAttack.cs`

### 3. Spine 动画设置

1. **SpineSprite 节点**：
   - 挂载 `scripts/controllers/SpineController.gd` 脚本
   - 配置 Spine 资源（.atlas, .spine-json）

2. **MainCharacter 脚本属性**：
   - `IdleAnimationName`: Spine 动画名称（默认: "idle"）
   - `WalkAnimationName`: Spine 动画名称（默认: "walk"）
   - `RunAnimationName`: Spine 动画名称（默认: "run"）
   - `AttackAnimationName`: Spine 动画名称（默认: "attack"）

### 4. 攻击配置

**使用 MainCharacterBasicMeleeAttack（推荐）**：
- 自动集成 WeaponSkillController
- 自动应用伤害倍率
- 自动使用武器技能动画

**或使用 PlayerBasicMeleeAttack**：
- 需要手动配置 WeaponSkillController
- 会使用 AnimationPlayer（如果存在）

## 工作原理

### 状态机流程

1. **输入处理**：StateMachine 的 `_UnhandledInput()` 处理输入
2. **状态切换**：根据输入和条件切换到相应状态
3. **动画播放**：
   - `PlayerState.PlayAnimation()` 自动检测是否是 `MainCharacter`
   - 如果是，调用 `MainCharacter.PlaySpineAnimation()`
   - 否则，使用 `AnimationPlayer`

### 攻击流程

1. **输入检测**：状态机检测到攻击输入
2. **状态切换**：切换到 `Attack` 状态
3. **攻击模板**：`MainCharacterAttackState` 启动 `MainCharacterBasicMeleeAttack`
4. **动画播放**：`MainCharacterBasicMeleeAttack.OnAttackStarted()` 播放 Spine 攻击动画
5. **伤害计算**：
   - 基础伤害 = `AttackDamage`
   - + InventoryComponent 攻击力加成
   - × WeaponSkillController 伤害倍率
6. **攻击检测**：`MainCharacter.PerformAttackCheck()` 检测并造成伤害

## 与 SamplePlayer 的区别

| 特性 | SamplePlayer | MainCharacter |
|------|-------------|---------------|
| 动画系统 | AnimationPlayer | Spine (SpineController.gd) |
| 状态机 | 必需 | 必需 |
| WeaponSkillController | 可选 | 已集成 |
| 移动控制 | 状态机 | 状态机 |
| 攻击控制 | 状态机 | 状态机 |

## 注意事项

1. **SpineController.gd**：必须挂载在 SpineSprite 节点上
2. **动画名称**：确保 Spine 资源中的动画名称与 `MainCharacter` 的属性匹配
3. **状态脚本**：推荐使用 `MainCharacter*State` 系列状态，或确保使用 `MainCharacterBasicMeleeAttack`
4. **组件依赖**：
   - `WeaponSkillController` 需要 `InventoryComponent`
   - `ItemAttachment` 需要 `InventoryComponent`
   - `ItemInteraction` 需要 `InventoryComponent`

## 故障排除

### 问题：动画不播放
- 检查 SpineSprite 是否挂载了 `SpineController.gd`
- 检查动画名称是否正确
- 检查 `SpineSpritePath` 是否正确

### 问题：攻击没有伤害
- 检查 `AttackArea` 是否设置
- 检查 `WeaponSkillController` 是否正确初始化
- 查看控制台日志确认伤害计算

### 问题：状态机不工作
- 检查 StateMachine 节点是否存在
- 检查 InitialState 是否设置
- 检查状态脚本是否正确附加
