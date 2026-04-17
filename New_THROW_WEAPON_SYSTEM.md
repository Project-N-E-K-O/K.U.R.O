# 投掷武器系统详细文档

## 📋 目录
1. [系统概述](#系统概述)
2. [核心组件](#核心组件)
3. [投掷流程时间轴](#投掷流程时间轴)
4. [碰撞检测机制](#碰撞检测机制)
5. [伤害系统](#伤害系统)
6. [耐久度系统](#耐久度系统)
7. [配置参数速查](#配置参数速查)
8. [常见问题排查](#常见问题排查)

---

## 系统概述

投掷武器系统是 K.U.R.O 中用于处理**可投掷物品**（如炸弹、标枪等）的完整解决方案。实现了从投掷、飞行、碰撞检测到伤害计算的整个流程。

### 核心特性
- ✅ **爆发式投掷**：物品以指定速度和方向投出
- ✅ **分阶段运动**：飞行 → 下降 → 停止
- ✅ **精确碰撞检测**：支持 Hitbox Area + RigidBody BodyEntered 双重检测
- ✅ **速度阈值伤害**：只有达到最小速度才能造成伤害
- ✅ **击退效果**：对目标应用击退力
- ✅ **耐久度消耗**：每次命中消耗耐久度，耗尽后销毁
- ✅ **防重复伤害**：同一物品同一碰撞检测周期内不会对同一目标多次伤害

---

## 核心组件

### 1. RigidBodyWorldItemEntity（主类）
**位置**：`/scripts/items/world/RigidBodyWorldItemEntity.cs`

核心职责：
- 管理物品的生命周期（拾取、投掷、销毁）
- 控制投掷时的物理和碰撞行为
- 执行伤害检测和处理

### 2. RigidBody2D（物理体）
承载场景中物品的物理属性：
- 承受重力、碰撞和速度计算
- 投掷时速度被设置为投出的向量
- 碰撞层和遮罩在投掷前后需要动态切换

### 3. Hitbox Area2D（伤害检测区）
投掷时用于检测**是否与敌人碰撞**：
- 碰撞层：0（不占用碰撞层）
- 碰撞遮罩：1u << 1（检测第2层 = 敌人層）
- 信号：BodyEntered（当有敌人进入时触发）

### 4. GrabArea Area2D（拾取范围）
拾取物品时使用：
- 碰撞层：1u << 1（第2层 = 物品層）
- 碰撞遮罩：1（检测第1層 = 玩家層）
- 会在物品被拾取时禁用

---

## 投掷流程时间轴

### 📍 阶段 1：投掷初始化 [T=0ms]

```
玩家调用 ApplyThrowImpulse(Vector2 velocity)
	↓
设置 _rigidBody.LinearVelocity = velocity
设置 _rigidBody.GravityScale = 0f （禁用重力，直线飞行）
设置 _isThrown = true
激活伤害检测：_impactArmed = true
	↓
应用投掷碰撞设置：
  - CollisionLayer = ThrowCollisionLayer (1u << 2 = 第3层)
  - CollisionMask = ThrowCollisionMask (通常为0，不检测任何物体)
  ↓
启动飞行计时器：_flightTimer = 0
```

**关键代码**：
```csharp
public virtual void ApplyThrowImpulse(Vector2 velocity)
{
	_rigidBody.Sleeping = false;
	_rigidBody.Set("freeze", false);
	_rigidBody.GravityScale = 0.0f;  // 禁用重力
	_inFlight = true;
	_flightTimer = 0.0;
	_rigidBody.LinearVelocity = velocity;
	
	if (velocity.LengthSquared() > 0.01f)
	{
		_impactArmed = true;         // 激活伤害检测
		_isThrown = true;
		ApplyThrowCollisionSettings();  // 应用碰撞设置
	}
}
```

---

### 📍 阶段 2：飞行中 [T=0 ~ FlightDurationSeconds]

**持续时间**：由 `FlightDurationSeconds` 控制（默认 0.4 秒）

在 `_PhysicsProcess` 中每帧执行：

```csharp
if (_inFlight)
{
	_flightTimer += delta;
	var vel = _rigidBody.LinearVelocity;
	
	// 保持水平飞行（垂直速度归零）
	_rigidBody.LinearVelocity = new Vector2(vel.X, 0);
	
	if (_flightTimer >= FlightDurationSeconds)
	{
		// 飞行时间结束，进入下降阶段
		_inFlight = false;
		_rigidBody.GravityScale = _initialGravityScale;  // 恢复重力
		// 应用下降速度...
	}
}
```

**物理行为**：
- 保持初始的水平速度 `velocity.X`
- 强制垂直速度为 0（不受重力影响）
- 物品呈直线飞行

**碰撞情况**：
- 碰撞遮罩为 0，不检测任何物体（穿过所有障碍）
- 但 Hitbox Area 仍在监控，可以检测敌人

---

### 📍 阶段 3：下降中 [T > FlightDurationSeconds]

飞行结束，物品开始下降：

```csharp
_inFlight = false;
_rigidBody.GravityScale = _initialGravityScale;  // 恢复重力
var vel = _rigidBody.LinearVelocity;

// 应用下降速度
_rigidBody.LinearVelocity = new Vector2(
	vel.X * DropHorizontalDamping,  // 水平速度衰减 (默认×0.6)
	DropVerticalSpeed  // 垂直速度 (默认240 像素/秒，向下)
);

_isDropping = true;
_dropStartY = _rigidBody.GlobalPosition.Y;  // 记录下降起点
_refreezePending = true;  // 等待物品停止
```

**物理行为**：
- 水平速度衰减（`DropHorizontalDamping × 60% = 0.6`）
- 垂直向下 240 像素/秒
- 受重力影响继续加速下降

**停止条件**：
```csharp
if (_isDropping)
{
	var currentY = _rigidBody.GlobalPosition.Y;
	if (currentY - _dropStartY >= DropLimitDistance)  // 默认64像素
	{
		// 触及地面，冻结物体
		_rigidBody.Set("freeze", true);
		_rigidBody.LinearVelocity = Vector2.Zero;
		_impactArmed = false;  // 停止伤害检测
		RestoreRigidBodyCollision();  // 恢复原始碰撞设置
	}
}
```

---

### 📍 阶段 4：停止状态 [T > DropLimitDistance]

物品已落地或速度过低：

```csharp
_rigidBody.Set("freeze", true);          // 完全冻结
_rigidBody.LinearVelocity = Vector2.Zero;
_impactArmed = false;                     // 禁用伤害检测
RestoreRigidBodyCollision();              // 恢复原始碰撞设置
_isThrown = false;
```

此时物品可以被玩家再次拾取。

---

## 碰撞检测机制

### 🎯 两层检测策略

为了确保稳定的碰撞检测，系统使用 **Hitbox Area + RigidBody BodyEntered 双重检测**：

#### 检测层 1：Hitbox Area2D (推荐 ⭐⭐⭐⭐⭐)

```csharp
private void OnHitboxBodyEntered(Node2D body)
{
	if (!_impactArmed) return;  // 只在飞行中检测
	
	if (body is GameActor actor)
	{
		if (actor == LastDroppedBy) return;  // 防止伤害投掷者
		if (_hitActors.Contains(actor)) return;  // 防止重复伤害
		
		var speed = _rigidBody.LinearVelocity.Length();
		if (speed >= MinDamageVelocity)  // 检查速度阈值
		{
			TryDealImpactDamage(actor, _rigidBody.LinearVelocity);
		}
	}
}
```

**优点**：
- 不受 RigidBody 碰撞遮罩影响
- 能穿透其他物体只检测敌人
- 更可靠

**配置**：
```csharp
_hitboxArea.CollisionLayer = 0;            // 不占用碰撞层
_hitboxArea.CollisionMask = 1u << 1;       // 检测第2层（敌人）
_hitboxArea.Monitoring = true;
_hitboxArea.Monitorable = false;
```

#### 检测层 2：RigidBody BodyEntered (备选)

如果 Hitbox 损毁或禁用，使用 RigidBody2D 的碰撞事件：

```csharp
private void OnRigidBodyEntered(Node body)
{
	// 逻辑与 Hitbox 相同
}
```

**缺点**：
- 受 CollisionMask 影响（投掷时 mask=0，无法检测）
- 需要额外的物理体碰撞事件

---

### 📊 碰撞配置对比

| 阶段 | CollisionLayer | CollisionMask | 用途 | 备注 |
|------|---|---|---|---|
| **拾取状态** | 1u << 1 (2) | 1 | 被玩家检测到 | 处于世界中 |
| **投掷中** | 1u << 2 (4) | 0 | 穿过障碍 | 靠 Hitbox 检测伤害 |
| **停止后** | _initialLayer | _initialMask | 恢复原状 | 可再次拾取 |

---

## 伤害系统

### 🔴 伤害计算流程

```
1. 物品以速度 velocity 被投出
		 ↓
2. 飞行中与敌人 Hitbox 碰撞
		 ↓
3. 检查速度是否 >= MinDamageVelocity
		 ↓
4. 计算伤害 damage = ThrowDamage（导出参数）
		 ↓
5. 调用 target.TakeDamage(damage, origin, attacker)
		 ↓
6. 应用击退：target.Velocity += knockbackDirection × KnockbackForce
		 ↓
7. 若 StopOnHit=true，物品立即停止
```

### 关键方法

```csharp
private bool TryDealImpactDamage(GameActor target, Vector2 impactVelocity)
{
	int damage = Mathf.Max(1, Mathf.RoundToInt(ThrowDamage));
	
	// 造成伤害
	target.TakeDamage(damage, GlobalPosition, LastDroppedBy);
	_hitActors.Add(target);  // 记录已命中
	
	// 消耗耐久度
	if (ConsumeDurabilityOnHit && MaxDurability > 0)
	{
		ConsumeDurability(1);
	}
	
	// 应用击退
	if (KnockbackForce > 0)
	{
		var knockbackDirection = (target.GlobalPosition - GlobalPosition).Normalized();
		var knockbackVelocity = knockbackDirection * KnockbackForce;
		target.Velocity += knockbackVelocity;
	}
	
	// 若需要，停止物品
	if (StopOnHit)
	{
		StopItemMovement();
	}
	
	return true;
}
```

### ⚠️ 速度阈值

只有当投掷物品的速度 **≥ MinDamageVelocity** 时才能造成伤害：

```csharp
if (speed >= MinDamageVelocity)  // 默认 300 像素/秒
{
	TryDealImpactDamage(actor, velocity);
}
```

**意义**：
- 防止轻微接触造成伤害
- 符合物理直觉（速度越快伤害越大）
- 可制造"判定感"

---

## 耐久度系统

### 📦 耐久度生命周期

```
物品创建时
	↓
MaxDurability > 0 ？
	├─ 是 → _currentDurability = MaxDurability（有限耐久）
	└─ 否 → _currentDurability = -1（无限耐久）
		 ↓
每次命中时
	├─ ConsumeDurabilityOnHit = true ？
	│   ├─ 是 → ConsumeDurability(1)
	│   └─ 否 → 跳过
	│       ↓
	│   _currentDurability -= 1
	│       ↓
	│   _currentDurability <= 0 ？
	│   ├─ 是 → DestroyItem()
	│   └─ 否 → 继续飞行
	└─ 无限耐久 → 始终存活
```

### 销毁逻辑

```csharp
private void DestroyItem()
{
	_isDestroying = true;
	
	// 1. 禁用伤害检测和碰撞
	_impactArmed = false;
	_hitboxArea?.SetDeferred(Area2D.PropertyName.Monitoring, false);
	DisableGrabArea();
	
	// 2. 停止移动
	_rigidBody.LinearVelocity = Vector2.Zero;
	_rigidBody.Set("freeze", true);
	RestoreRigidBodyCollision();
	
	// 3. 播放销毁动画（如果存在）
	PlayDestructionAnimation();
}
```

### 获取耐久度信息

```csharp
// 获取当前耐久度（整数）
int current = _currentDurability;

// 获取耐久度百分比（0.0 - 1.0，用于UI显示）
float percent = MaxDurability > 0 ? (float)_currentDurability / MaxDurability : 1.0f;

// 判断是否有限耐久
bool isLimited = MaxDurability > 0;
```

---

## 配置参数速查

### 🔧 Physics（物理）

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FlightDurationSeconds` | double | 0.4 | 飞行时长（秒） |
| `DropLimitDistance` | float | 64 | 下降距离限制（像素） |
| `DropVerticalSpeed` | float | 240 | 下降时垂直速度 |
| `DropHorizontalDamping` | float | 0.6 | 下降时水平速度衰减系数 |
| `ThrowCollisionLayer` | uint | 1u << 2 (4) | 投掷时碰撞层 |
| `ThrowCollisionMask` | uint | 0 | 投掷时碰撞遮罩 |

### ⚔️ Combat（战斗）

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ThrowDamage` | float | 4 | 伤害数值 |
| `MinDamageVelocity` | float | 300 | 造成伤害的最小速度 |
| `KnockbackForce` | float | 200 | 击退力度 |
| `StopOnHit` | bool | false | 命中敌人后是否停止 |

### 🛡️ Durability（耐久度）

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MaxDurability` | int | 0 | 最大耐久度（0=无限） |
| `ConsumeDurabilityOnHit` | bool | true | 命中时是否消耗耐久 |
| `DestructionAnimationName` | string | destroy | 销毁动画名 |
| `DestructionAnimationDuration` | float | 0.5 | 销毁动画时长（秒） |

---

## 实际配置示例

### 💣 炸弹（有限耐久，一击即破）

```
[场景导出]
MaxDurability = 1
ThrowDamage = 20
MinDamageVelocity = 200
KnockbackForce = 300
StopOnHit = true
ConsumeDurabilityOnHit = true
DestructionAnimationName = "explode"
```

### 🗡️ 标枪（无限耐久，多次伤害）

```
[场景导出]
MaxDurability = 0  // 无限
ThrowDamage = 8
MinDamageVelocity = 350
KnockbackForce = 100
StopOnHit = false  // 穿过敌人继续飞行
ConsumeDurabilityOnHit = false  // 不消耗
```

### 🎯 可回收道具（限时耐久）

```
[场景导出]
MaxDurability = 5
ThrowDamage = 2
MinDamageVelocity = 250
KnockbackForce = 50
StopOnHit = false
ConsumeDurabilityOnHit = true
```

---

## 常见问题排查

### ❌ 问题 1：投掷物品无法伤害敌人

**症状**：投掷物品与敌人碰撞但没有造成伤害

**排查步骤**：

1. **检查速度阈值**
   ```csharp
   // 在 OnHitboxBodyEntered 或 OnRigidBodyEntered 中添加日志
   GD.Print($"物品速度: {_rigidBody.LinearVelocity.Length()}, 阈值: {MinDamageVelocity}");
   ```
   - 如果速度 < MinDamageVelocity，增加投掷速度或降低阈值

2. **检查 _impactArmed 状态**
   ```csharp
   GD.Print($"伤害检测激活: {_impactArmed}");
   ```
   - 如果为 false，可能投掷还未初始化或已停止
   - 确保 ApplyThrowImpulse 被调用

3. **检查敌人碰撞层**
   - 敌人必须在第 2 层（1u << 1）
   - Hitbox 的 CollisionMask 必须包含第 2 层

4. **检查是否重复命中**
   ```csharp
   GD.Print($"已命中敌人: {_hitActors.Count}");
   ```
   - 同一物品同一敌人只能造成一次伤害
   - 如需多次伤害，检查 `_hitActors.Clear()` 时机

---

### ❌ 问题 2：物品与地形相互作用

**症状**：投掷物品穿过地形，或被地形阻挡

**原因**：
- 投掷中 `ThrowCollisionMask = 0`，所以不会与任何物体碰撞
- 这是**设计行为**，允许投掷物品穿过障碍直达敌人

**调整**：

如果需要物品与地形碰撞，修改投掷配置：

```csharp
// 改为检测地形层（假设地形在第 0 层）
ThrowCollisionMask = 1;  // 检测第 1 层
```

但这会导致物品在碰到地形时停止，而无法穿越到敌人。

---

### ❌ 问题 3：碰撞设置在投掷中被重置

**症状**：投掷中物品突然与其他物体相互作用

**排查**：

系统已有三重保险防止这种情况：

1. **_PhysicsProcess 中持续验证**
   ```csharp
   if (_isThrown)
   {
	   if (currentLayer != ThrowCollisionLayer || currentMask != ThrowCollisionMask)
	   {
		   GD.Print("碰撞设置被改变，正在修复...");
		   _rigidBody.CollisionLayer = ThrowCollisionLayer;
		   _rigidBody.CollisionMask = ThrowCollisionMask;
	   }
   }
   ```

2. **CallDeferred 延迟设置**
   ```csharp
   CallDeferred(MethodName.ApplyThrowCollisionSettingsDeferred);
   ```

3. **CreateTimer 异步验证**
   ```csharp
   GetTree().CreateTimer(0.0).Timeout += () => { /* 验证 */ };
   ```

如果仍有问题，检查是否有其他代码修改了物品的碰撞设置。

---

### ❌ 问题 4：物品投掷后立即停止

**症状**：ApplyThrowImpulse 后物品没有移动

**排查**：

1. **检查 RigidBody2D 的 Freeze 状态**
   ```csharp
   GD.Print($"Frozen: {_rigidBody.Get("freeze")}");
   ```

2. **检查初始重力**
   ```csharp
   GD.Print($"GravityScale: {_rigidBody.GravityScale}");
   ```
   - 如果 _initialGravityScale 很高，可能立即下降
   - 检查 ResolveRigidBody 方法是否正确读取

3. **检查投掷速度**
   ```csharp
   if (velocity.LengthSquared() <= 0.01f)
   {
	   // 速度太小，无法投掷
	   GD.Print("投掷速度不足");
	   return;
   }
   ```

---

### ❌ 问题 5：耐久度未正确消耗

**症状**：物品命中敌人但耐久度没有减少

**排查**：

1. **检查 MaxDurability**
   ```csharp
   if (MaxDurability <= 0)
   {
	   // 无限耐久，跳过消耗
	   return;
   }
   ```

2. **检查 ConsumeDurabilityOnHit**
   ```csharp
   if (!ConsumeDurabilityOnHit)
   {
	   // 禁用消耗
	   return;
   }
   ```

3. **检查伤害是否真的造成**
   ```csharp
   // 在 TryDealImpactDamage 中添加
   GD.Print($"造成伤害: {damage}, 耐久度: {_currentDurability}");
   ```

---

## 🔍 调试技巧

### 启用详细日志

```csharp
// 在 ApplyThrowImpulse 后
GD.Print($"[投掷] 速度={velocity.Length()}, 飞行时长={FlightDurationSeconds}");

// 在 _PhysicsProcess 中
if (_inFlight)
{
	GD.Print($"[飞行] 时间={_flightTimer:F2}s, 位置={_rigidBody.GlobalPosition}");
}

// 在 OnHitboxBodyEntered 中
GD.Print($"[碰撞] 速度={speed}, 敌人={body.Name}, 伤害={damage}");
```

### 可视化调试

在场景中添加 DebugDraw：

```csharp
public override void _Draw()
{
	if (!EnableDebug) return;
	
	// 绘制飞行路径预测
	if (_inFlight && _rigidBody != null)
	{
		var velocity = _rigidBody.LinearVelocity;
		var predictedEndPos = GlobalPosition + velocity * (float)(FlightDurationSeconds - _flightTimer);
		DrawLine(GlobalPosition, predictedEndPos, Colors.Red);
	}
}
```

---

## 📊 完整执行流程图

```
玩家按下投掷键
	↓
计算投掷方向和速度
	↓
调用 ApplyThrowImpulse(velocity)
	├─ 设置 LinearVelocity
	├─ 禁用重力 GravityScale = 0
	├─ 激活伤害检测 _impactArmed = true
	└─ 应用投掷碰撞设置 (layer=4, mask=0)
	↓
[飞行阶段] _inFlight = true (0 ~ 0.4s)
	├─ 每帧保持水平速度，垂直速度 = 0
	├─ Hitbox 监控敌人碰撞
	│   └─ 若碰撞 → TryDealImpactDamage() → 消耗耐久度
	└─ 时间到 → 进入下降
	↓
[下降阶段] _isDropping = true
	├─ 恢复重力 GravityScale = _initialGravityScale
	├─ 水平速度衰减 (×0.6)
	├─ 垂直速度 = 240
	├─ 监控下降距离
	│   └─ 若 >= 64px → 进入停止
	└─ 继续伤害检测
	↓
[停止阶段] freeze = true
	├─ 禁用伤害检测 _impactArmed = false
	├─ 恢复原始碰撞设置
	├─ 玩家可再次拾取
	└─ 若需要销毁，播放动画后 QueueFree()
```

---

## 📚 相关文件参考

- **主实现**：`/scripts/items/world/RigidBodyWorldItemEntity.cs`
- **敌人检测**：`/scripts/actors/enemies/SampleEnemy.cs#IsEnemyInPlayerAttackRange`
- **玩家投掷**：需要查看 `PlayerItemAttachment.cs` 中的投掷触发逻辑
- **物品定义**：`/data/ItemDefinition.cs`
- **伤害系统**：`/scripts/actors/core/GameActor.cs#TakeDamage`

---

## 📝 版本历史

| 版本 | 更新日期 | 内容 |
|------|---------|------|
| v1.0 | 2026-04-16 | 初版完成，详细说明投掷系统全流程 |
