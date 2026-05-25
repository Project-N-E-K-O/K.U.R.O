# 敌人攻击模板指南

本指南说明如何继承 `EnemyAttackTemplate` 来快速实现新的敌人攻击技能。

---

## 目录

1. [核心概念](#核心概念)
2. [攻击流程](#攻击流程)
3. [基础实现](#基础实现)
4. [进阶模式](#进阶模式)
5. [检测区域攻击](#检测区域攻击detection-based-attacks)
6. [冷却机制（PostCooldown）](#冷却机制postcooldown)
7. [控制器冷却协调](#控制器冷却协调controller-coordination)
8. [常见问题](#常见问题)

---

## 核心概念

### 攻击模板继承链

```
EnemyAttackTemplate（基础模板）
	↓ 继承
SimpleAttack（如EnemySimpleMeleeAttack）
ComplexAttack（如EnemyDashSlashAttack、EnemyChargeGrabAttack）
```

### 关键属性

| 分类 | 属性 | 说明 |
|------|------|------|
| **Meta** | `AttackName` | 调试用的攻击名称 |
| **Timing** | `WarmupDuration` | 预热阶段时长（秒） |
| | `ActiveDuration` | 生效阶段时长（秒） |
| | `RecoveryDuration` | 恢复阶段时长（秒） |
| | `CooldownDuration` | 攻击冷却时长（秒） |
| **Combat** | `MaxAllowedAngleToPlayer` | 可发起攻击的最大角度（度） |
| | `AnimationName` | 播放的动画路径 |
| **Knockback** | `KnockbackDistance` / `KnockbackSpeed` / `KnockbackDuration` | 击退参数 |
| **Super Armor** | `GrantedImmunities` | 攻击期间赋予的免疫标志 |
| **Effect** | `EffectScene` | 攻击生成的特效预制 |
| | `SpawnTiming` | 特效生成时机：OnActive / OnAnimationHit / OnRecovery |

---

## 攻击流程

### 四阶段状态机

```
┌─────────────────────────────────────────┐
│ Idle (准备中)                            │
│ - 冷却计时                                │
└──────────────┬──────────────────────────┘
			   │ TryStart()
			   ↓
┌─────────────────────────────────────────┐
│ Warmup (预热)                            │
│ - 动画播放开始                            │
│ - OnWarmupStarted() 钩子                 │
│ - ShouldHoldWarmupPhase() 检查            │
└──────────────┬──────────────────────────┘
			   │ WarmupDuration 时间到 + 不Hold
			   ↓
┌─────────────────────────────────────────┐
│ Active (生效)                            │
│ - PerformAttack() 执行伤害                 │
│ - OnActivePhase() 钩子                   │
│ - 特效生成（若SpawnTiming=OnActive）      │
│ - ShouldHoldActivePhase() 检查            │
└──────────────┬──────────────────────────┘
			   │ ActiveDuration 时间到 + 不Hold
			   ↓
┌─────────────────────────────────────────┐
│ Recovery (恢复)                          │
│ - OnRecoveryStarted() 钩子               │
│ - 特效生成（若SpawnTiming=OnRecovery）    │
│ - ShouldHoldRecoveryPhase() 检查         │
│ - OnAttackFinished() 清理                │
└──────────────┬──────────────────────────┘
			   │ RecoveryDuration 时间到 + 不Hold
			   ↓
┌─────────────────────────────────────────┐
│ Idle (冷却中)                            │
└─────────────────────────────────────────┘
```

### 关键变量

- **IsRunning**: 是否正在执行攻击（非Idle状态）
- **IsOnCooldown**: 冷却计时器是否大于0

---

## 基础实现

### 最小化继承：简单近战攻击

```csharp
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Attacks
{
	public partial class EnemySimpleSlashAttack : EnemyAttackTemplate
	{
		[Export(PropertyHint.Range, "0,500,1")] public int Damage = 20;

		public override bool CanStart()
		{
			// 调用基类检查
			if (!base.CanStart()) return false;

			// 可在此追加额外条件检查
			// 如玩家距离、敌人状态等
			return true;
		}

		protected override void OnAttackStarted()
		{
			base.OnAttackStarted();
			// 此处追加初始化逻辑
		}

		protected override void OnActivePhase()
		{
			base.OnActivePhase();
			// 此处可追加额外的Active阶段逻辑
			// 如检查玩家位置、调整方向等
		}

		protected override void OnAnimationHit()
		{
			base.OnAnimationHit();
			// RequireAnimationHitTrigger=true 时触发

			// 在此执行伤害逻辑（需要从Spine动画帧事件调用 TriggerAnimationHit）
			if (Enemy?.PlayerTarget is GameActor player)
			{
				player.TakeDamage(Damage, Enemy.GlobalPosition);
				
				// 使用模板提供的击退工具
				TryApplyPlayerKnockback(
					player, 
					KnockbackDistance, 
					KnockbackDuration, 
					KnockbackSpeed,
					fallbackDirection: Enemy.FacingRight ? Vector2.Right : Vector2.Left
				);
			}
		}
	}
}
```

### 在编辑器中配置

1. 选择敌人节点
2. 在Inspector中找到Attack列表
3. 创建并拖入继承类脚本
4. 配置参数：
   - `WarmupDuration` / `ActiveDuration` / `RecoveryDuration`
   - `AnimationName`：路径形如 `"animations/slash"` 或 `"0"` (Spine模型用整数)
   - `RequireAnimationHitTrigger`：是否需要Spine帧事件触发伤害（建议=true）
   - 若需要生成特效：设置 `EffectScene` 和 `SpawnTiming`

---

## 进阶模式

### 1. 动画驱动伤害（推荐）

**何时使用**：攻击动画中有明确的"出手"时刻

```csharp
protected override void OnActivePhase()
{
	base.OnActivePhase();
	// 不直接伤害，只等待动画帧事件
}

protected override void OnAnimationHit()
{
	// 在Spine动画edit中为"hit"帧设置帧事件
	// C#代码中：Enemy.AnimPlayer?.TriggerEvent("hit")
	// 或在Spine的spine_player中设置 Events 引脚

	base.OnAnimationHit();

	if (Enemy?.PlayerTarget is GameActor player)
	{
		// 在这里执行伤害
		player.TakeDamage(Damage, Enemy.GlobalPosition);
	}
}
```

**在Spine/AnimatedSprite2D中配置**：
- 编辑动画，在发动伤害的帧上添加"hit"事件
- 敌人AnimPlayer会自动调用 `TriggerAnimationHit()`

### 2. 阶段Hold（长操作如冲刺、抓取）

**何时使用**：攻击需要多帧进行复杂操作（如追踪、旋转、等待输入）

```csharp
protected override bool ShouldHoldActivePhase()
{
	// 在Active阶段持续运动，直到到达目标/超时
	if (_isDashing && Vector2.Distance(Enemy.GlobalPosition, _dashTarget) > 5f)
	{
		// 继续冲刺
		UpdateDash(delta);
		return true;  // Hold Active阶段
	}

	// 冲刺完成或到达目标，退出Active
	return false;
}

protected override bool ShouldHoldRecoveryPhase()
{
	// 恢复中如需动作（如翻身、起身），可在此Hold
	if (!_recoveryAnimationFinished)
	{
		return true;
	}

	return false;
}
```

### 3. 多段攻击/连续技

```csharp
private int _hitCount = 0;
private const int MaxHits = 3;

protected override void OnAttackFinished()
{
	base.OnAttackFinished();

	// 若连续命中，可立即重新发起攻击
	if (_hitCount < MaxHits && _playerStillInRange)
	{
		_hitCount++;
		// 设置 _cooldownTimer = 0 以允许立即再次发起
		// （由 EnemyAttackController 或 TryStart 决定）
	}
	else
	{
		_hitCount = 0;
	}
}
```

---

## 检测区域攻击（Detection-Based Attacks）

### 场景说明

某些复杂攻击（如 `EnemyDashSlashAttack`、`EnemySmashAttack`、`EnemyKickAttack`、`EnemyChargeGrabAttack`、`EnemyOnePunchAttack`）需要在特定检测区域内自动触发，无需等待控制器的状态机切换。

这些攻击通过 `OnArea*` 信号回调（如 `_OnDetectionAreaEntered()`）或 `_PhysicsProcess()` 中的轮询（Poll）来调用 `TryRequestAttackFromDetection(reason: string)` 方法。

### 关键问题：Cooldown 绕过

⚠️ **常见错误**：`TryRequestAttackFromDetection()` 检查自身的冷却状态（`IsOnCooldown`, `IsRunning` 等），但**忽视了控制器级别的 `_interAttackDelay` 检查**。

**后果**：
- 攻击完成后，控制器设置 `_interAttackDelay` 以防止立即重新发起
- 但检测区域立即触发 `TryRequestAttackFromDetection()`
- 该方法只检查自身冷却（返回false），不知道控制器正在延迟
- 结果：`ChangeState("Attack")` 被强制执行，敌人在Idle/Walk中不断振荡

### 正确实现

**关键防护**：在 `TryRequestAttackFromDetection()` 中加入 `_controller.CanStart()` 检查

```csharp
private void TryRequestAttackFromDetection(string reason)
{
	if (Enemy == null) return;
	if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return;
	if (IsRunning || IsOnCooldown) return;
	if (Enemy.AttackTimer > 0) return;
	if (_postAttackCooldown > 0f) return;

	if (_controller != null && _controller.PeekQueuedAttack() != this)
	{
		return;
	}
	
	// ✅ 关键防护：检查控制器的 _interAttackDelay
	if (_controller != null && !_controller.CanStart()) return;

	if (Enemy.StateMachine?.CurrentState?.Name != "Attack")
	{
		Enemy.StateMachine?.ChangeState("Attack");
	}
}
```

### 为什么需要这个检查

| 检查项 | 说明 |
|--------|------|
| `IsRunning` | 自身是否正在执行 |
| `IsOnCooldown` | 自身的 `_cooldownTimer` 是否大于0 |
| `_postAttackCooldown > 0` | PostCooldown 专用延迟（如果支持） |
| **`_controller.CanStart()`** | **控制器级别的 `_interAttackDelay` 检查（防止切换攻击模式时卡住）** |

`_controller.CanStart()` 内部检查 `_interAttackDelay > 0f`，返回 false 时表示：
- 当前已有攻击正在冷却，所有新攻击应等待
- 这是**跨攻击**的全局延迟，优于单个攻击的自检

### 场景流程示例

```
Frame N:    SimpleMeleeAttack 完成 → OnAttackFinished()
			→ _controller._interAttackDelay = SimpleMeleeAttack.CooldownDuration

Frame N+1:  EnemyAttackState.ProcessTemplateAttack() → IsRunning=false
			→ ChangeToNextState() → Idle

Frame N+1:  EnemyIdleState 等待 CanStartAttack() = true
			→ 继续返回false（因为_interAttackDelay > 0）

同时N+1:    DashSlashAttack 检测区域信号触发
			→ TryRequestAttackFromDetection()
			✅ 检查 _controller.CanStart() → false
			✅ 返回，不强制 ChangeState("Attack")

Frame N+2:  _interAttackDelay 递减至0
			→ CanStartAttack() 返回true
			→ EnemyIdleState 切换到 Attack 或 DashSlashAttack 自动触发
```

### 常见检测区域攻击位置

| 文件 | 检测类型 | 说明 |
|-----|---------|------|
| `EnemyDashSlashAttack.cs` | 区域信号 + 轮询 | 冲刺斩，检测玩家进入冲刺路径 |
| `EnemySmashAttack.cs` | 区域信号 + 轮询 | 砸地板，检测地面范围 |
| `EnemyKickAttack.cs` | 区域信号 + 轮询 | 踢击，检测前方范围 |
| `EnemyChargeGrabAttack.cs` | 区域信号 + 轮询 | 抓取，检测抓取范围 |
| `EnemyOnePunchAttack.cs` | 区域信号 + 轮询 | 一拳，检测拳击范围 |

---

## 冷却机制（PostCooldown）

### 问题背景

许多敌人在完成攻击后需要一个**独立的冷却期**，在此期间不允许发起新攻击。同时，这个冷却期可能被其他游戏逻辑（如转身冻结）中断或暂停。

### 解决方案：PostCooldown 模式

**模板方式**：在继承类中手动管理 `_postAttackCooldown`

```csharp
public partial class EnemyChargeGrabAttack : EnemyAttackTemplate
{
	private const float PostCooldownDuration = 1.0f;
	[Export] public StringName CooldownStateName = "CooldownFrozen";

	private float _postAttackCooldown;
	private bool _pendingCooldownExit;

	// ... 其他逻辑 ...

	public override void _PhysicsProcess(double delta)
	{
		if (Enemy == null) return;

		// ⚠️ 关键防护：若其他攻击已接管状态机，立即放弃冷却追踪
		// 场景：DashSlash结束 → 冻结 → 冻结结束时新攻击开始 → 旧冷却不应干预
		if (_postAttackCooldown > 0f)
		{
			var currentStateName = Enemy?.StateMachine?.CurrentState?.Name;
			if (currentStateName == "Attack" && !IsRunning)
			{
				_postAttackCooldown = 0f;
				_pendingCooldownExit = false;
				return;
			}

			_postAttackCooldown -= (float)delta;
			if (_postAttackCooldown <= 0f)
			{
				_postAttackCooldown = 0f;
				if (_pendingCooldownExit)
				{
					FinishCooldownState();
					_pendingCooldownExit = false;
				}
			}
			return;
		}

		// 正常攻击逻辑
		UpdateDashMovement(delta);
	}

	protected override void OnAttackFinished()
	{
		base.OnAttackFinished();

		// 只在冷却未激活时启动
		if (_postAttackCooldown <= 0f)
		{
			StartPostCooldown();
		}
	}

	private void StartPostCooldown()
	{
		if (Enemy == null) return;

		bool starting = _postAttackCooldown <= 0f;
		_postAttackCooldown = PostCooldownDuration;
		Enemy.AttackTimer = Mathf.Max(Enemy.AttackTimer, PostCooldownDuration);
		Enemy.Velocity = Vector2.Zero;

		if (starting)
		{
			if (!CooldownStateName.IsEmpty && Enemy.StateMachine != null)
			{
				Enemy.StateMachine.ChangeState(CooldownStateName);  // 进入冷却状态
			}
		}

		_pendingCooldownExit = true;
	}

	private void FinishCooldownState()
	{
		if (Enemy?.StateMachine == null) return;

		// ⚠️ 关键修复：只有仍处于冷却状态时才负责退出
		// 若外部因素（如转身冻结）已切换状态，则不干预
		if (Enemy.StateMachine.CurrentState?.Name == CooldownStateName)
		{
			Enemy.StateMachine.ChangeState("Walk");
		}

		if (IsRunning)
		{
			Cancel();
		}
	}

	public override bool CanStart()
	{
		if (!base.CanStart()) return false;

		// PostCooldown 计时中不允许发起新攻击
		if (_postAttackCooldown > 0f) return false;

		// 其他检查...
		return true;
	}
}
```

### PostCooldown 最佳实践

✅ **正确做法**：

1. 在 `_PhysicsProcess` 入口处检查 `currentState == "Attack" && !IsRunning`，若成立立即清零冷却
2. `FinishCooldownState()` 中**只在当前状态确实是冷却状态时**才切状态，否则不干预
3. 在 `CanStart()` 中检查 `_postAttackCooldown > 0f` 来阻止新攻击

❌ **常见错误**：

- ❌ 在 `FinishCooldownState()` 中无条件执行 `ChangeState("Walk")`，导致打断其他攻击
- ❌ 遗漏对 `currentState == "Attack" && !IsRunning` 的检查，冷却计时在其他攻击运行时继续
- ❌ 冷却期间允许新攻击启动

---

## 控制器冷却协调（Controller Coordination）

### 背景：两层冷却系统

敌人攻击系统包含**两层独立的冷却机制**：

| 层级 | 位置 | 作用 | 管理者 |
|------|------|------|--------|
| **单个攻击冷却** | `EnemyAttackTemplate._cooldownTimer` | 防止同一攻击立即重复 | 各个攻击模板 |
| **控制器全局延迟** | `EnemyAttackController._interAttackDelay` | 防止任何攻击立即切换 | 控制器 |

### 为什么需要双层冷却

**场景1：SimpleMeleeAttack 完成后**
- `SimpleMeleeAttack._cooldownTimer = 0.5f`（自身冷却）
- `_interAttackDelay = 0.5f`（全局延迟）
- **结果**：任何攻击（包括DashSlashAttack）都无法启动

**场景2：没有 `_interAttackDelay` 的世界**
- SimpleMeleeAttack 自身冷却0.5秒，DashSlashAttack 冷却1.0秒
- 0.2秒后，DashSlashAttack 冷却结束，检测区域触发
- `TryRequestAttackFromDetection()` 看到 `!IsOnCooldown` = true，强制 `ChangeState("Attack")`
- **问题**：敌人在Idle↔Attack中振荡，SimpleMeleeAttack 还未冷却完

### 控制器初始化流程

```csharp
public partial class EnemyAttackController : EnemyAttackTemplate
{
	private float _interAttackDelay = 0f;  // 初始值为0

	public override bool CanStart()
	{
		if (_entries.Count == 0) return false;
		if (!base.CanStart()) return false;
		if (_interAttackDelay > 0f) return false;  // ✅ 核心检查
		// ... 其他检查 ...
		return true;
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);
		// ✅ 每帧递减
		if (_interAttackDelay > 0f) _interAttackDelay -= (float)delta;
		// ... 其他逻辑 ...
	}

	private void FinishControllerAttack(string reason, bool clearControllerCooldown = false)
	{
		float childInterAttackDelay = 0f;
		if (reason == "ChildFinished" && _currentAttack != null)
			childInterAttackDelay = _currentAttack.CooldownDuration;

		// ✅ 攻击完成时，从子攻击的 CooldownDuration 继承延迟
		if (clearControllerCooldown) _interAttackDelay = 0f;
		else if (childInterAttackDelay > 0f) _interAttackDelay = childInterAttackDelay;
	}
}
```

### 检测区域攻击的防护修复

上一章节介绍了在 `TryRequestAttackFromDetection()` 中添加 `_controller.CanStart()` 检查。这个检查的完整逻辑：

```csharp
if (_controller != null && !_controller.CanStart()) return;
```

**内部工作流程**：
1. `_controller.CanStart()` 检查 `_interAttackDelay > 0f`
2. 若为true，返回false（表示不能启动）
3. `TryRequestAttackFromDetection()` 在此处返回，不执行 `ChangeState("Attack")`
4. 敌人保持Idle/Walk，等待 `_interAttackDelay` 递减到0

### 最佳实践：攻击完成后的正确流程

```
Frame N:    AttackA 完成
			↓
			OnAttackFinished()
			↓
			EnemyAttackState 立即 exit → Idle/Walk
			↓
			_interAttackDelay = AttackA.CooldownDuration

Frame N+1:  检测区域触发 AttackB.TryRequestAttackFromDetection()
			↓
			检查 _controller.CanStart() → false（因为延迟中）
			↓
			返回（不强制切攻击）

Frame N+1:  EnemyIdleState / EnemyWalkState
			↓
			每帧调用 CanStartAttack()
			↓
			返回false（_interAttackDelay > 0）
			↓
			保持Idle/Walk

Frame N+k:  _interAttackDelay 递减至0
			↓
			EnemyIdleState 下次 PhysicsUpdate() 调用 CanStartAttack()
			↓
			返回true
			↓
			ChangeState("Attack") 并自动选择合适的攻击
```

### 性能提示

- **不要频繁创建新的冷却计时器**：重用 `_interAttackDelay`
- **不要在检测区域回调中做复杂计算**：只检查基本条件，留给 `_PhysicsProcess()` 处理
- **使用 `PeekQueuedAttack()`**：快速检查队列中的攻击，避免重复触发

### 调试方法

若敌人在攻击模式切换时卡住：

1. **检查 `_interAttackDelay`**：在Debug窗口查看 `EnemyAttackController._interAttackDelay` 值
2. **检查 `TryRequestAttackFromDetection` 逻辑**：确保含有 `if (_controller != null && !_controller.CanStart()) return;`
3. **查看状态日志**：添加 GD.PrintDebug() 到 `TryRequestAttackFromDetection()` 和 `CanStart()` 以跟踪调用顺序

---

## 常见问题

### Q1: 如何让伤害判定与动画同步？

**A**: 使用 `RequireAnimationHitTrigger = true` + 动画帧事件

```csharp
// 在编辑器中：
// - 设置 RequireAnimationHitTrigger = true
// - 在Spine编辑器中为"hit"帧添加帧事件

// 在C#代码中：
protected override void OnAnimationHit()
{
	base.OnAnimationHit();
	// 伤害逻辑
}
```

### Q2: 如何实现多个攻击的优先级？

**A**: 在 `CanStart()` 中追加条件检查，由 `EnemyAttackController` 按列表顺序遍历

```csharp
public override bool CanStart()
{
	if (!base.CanStart()) return false;

	// 若当前已在执行其他高优先级攻击，等待
	if (Enemy.IsPerformingCriticalAttack) return false;

	// 玩家必须在特定范围内
	if (Vector2.Distance(Enemy.GlobalPosition, Enemy.PlayerTarget.GlobalPosition) > 100f)
		return false;

	return true;
}
```

### Q3: 如何在攻击中追踪玩家（如导弹、激光）？

**A**: 在 `_PhysicsProcess` 或 `ShouldHold*` 钩子中更新方向

```csharp
protected override bool ShouldHoldActivePhase()
{
	if (Enemy?.PlayerTarget == null) return false;

	// 每帧更新指向玩家的方向
	Vector2 toPlayer = Enemy.GetDirectionToPlayer();
	Enemy.FacingRight = toPlayer.X > 0;

	// 直到某条件满足才退出Active
	if (_trackedTime > 2f)
	{
		return false;
	}

	_trackedTime += (float)GetPhysicsProcess();
	return true;
}
```

### Q4: 多个相同攻击实例是否会冲突？

**A**: 不会。每个敌人有独立的 `EnemyAttackController`，管理该敌人的所有攻击实例。

### Q5: 如何实现"蓄力"效果（不消耗Warmup时间）？

**A**: 在 `_PhysicsProcess` 中追加计时，条件满足后调用 `TryStart()`

```csharp
private float _chargeTime = 0f;

public override void _PhysicsProcess(double delta)
{
	if (Enemy.PlayerTarget != null && Enemy.IsPlayerWithinDetectionRange())
	{
		_chargeTime += (float)delta;
		if (_chargeTime >= ChargeThreshold && !IsRunning)
		{
			TryStart();
			_chargeTime = 0f;
		}
	}
	else
	{
		_chargeTime = 0f;
	}
}
```

### Q6: 如何处理攻击被打断？

**A**: 调用 `Cancel()` 方法，可选清除冷却

```csharp
// 若敌人进入受击状态
if (Enemy.CurrentState == "Hit")
{
	Cancel(clearCooldown: true);  // 被打断可立即重新发起
}
```

---

## 文件位置

- **基础模板**: `scripts/actors/enemies/attacks/EnemyAttackTemplate.cs`
- **简单实现示例**: `scripts/actors/enemies/attacks/EnemySimpleMeleeAttack.cs`
- **复杂实现示例**: `scripts/actors/enemies/attacks/EnemyDashSlashAttack.cs`
- **PostCooldown示例**: `scripts/actors/enemies/attacks/EnemyChargeGrabAttack.cs`
- **攻击控制器**: `scripts/actors/enemies/attacks/EnemyAttackController.cs`

---

## 检查清单（新增攻击时）

- [ ] 继承 `EnemyAttackTemplate`
- [ ] 配置导出属性（Timing、Damage、动画等）
- [ ] 实现 `CanStart()` 条件检查
- [ ] 若需要动画驱动伤害，设置 `RequireAnimationHitTrigger = true` 并实现 `OnAnimationHit()`
- [ ] 若需要Post冷却，实现 `_postAttackCooldown` 逻辑 + 防护检查
- [ ] 在动画中添加"hit"帧事件（若需要）
- [ ] 在 `EnemyAttackController` 的敌人子类中注册此攻击
- [ ] 测试：正常流程、被打断、冷却期间切攻击等边界情况

---

## 相关系统

- **状态机**: `scripts/core/StateMachine.cs`
- **敌人基类**: `scripts/actors/enemies/SampleEnemy.cs`
- **玩家受击**: `scripts/actors/heroes/MainCharacter.cs` → `TakeDamage()`
- **击退系统**: `EnemyAttackTemplate.TryApplyPlayerKnockback()`
- **效果系统**: `scripts/core/Effects/` 目录
