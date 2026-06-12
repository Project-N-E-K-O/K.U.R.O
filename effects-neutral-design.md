# 伤害效果中性化设计规范

## 核心原则

**伤害本身不关心目标身份。** `GameActor.TakeDamage()` 对所有 Actor 通用，效果脚本不应在逻辑中硬编码"敌人"或"玩家"判断。目标是谁、打谁，应由调用方或配置决定，而非写死在效果内部。

## 两种模式

### 模式 A：命中触发型（通过 DamageEventBus）

效果监听 `DamageEventBus.OnDamageResolved(attacker, target, ...)`，天然目标无关。

```
宿主打谁 → 效果就挂谁身上，不需要知道对方阵营
```

**规则：**
- 检查 `attacker == Actor`（确保是宿主打的）
- 对 `target` 执行效果
- 变量名用 `target`，禁止用 `enemy` / `player`

**示例：** `DotBleedEffect`、`SlowOnHitEffect`、`KnockbackOnAttackEffect`

### 模式 B：区域扫描型（Area2D / 扫描查找）

效果需要自己找到范围内的目标，通过**碰撞层掩码**配置影响对象，不写死组名。

```
[Export(PropertyHint.Layers2DPhysics)] uint TargetCollisionMask
```

| 配置 | 值 |
|------|-----|
| 只影响敌人 | `2`（Layer 2） |
| 只影响玩家 | `4`（Layer 3） |
| 同时影响两者 | `6`（2\|4） |

**禁止：**
- `GetNodesInGroup("enemies")` — 硬编码组名
- `private const uint EnemiesLayerMask` — 硬编码层

**示例：** `SlowHitAreaEffect`

## 命名规范

| 旧（禁止） | 新 |
|-----------|-----|
| `enemy`、`_enemy` | `target`、`actor` |
| `capturedEnemy` | `capturedTarget` |
| `EnemiesLayerMask` | `TargetCollisionMask` |
| 注释中的"敌人" | "目标" |

## 何时需要阵营判断

当前项目没有 PvP / 友伤 / 队友 NPC，**不需要阵营系统**。

未来如果有以下需求，再加 `Faction` 枚举到 `GameActor`：
- 敌人使用玩家效果（如 BOSS 放黑洞）
- 队友 NPC 系统
- PvP 模式

加的时候只需：

```csharp
// scripts/core/GameActor.cs
public enum FactionType { Neutral, Player, Enemy }
[Export] public FactionType Faction { get; set; }
```

效果中过滤一行即可：`if (target.Faction == Actor.Faction) return;`

**不要提前添加。**

## 参考对照

| 脚本 | 状态 | 备注 |
|------|------|------|
| `DotBleedEffect` | 已中性化 | DamageEventBus，变量名已改为 target |
| `SlowOnHitEffect` | 已中性化 | DamageEventBus |
| `KnockbackOnAttackEffect` | 已中性化 | DamageEventBus |
| `SlowHitAreaEffect` | 已中性化 | 碰撞掩码，新建统一脚本 |
| `BoomDmgEffect` | 已中性化 | 双开关，径向检测场景仍需 bool |
| `BlackHoleEffect` | 待迁移 | 仍写死 `GetNodesInGroup("enemies")` |
| `SpikeAttackEffect` | 待废弃 | 被 SlowHitAreaEffect 替代 |
| `TeapotBrokenEffect` | 待废弃 | 被 SlowHitAreaEffect 替代 |
| `SoundWaveEffect` | 待迁移 | 仍写死 `GetNodesInGroup("enemies")` |
