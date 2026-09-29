# 效果统一规范

## 核心原则

1. **效果不关心目标身份。** 变量名用 `target`/`actor`，禁止用 `enemy`/`player`。目标是谁由调用方或配置决定，不写死在效果内部。
2. **所有外部效果通过统一入口作用于 GameActor。GameActor 自身决定接受还是拒绝。**

```
效果（伤害/击退/眩晕/...）
  → 统一入口（GameActor 公开方法）
	→ GameActor 内部守门（CanBeAffected / ActiveImmunities）
	  → 执行
```

---

## 一、三道守门：按优先级

### 1. CanBeAffected(effect) — 角色级条件免疫

```csharp
// GameActor.cs
public virtual bool CanBeAffected(ActorEffect? effect) => true;
```

- `effect != null`：效果系统调用（`ApplyEffect`）
- `effect == null`：直接伤害调用（`TakeDamage`）
- 子类覆写实现"非眩晕无敌"等逻辑

### 2. ActiveImmunities — 类型级永久免疫

```csharp
public ImmunityFlags ActiveImmunities { get; set; }
```

| Flag | 阻塞内容 |
|------|---------|
| `Stun` | FreezeEffect 施加 |
| `ForcedMovement` | 所有击退/位移 |
| `SpeedSlow` | 减速效果 |
| `WarmupSuperArmor` | 预热阶段霸体：Warmup 期间受伤害不进入受击硬直 |
| `ActiveSuperArmor` | 生效阶段霸体：Active 期间受伤害不进入受击硬直 |
| `RecoverySuperArmor` | 恢复阶段霸体：Recovery 期间受伤害不进入受击硬直 |
| `ThrowableDamage` | 免疫投掷武器伤害（ThrowableDirectAttack + ThrowImpact） |
| `NonThrowableDamage` | 免疫所有非投掷伤害（仅 Throwable 来源可穿透） |

在效果入口统一检查，不依赖效果内部自行判断。

### 3. IsDead / IsDeathSequenceActive — 生死门

`TakeDamage` 和 `ApplyEffect` 入口已内置，效果无需重复检查。

---

## 二、两种效果模式

### 模式 A：命中触发型（DamageEventBus）

效果监听 `DamageEventBus.OnDamageResolved(attacker, target, ...)`，天然目标无关。

```
宿主打谁 → 效果就挂谁身上，不需要知道对方阵营
```

**规则：**
- 检查 `attacker == Actor`（确保是宿主打的）
- 对 `target` 执行效果
- **变量名用 `target`，禁止用 `enemy` / `player`**

**示例：** `DotBleedEffect`、`SlowOnHitEffect`、`KnockbackOnAttackEffect`

### 模式 B：区域扫描型（IntersectShape / Area2D 信号）

效果通过物理引擎发现目标。**禁止 `GetNodesInGroup`，统一用 `IntersectShape` 或 `Area2D` 信号。**

```csharp
// 同步形状查询
var query = new PhysicsShapeQueryParameters2D
{
	Shape = shape,
	Transform = shape.GlobalTransform,
	CollisionMask = TargetCollisionMask,     // 导出配置，不写死
	CollideWithAreas = true,
	CollideWithBodies = false
};
spaceState.IntersectShape(query);

// 或信号驱动
area.BodyEntered += OnBodyEntered;
```

碰撞掩码通过 `[Export(PropertyHint.Layers2DPhysics)] uint TargetCollisionMask` 配置：

| 影响对象 | 值 |
|---------|-----|
| 只影响敌人 | `2`（Layer 2） |
| 只影响玩家 | `4`（Layer 3） |
| 同时影响两者 | `6`（2\|4） |

### 迁移清单（仍用 GetNodesInGroup 的效果）

| 效果 | 当前方式 | 应改为 |
|------|---------|--------|
| BoomDmgEffect | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` + `CircleShape2D(Radius)` |
| DiscoFlashStunEffect | `GetNodesInGroup` + `DistanceSquaredTo` | `IntersectShape` |
| BlackHoleEffect 吸引 | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` |
| SoundWaveEffect | `GetNodesInGroup` + `DistanceTo` | `IntersectShape` |
| TeleportStrikeEffect | `GetNodesInGroup` + `DistanceToSegment` | `IntersectShape` |

### 当前实施难点：WorldItem 的 IntersectShape 检测

以上效果迁移至 `IntersectShape` 时，WorldItem 阵营无法被正确检测。根因是 WorldItem 的碰撞结构不统一：

| WorldItem 类型 | 基类 | 碰撞方式 | IntersectShape 问题 |
|---|---|---|---|
| WorldItemEntity | `CharacterBody2D` | `BodyCollisionLayer = 0`（身体碰撞层关闭），通过 `TriggerArea`（Area2D, layer 2）交互 | Body 查询找不到（层 0），Area 查询需沿树找 `world_items` 组 |
| RigidBodyWorldItemEntity | `Node2D` | Area2D 子节点 | 非物理体，Body 查询完全找不到 |
| DestructibleObject | `StaticBody2D` 子节点 | `StaticBody2D` + CollisionShape2D | 找到的是子节点 `StaticBody2D`，需沿树向上找 `world_items` 组成员 |

**待解决**：是否接受"用 `GetNodesInGroup("world_items")` 处理 WorldItem，其余阵营走 `IntersectShape`"的混合方案，还是为 WorldItem 统一碰撞层后全部迁移。

### WorldItem 阵营的现状（2026-08 定稿）

`TargetableFactions.WorldItem` 的实际语义 = **只对 DestructibleObject（可破坏物）生效**：
- `DestructibleObject`：有 `TakeDamage(float)` 接口（HP/摧毁），StaticBody2D 在层 1 → 可被伤害
- `WorldItemEntity` / `RigidBodyWorldItemEntity`（掉落物）：无 `TakeDamage` → `ResolveDamageReceiver` 解析不出伤害接收者，**免疫伤害**（掉落物不该被打碎）

因此配置 `WorldItem` flag 时实际命中目标只有可破坏物；掉落物即使物理层被检测到也会被伤害结算拒绝。

---

### 场景根节点类型规范

| 效果类型 | 根节点 | 继承 | 应用路径 |
|---------|--------|------|---------|
| 逻辑效果（眩晕/DOT/Buff/击退） | `Node` | `ActorEffect` | `ApplyEffect` → EffectController |
| 世界生成效果（爆炸/投射物/区域） | `Node2D` | `Node2D` 或子类 | `SpawnSingleEffect` → world |
| **全局反馈（震屏/全屏闪烁/时间缩放）** | `Node2D` | `Node2D` | `SpawnSingleEffect` → world（不需要定位，落点无所谓） |

- `Node` 根的效果不需要世界坐标，挂载在 GameActor 下由 `EffectController` 管理生命周期
- `Node2D` 根的效果需要 `GlobalPosition` 定位，走 `SpawnSingleEffect` 的世界生成路径
- 全局反馈虽然不需要坐标，但**必须与"世界效果"同族（`Node2D` 自管理）**：它天然是"一次性指令"，
  挂在 `ActorEffect` 下会吃到 `EffectController` 的 **EffectId 去重**（同一时间窗内多次震屏只生效一次）
  与 **Actor 生命周期绑定**（施放者的生死不该取消已安排的反馈）。参考实现：`CameraShakeEffect`
- **禁止混用**：`ActorEffect` 子类用 `Node2D` 根会导致坐标失效，`Node2D` 脚本用 `Node` 根会导致 Godot 类型错误

### 世界效果规范（2026-08 已实施，取代原 WorldActorEffect 规划）

`ActorEffect` 继承 `Node`，需要世界坐标的效果曾通过 `IWorldSpawnable` 接口手动管理位置（绕过 C# 单继承限制的临时方案）。**已彻底解决**：世界效果直接继承 `Node2D`（不经过 ActorEffect 体系），统一模式：

| 基类 | 根节点 | 生命周期 | 用途 |
|---|---|---|---|
| `ActorEffect` | `Node` | `EffectController`（EffectId 去重、Tick、Actor 绑定） | 纯逻辑 Buff/Debuff：眩晕/减速/DOT/属性修改 |
| `Node2D` + `IAttackerProvider` | `Node2D` | 自管理（`_Ready`/`_Process` Duration 到期销毁 + `_ExitTree` 兜底清理） | 世界效果：区域伤害/爆炸/落点效果 |

**已改造效果**（从 `ActorEffect + IWorldSpawnable` → `Node2D`）：

| 效果 | 说明 |
|---|---|
| BriefcaseOpenEffect | 公文包落点生成护盾/攻击模块 |
| EmojiBoomEffect | 表情炸弹落点爆炸 |
| StunEnemiesEffect | 区域眩晕（烟雾弹） |
| BlackHoleEffect | 重力手雷黑洞 |
| SlowHitAreaEffect | 地面减速/伤害区域（茶壶/尖刺） |

**改造收益**：
- 脱离 `EffectController.AddEffect` 的 EffectId 去重——多投掷各自独立生成（此前固定 EffectId 的烟雾弹眩晕区只生成一个）
- 脱离 Actor 生命周期绑定——玩家死亡/场景切换不再连带销毁已生成的世界效果
- 不再需要 `IWorldSpawnable` 接口 hack（Node2D 分支用 `GlobalPosition = 落点` 定位）

**应用路径**：投掷效果走 `SpawnThrowDestroyEffects` / `EnemyWaiterAThrowProjectile` 的 Node2D 分支（`is ActorEffect` 判断失败自动落入），`IAttackerProvider.Attacker` 注入投掷者。

---

## 三、伤害：只走 DamageDispatcher

### 禁止

```csharp
actor.TakeDamage(damage);  // 绕过阵营过滤和自伤保护
```

### 统一使用

```csharp
// 有 Area2D
DamageDispatcher.DealDamageFromArea(area, damage, attacker, factions, allowSelfDamage);

// 有具体目标
bool dealt = DamageDispatcher.DealDamage(target, damage, origin, attacker, source, factions, allowSelfDamage);
if (!dealt) return;  // 阵营不匹配时不继续
```

详见 `DAMAGE_DISPATCHER_SYSTEM.md`。

---

## 四、击退：只走 GameActor.ApplyKnockback / ApplyKnockbackDisplacement

### 两个入口（都在 `GameActor` 上，都带 `ForcedMovement` 免疫门）

| API | 语义 | 典型场合 |
|-----|------|---------|
| `ApplyKnockback(dir, speed)` | **速度制**：直接给一次速度，由受击状态自行衰减 | 光束/接触这类"一下弹开"的小位移 |
| `ApplyKnockbackDisplacement(dir, distance, duration)` | **位移制**：指定"推多远、多久推完"，受击状态在时长内匀减速滑完 | 特效 / 爆炸 / 子弹等需要**可预期距离**的场合（当前主流写法，20+ 处） |

**两者都不得用 `Velocity = ` 直接写**：那会绕过免疫门与受击状态。

### 玩家侧：放行由 API 内部处理，调用点不用管

对 `MainCharacter` 的击退**不能事后判断 `IsHitInvincible`**：这一下命中本身就会开无敌帧，
打完之后再看会把"刚命中的这一击"也判成已无敌 → 表现就是"只推得动敌人、推不动玩家"（§八 的反例同源）。

**这条规则已经收进 API 内部**：`GameActor.ApplyKnockbackDisplacement` / `ApplyKnockback` 会先过
`AllowsKnockback()` 这个虚钩子，`MainCharacter` 把它覆写成"消费 `ConsumePendingHitKnockback()`"
（由刚才那次 `TakeDamage` 置位；无敌帧 / 护盾完全格挡 / IgnoreHitStateOnDamage 时为 false）。
所以**攻击方一律直接调用即可**：

```csharp
actor.ApplyKnockbackDisplacement(dir, distance, duration);   // 玩家侧该不该弹，API 自己判断
```

需要"知道这一下到底弹没弹"的场合（例如 BoomDmgEffect 要顺带做冰冻外部位移、或要给调用方返回布尔），
用**只读**的 `MainCharacter.HasPendingHitKnockback` 先看一眼——**不要自己调 `ConsumePendingHitKnockback()`**，
那会和内部那道门重复消费、把击退吃掉。

### 新增：统一击退入口

```csharp
// GameActor.cs
public void ApplyKnockback(Vector2 direction, float speed)
{
	if (ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement)) return;
	Velocity = direction * speed;
}
```

### 迁移：所有击退点改为调用此方法

| 当前 | 改为 |
|------|------|
| `enemy.Velocity = beamDir * knockSpeed` | `enemy.ApplyKnockback(beamDir, knockSpeed)` |
| `actor.Velocity = knockbackVelocity` | `actor.ApplyKnockback(direction, speed)` |
| `mainCharacter.Velocity = knockbackVelocity` | `mainCharacter.ApplyKnockback(dir, speed)`（玩家走覆写） |

`KnockbackDriver`（`KnockbackOnAttackEffect` 内部）保留，因其需要 `MoveAndCollide` 做碰撞响应，但 `Attach` 入口也加 `ForcedMovement` 检查。

> 生死门例外：致死一击本身仍会放行击退。血量归零时死亡标记立即置位，但受击反馈（Hit 状态：后仰动画 + 击退位移）
> 尚未走完，此窗口内 `IsDyingDeferred == true` 放行击退写入；Hit 结束转入 Dying 后门即关闭。

---

## 五、效果应用：只走 GameActor.ApplyEffect

```csharp
// GameActor.cs
public void ApplyEffect(ActorEffect effect)
{
	if (!CanBeAffected(effect)) return;
	EffectController?.AddEffect(effect);
}
```

已内置 `CanBeAffected` 守门。施加效果的代码直接调 `ApplyEffect`，不需要自己判断目标状态。

---

## 六、命名规范

| 禁止 | 正确 |
|------|------|
| `enemy`、`_enemy` | `target`、`actor` |
| `capturedEnemy` | `capturedTarget` |
| `EnemiesLayerMask` | `TargetCollisionMask` |
| 注释中的"敌人" | "目标" |

---

## 七、GameActor 公开 API 总览

| 方法 | 用途 | 内部守门 |
|------|------|---------|
| `TakeDamage(damage, ...)` | 造成伤害 | IsDead, ActiveImmunities |
| `ApplyEffect(effect)` | 施加效果（眩晕/减速/Buff...） | CanBeAffected |
| `ApplyKnockback(dir, speed)` | 击退（速度制） | ForcedMovement |
| `ApplyKnockbackDisplacement(dir, distance, duration)` | 击退（位移制，主流） | ForcedMovement |
| `ConsumePendingHitKnockback()` | 玩家侧：这一下命中是否允许被击退（须在 `TakeDamage` 之后立刻调用） | 无敌帧 / 护盾格挡 / IgnoreHitStateOnDamage |
| `CanBeAffected(effect?)` | 子类覆写，条件免疫 | — |

---

## 八、阵营过滤（已实施：TargetableFactions Flags 模式）

项目的"可命中谁"需求已通过 `TargetableFactions`（Flags 枚举，[DamageDispatcher.cs](scripts/core/DamageDispatcher.cs)）实现，而非 `FactionType` 枚举：

```csharp
public enum TargetableFactions
{
	None = 0,
	Player = 1 << 0,
	Enemy = 1 << 1,
	WorldItem = 1 << 2,
	All = Player | Enemy | WorldItem
}
```

每个造成伤害的效果必须配置目标阵营与自伤开关：

```csharp
[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
public TargetableFactions TargetableFactions = TargetableFactions.Enemy;
[Export] public bool AllowSelfDamage { get; set; } = false;   // 自伤保护（投掷物不打投掷者）
```

**统一过滤入口**：伤害一律走 `DamageDispatcher.DealDamage(target, damage, origin, attacker, source, TargetableFactions, AllowSelfDamage)`——不要在效果内部自写阵营判断。

- 效果示例：`ECoreAttackEffect` / `BoomerangAttackEffect`（Enemy）、敌人投掷物（Player | WorldItem）
- 自伤保护：`DamageDispatcher.BelongsToActor(target, attacker)`
- 状态免疫：`ImmunityFlags`（Stun / ForcedMovement / SpeedSlow 等）按类型过滤状态施加，与阵营正交

**不采用 `FactionType` 枚举方案**：项目无 PvP / 友伤 / 队友 NPC，`TargetableFactions` Flags 已覆盖"可命中谁"的全部需求；若未来需要玩家打玩家/队友，在现有 Flags 上扩展即可，无需引入新枚举。

### 目标筛选：免疫目标不参与瞄准、也不构成遮挡（2026-09）

自动选目标 / "首个目标截断"类效果（追踪弹幕、浮游炮、非穿透光束）在挑目标时必须跳过
`CanBeAffected(null) == false` 的敌人（rogueAI 本体 / 磁铁臂 / netAdmin 这类"常态免伤"的部件）——
否则**最近的那个免疫目标会把攻击全部吃掉**：弹幕瞄准偏航、光束被它截断、浮游炮对它空放。

```csharp
// 最近目标扫描：RotatingCube.ResolveAimTarget / BunnySwardFloatingCannon.FindNearestEnemy
if (!enemy.CanBeAffected(null)) continue;

// 首个目标截断：LaserBeamPlayerWeapon.AddTarget —— 跳过它 = 不进目标表也不设截断距离
if (receiver is GameActor immune && !immune.CanBeAffected(null)) return;
```

**只认 `CanBeAffected` 这一道闸，不要拿"当前能不能掉血"当筛子。**
`IsHitInvincible`（玩家受伤无敌帧）、`DamageIntercepted`（延迟损伤 / 护盾 / 方向格挡）都是
"伤害照常到达、只是被改写或延后"——这些效果**必须**继续被选中并收到伤害事件
（`MachineDelayedDamageEffect` 正是靠收到事件记账、buff 结束时统一结算）。
反例写法：`if (enemy.IsHitInvincible) continue;`、`if (enemy.CurrentHealth <= 0) continue;`

**不要靠关闭 HitArea 来实现"无视"**：HitArea 同时是效果与受击通道——netAdmin 正是靠它接收
`drone_stun_` 前缀的眩晕（`CanBeAffected` 只放行该前缀）。"不可伤害" ≠ "不可交互"。

- 落点参考：`RotatingCube.ResolveAimTarget()`、`LaserBeamPlayerWeapon.AddTarget()`、`BunnySwardFloatingCannon.FindNearestEnemy()`
- 不适用：敌→玩家方向的效果（`LaserBeamA` / `EnemyFloatingCannon` 等）——玩家从不覆写 `CanBeAffected`（恒 `true`），加筛选是空转

---

## 九、新效果开发检查清单

- [ ] 目标发现：`IntersectShape` 或 `Area2D` 信号，不用 `GetNodesInGroup`
- [ ] 伤害：`DamageDispatcher.DealDamage`，不用 `actor.TakeDamage`
- [ ] 击退：`actor.ApplyKnockback` / `ApplyKnockbackDisplacement`，不用 `actor.Velocity =`；对玩家必须先过 `ConsumePendingHitKnockback()` 门（见第四节），**不得事后判断 `IsHitInvincible`**
- [ ] 效果施加：`actor.ApplyEffect`，不用手动判断免疫
- [ ] `TargetCollisionMask` 导出配置，不写死组名/层号
- [ ] 变量名用 `target`/`actor`，不硬编码 `enemy`/`player`
- [ ] 效果内部不判断目标阵营/身份
- [ ] 世界效果（区域/爆炸/落点）：根节点 `Node2D`，继承 `Node2D` + `IAttackerProvider`（不继承 `ActorEffect`），`Duration` 自管理、`_ExitTree` 兜底清理
- [ ] 阵营过滤：配置 `TargetableFactions` + `AllowSelfDamage`，不写死目标
- [ ] 自动选目标 / 首个目标截断：跳过 `CanBeAffected(null) == false` 的目标（见第八节），不用无敌帧 / 伤害拦截 / 血量当筛选条件
- [ ] 生成在目标身上的视觉：`target.GetVisualAnchorWorld()`，不用 `target.GlobalPosition` / 写死 y 偏移
- [ ] 数值加成效果：用基础值（`BaseMaxHealth` 等）做叠加基数，不用当前属性值（防场景切换快照污染重复叠加，见第十一节）

---

## 十、目标视觉锚点（VisualEffectMarker2D）

生成在目标**身上**的视觉（dot 特效、buff 视觉等）不能直接使用目标原点（`GameActor.GlobalPosition` = 脚底/几何中心）——高个子敌人（如 b1_fat）的视觉中心在身体中上部，原点定位会让特效出现在脚底。

### 规则

- **视觉定位一律走 `GameActor.GetVisualAnchorWorld()`**，优先级：
  1. `VisualEffectPosition/VisualEffectMarker2D`（显式视觉挂点，**按名字+类型查找**，与场景内其它 Marker2D 不混淆；可拖拽 Marker 调位置）
  2. 目标身上的 `VisualEffectArea`（Area2D，挂 `Sprite2D/VisualEffectArea`，其 `CollisionShape2D.GlobalPosition` 即视觉中心；存量回退）
  3. 回退 `HitArea` 中心
  4. 回退目标原点
- **挂载一律用工具 `Kuros.Fx.VisualAnchorAttach.Attach(fx, actor)`**，它封装了两条纪律：
  - **排序**：挂 `VisualEffectPosition` 容器、根坐标保持原点 —— y_sort 容器中节点 Y 即排序键，根摆在锚点坐标会被排到角色后（遮挡）；
  - **偏移**：锚点偏移由内部子节点 Position 承载 —— 不要用 `AddChild` 重挂承载偏移（重挂默认保留全局变换，会反向补偿抵消偏移）。
- **敌人场景**：体型使视觉中心偏离原点的敌人，需配置 `Sprite2D2/VisualEffectPosition` + 子 `VisualEffectMarker2D`（视觉中心 offset 配在 Marker 的 position）。
  **2026-09 已完成迁移**：14 个敌人场景从 `VisualEffectArea`(Area2D+CollisionShape2D) 全部换为 Marker 结构（形状资源同步清理），与 `main_character` 挂点体例一致；Area 方案保留为解析回退，仅存量/特殊场景使用。
  动机：Area2D 是为物理查询设计的（默认参与每帧宽相位），纯锚点用途属浪费且拖动 gizmo 易被误认成判定区；Marker2D 运行时零物理/零绘制。
- **不适用**：
  - 死亡特效——留在目标原点（倒地位置，见 `EnemyDyingState`）
  - 瞄准类打击点（光束/投掷瞄准 `HitArea` 中心）——与伤害判定一致，不随视觉锚点

### 已迁移

- `DotBurnEffect`（灼烧火焰跟随视觉锚点）
- `DotBleedEffect`（流血视觉挂视觉锚点）
- `P2CompanionController.SpawnActionEffect`（P2 护盾/治疗特效——经 `VisualAnchorAttach` 挂载，根留原点避免 y-sort 遮挡）
- `ThrowShieldTransformEffect`（A_009 护盾——推动 Marker 挂点优先级与 `VisualAnchorAttach` 工具收敛；上述调用点随之全体升级）
- 14 个敌人场景挂点体例迁移：`VisualEffectArea` → `VisualEffectPosition/VisualEffectMarker2D`（`DotBurnEffect`/`DotBleedEffect`/`P2CompanionController.SpawnActionEffect` 在敌人目标上自动受益）

---

## 十一、构筑效果数值叠加规范（基础值基数）

### 问题案例（2026-08 修复）

`NormalMaxHealthBoostEffect`（血量上限提升）跨场景**无限叠加**：

```
场景切换 → 玩家重建 → 过渡快照把 MaxHealth 恢复为"含加成值"（145）
→ RestoreBuildState 恢复效果 → 新效果 OnApply → _originalMaxHealth = Actor.MaxHealth（145，已被快照污染）
→ +25 = 170 → 下次场景 195 → 220 …… 无限
```

### 规则

1. **数值加成效果的叠加基数 = 基础值，禁止用当前属性值**
   - `GameActor.BaseMaxHealth`（`_Ready` 记录初始值，不含效果加成）——MaxHealth 类效果用它做基数
   - 禁止 `Actor.MaxHealth` / 当前属性值做基数——过渡快照恢复的"含加成值"会导致重复叠加
2. **`OnApply` 必须幂等**——玩家重建/场景切换时效果重新挂载（走 **OnApply**，不走 `OnStackRefreshed`）——OnApply 计算结果只依赖基础值/固定数组值（tier 数组），不依赖"当前属性值"
3. **`OnStackRefreshed` 的 tier+1 升级语义正确**（玩家选卡叠层触发）——但它**不是场景切换的路径**（场景切换玩家重建 → OnApply）——不要用它承载幂等责任
4. **Modifier 系统幂等**（`SetStatModifier` 按 EffectId 覆盖）✓——可安全用于数值修改（Machine 系效果均走此路径，无叠加问题）
5. **场景切换流程**：`RestoreInventoryTransit`（血量快照恢复含加成值）→ `RestoreBuildState`（效果重挂 OnApply）——效果基数必须能在"快照污染后"仍正确 → 只用基础值

### 已修复

- `NormalMaxHealthBoostEffect`：基数改用 `Actor.BaseMaxHealth` ✓（`GameActor.BaseMaxHealth` 于 `_Ready` 记录）
