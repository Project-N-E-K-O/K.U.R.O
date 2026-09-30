using Godot;
using System;
using Kuros.Core;
using Kuros.Utils;
using Kuros.Systems.Stage;
using Kuros.Actors.Enemies.States;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Enemies;

public partial class SampleEnemy : GameActor
{
	[ExportCategory("Behavior")]
	[Export] public EnemyBehaviorConfig? BehaviorConfig { get; set; }

	[ExportCategory("Boss HUD")]
	/// <summary>是否为 Boss（end 关卡高血精英）：BossHUD 为其显示屏幕血条。</summary>
	[Export] public bool IsBoss { get; set; } = false;
	/// <summary>Boss 血条显示名（如"网管"）；为空则不显示名字；非 Boss 忽略。</summary>
	[Export] public string DisplayName { get; set; } = string.Empty;
	/// <summary>Boss 血条图标（png）；为空则不显示图标。</summary>
	[Export] public Texture2D? BossIcon { get; set; }

	[ExportCategory("Debug")]
	[Export] public bool EnableStateDebugOverlay = false;
	[Export] public Vector2 DebugOverlayOffset = new(-90f, -90f);
	[Export(PropertyHint.Range, "8,128,1")] public int DebugOverlayFontSize = 14;
	[Export] public Color DebugOverlayColor = new(1f, 0f, 0f, 1f);
	/// <summary>调试覆盖层的绘制 z 索引（**绝对**值，默认 10）：不跟敌人自身的 z_index 走。
	/// 覆盖层由独立子节点绘制（见 <see cref="EnemyStateDebugOverlay"/>），所以敌人再怎么调 z 都不影响可读性。</summary>
	[Export] public int DebugOverlayZIndex { get; set; } = 10;

	[ExportCategory("Detection")]
	[Export] public Area2D? DetectionArea { get; private set; }
	[Export(PropertyHint.Range, "200,1000,10")] public float AttackRangeCheckDistance = 500f;

	[ExportCategory("Attack")]
	[Export] public Area2D? AttackArea { get; private set; }

	[ExportCategory("Score")]
	[Export] public int ScoreValue = 10;

	/// <summary>死亡时是否给玩家加分数。测试控制台（EnemySpawnConsole）生成的敌人置 false——清场/击杀不应获得分数。</summary>
	public bool GrantsScore { get; set; } = true;

	[ExportCategory("Effects")]
	[Export] public PackedScene? DeathEffect { get; set; }
	private SamplePlayer? _player;
	private bool _scoreGranted;
	private string _debugOverlayText = string.Empty;
	private EnemyStateDebugOverlay? _debugOverlay;
	private EnemyAttackController? _cachedAttackController;
	public float KeepDistanceCooldownRemaining;
		public float CloseInCooldownRemaining;

	// public SampleEnemy()
	// {
	// 	Speed = 150.0f;
	// 	AttackDamage = 10.0f;
	// 	AttackCooldown = 1.5f;
	// 	MaxHealth = 50;
	// }

	/// <summary>本敌人的**对外伤害系数**（楼层难度）。默认 1；由 <see cref="ApplyStageScaling"/> 按当前层数写入，
	/// 实际乘算发生在受击侧（<see cref="GameActor.TakeDamage"/>）——因为多数敌人的伤害写在攻击特效里，
	/// 不在自己身上，只有统一在受击侧缩放才能覆盖"发射特效型"敌人（printerMachine / guard3 等）。</summary>
	public float OutgoingDamageMultiplier { get; set; } = 1f;

	private bool _stageScaled;

	/// <summary>按楼层缩放本敌人的数值：血量上限 ×血量系数、对外伤害系数。仅调用一次。
	///
	/// 必须在 <c>base._Ready()</c> **之前**调用——基类在 _Ready 开头就把 MaxHealth 抄进
	/// BaseMaxHealth / CurrentHealth，晚了就只改上限不改当前血。
	///
	/// 不缩放 AttackDamage：那是近战/接触伤害的输入，而受击侧还会再乘一次系数，会双倍。
	/// 子类要让某个敌人豁免（例如固定强度的特殊敌人），override 成空即可。</summary>
	protected virtual void ApplyStageScaling()
	{
		if (_stageScaled) return;
		_stageScaled = true;

		var session = StageSession.Current;
		if (session == null) return;   // 独立场景（测试/工具）没有会话 → 一律 ×1

		MaxHealth = Mathf.RoundToInt(MaxHealth * session.EnemyHealthMultiplier);
		OutgoingDamageMultiplier = session.EnemyDamageMultiplier;
	}

	public override void _Ready()
	{
		ApplyStageScaling();
		base._Ready();
		if (!IsInGroup("enemies"))
		{
			AddToGroup("enemies");
		}

		// Try to find areas if not assigned (they are nested under Sprite2D in the scene)
		if (AttackArea == null)
		{
			AttackArea = GetNodeOrNull<Area2D>("Sprite2D/AttackArea");
			if (AttackArea == null) GD.PrintErr("AttackArea not found at Sprite2D/AttackArea");
		}
		if (DetectionArea == null)
		{
			DetectionArea = GetNodeOrNull<Area2D>("Sprite2D/ControllerDetectionArea");
			if (DetectionArea == null) GD.PrintErr("DetectionArea not found at Sprite2D/ControllerDetectionArea");
		}
		RefreshPlayerReference();
		EnsureDebugOverlay();
		UpdateDebugOverlayText();
		_debugOverlay?.QueueRedraw();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (KeepDistanceCooldownRemaining > 0f)
				KeepDistanceCooldownRemaining -= (float)delta;
			if (CloseInCooldownRemaining > 0f)
				CloseInCooldownRemaining -= (float)delta;
			if (!EnableStateDebugOverlay) return;

		UpdateDebugOverlayText();
		_debugOverlay?.QueueRedraw();
	}

	/// <summary>覆盖层文本（由 <see cref="EnemyStateDebugOverlay"/> 读取绘制）。</summary>
	public string DebugOverlayText => _debugOverlayText;

	/// <summary>创建调试覆盖层节点：独立 z（ZAsRelative = false）——覆盖层不能被敌人自己的 z_index 决定可见性。</summary>
	private void EnsureDebugOverlay()
	{
		if (!EnableStateDebugOverlay || _debugOverlay != null) return;

		_debugOverlay = new EnemyStateDebugOverlay
		{
			Name = "StateDebugOverlay",
			ZAsRelative = false,
			ZIndex = DebugOverlayZIndex,
			Enemy = this,
		};
		AddChild(_debugOverlay);
	}

	public SamplePlayer? PlayerTarget => _player;

	/// <summary>
	/// 检查玩家是否在检测范围内。使用 DetectionArea 碰撞检测。
	/// 子类可重写以整体关闭追踪（返回 false 即"看不见玩家"：追击、选招、攻击状态的进出都会随之停摆）。
	/// </summary>
	public virtual bool IsPlayerWithinDetectionRange()
	{
		RefreshPlayerReference();
		if (_player == null || DetectionArea == null) return false;
		return DetectionArea.OverlapsBody(_player);
	}

	/// <summary>
	/// 检查玩家是否在攻击范围内。使用 AttackArea 碰撞检测。
	/// </summary>
	public bool IsPlayerInAttackRange()
	{
		RefreshPlayerReference();
		if (_player == null || AttackArea == null) return false;
		return _player.IsHitByArea(AttackArea);
	}

	public bool IsPlayerWithinStoppingRange()
	{
		RefreshPlayerReference();
		if (_player == null || AttackArea == null) return false;

		float stopDist = 48f;
		var shapeNode = AttackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shapeNode?.Shape != null)
		{
			var rect = shapeNode.Shape.GetRect();
			stopDist = Mathf.Max(rect.Size.X, rect.Size.Y);
		}

		return GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) <= stopDist * stopDist;
	}

	/// <summary>
	/// 检查玩家是否正在攻击（处于 Attack 状态）。
	/// </summary>
	public bool IsPlayerAttacking()
	{
		RefreshPlayerReference();
		return _player?.StateMachine?.CurrentState?.Name == "Attack";
	}

	/// <summary>
	/// 检查本敌人是否在玩家的攻击范围内（基于固定距离检测，不依赖玩家AttackArea碰撞体）。
	/// </summary>
	public bool IsEnemyInPlayerAttackRange()
	{
		RefreshPlayerReference();
		if (_player == null) return false;

		float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
		return distanceToPlayer <= AttackRangeCheckDistance;
	}

	/// <summary>自身受击判定区（玩家攻击命中通道同一节点；懒解析缓存）。</summary>
	private Area2D? _selfHitArea;

	public Area2D? GetSelfHitArea()
	{
		if (_selfHitArea != null && GodotObject.IsInstanceValid(_selfHitArea))
			return _selfHitArea;

		_selfHitArea = GetNodeOrNull<Area2D>("Sprite2D/HitArea")
			?? GetNodeOrNull<Area2D>("Sprite2D/HitAreaMover/HitArea");
		_selfHitArea ??= FindChild("HitArea", recursive: true, owned: false) as Area2D;
		return _selfHitArea;
	}

	/// <summary>
	/// 本敌人体是否位于玩家当前攻击判定区（解析自当前武器的 AttackArea）几何内——
	/// 与玩家命中判定同一通道（OverlapsArea）,矩形/胶囊/圆/旋转均由物理引擎处理,
	/// 不受固定距离(AttackRangeCheckDistance)误差影响。玩家无法解析攻击区时返回 false。
	/// </summary>
	public bool IsInsidePlayerAttackArea()
	{
		RefreshPlayerReference();
		if (_player == null) return false;

		var attackArea = _player.ResolveAttackAreaForHitDetection();
		var myHitArea = GetSelfHitArea();
		if (attackArea == null || myHitArea == null) return false;

		return attackArea.OverlapsArea(myHitArea);
	}

	/// <summary>贴脸重叠时锁定的目标边(GetApproachTarget 用):重叠期保持同侧,防玩家微动引发逐帧换边/翻转抖动。
	/// 离开重叠区(|dx|≥1)时按相对位置重算并刷新锁。</summary>
	private float _approachSideSign;

	public Vector2 GetApproachTarget()
	{
		RefreshPlayerReference();
		if (_player == null) return GlobalPosition;

		float dx = GlobalPosition.X - _player.GlobalPosition.X;
		float sideSign;
		if (Mathf.Abs(dx) >= 1f)
		{
			sideSign = dx > 0 ? 1f : -1f;
			_approachSideSign = sideSign; // 非重叠:直接由相对位置决定并刷新锁定边
		}
		else
		{
			// 水平重叠:沿用上次锁定边(首次无锁定时按当前朝向侧)
			if (_approachSideSign == 0f)
				_approachSideSign = FacingRight ? 1f : -1f;
			sideSign = _approachSideSign;
		}

		float offset = GetHitAreaHalfWidth();
		return _player.GlobalPosition + new Vector2(offset * sideSign, 0);
	}

	private float GetHitAreaHalfWidth()
	{
		var hitArea = _player?.HitArea;
		if (hitArea == null) return 300f;
		var shapeNode = hitArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shapeNode?.Shape == null) return 300f;
		return shapeNode.Shape switch
		{
			CircleShape2D circle => circle.Radius * Mathf.Abs(shapeNode.Scale.X),
			RectangleShape2D rect => rect.Size.X * 0.5f * Mathf.Abs(shapeNode.Scale.X),
			_ => 300f
		};
	}


	public Vector2 GetDirectionToPlayer()
	{
		RefreshPlayerReference();
		if (_player == null) return Vector2.Zero;
		Vector2 direction = (_player.GlobalPosition - GlobalPosition);
		return direction == Vector2.Zero ? Vector2.Zero : direction.Normalized();
	}

	/// <summary>
	/// 判断敌人是否可以进入 Attack 状态。
	/// 优先委托给 AttackController.CanStart()，不存在时回退到 IsPlayerInAttackRange()。
	/// 子类可重写此方法实现自定义条件。
	/// </summary>
	public virtual bool CanStartAttack()
	{
		// 优先委托给 AttackController（它通过 EnemyAttackTemplate._cooldownTimer 管理自己的 CD）
		if (_cachedAttackController == null || !IsInstanceValid(_cachedAttackController))
		{
			var attackState = StateMachine?.GetNodeOrNull("Attack");
			_cachedAttackController = attackState?.GetNodeOrNull<EnemyAttackController>("AttackController");
		}

		if (_cachedAttackController != null && IsInstanceValid(_cachedAttackController))
			return _cachedAttackController.CanStart();

		// 回退：无 AttackController 时使用 AttackTimer 和近战范围检测
		if (AttackTimer > 0) return false;
		return IsPlayerInAttackRange();
	}

	/// <summary>该敌人当前是否正在执行指定攻击（攻击控制器当前子攻击匹配；无控制器返回 false）。</summary>
	public bool IsAttackRunning(string attackName)
	{
		if (_cachedAttackController == null || !IsInstanceValid(_cachedAttackController))
		{
			var attackState = StateMachine?.GetNodeOrNull("Attack");
			_cachedAttackController = attackState?.GetNodeOrNull<EnemyAttackController>("AttackController");
		}

		return _cachedAttackController != null
			&& IsInstanceValid(_cachedAttackController)
			&& _cachedAttackController.IsAttackRunning(attackName);
	}

	public void PerformAttack(TargetableFactions targetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem)
	{
		PerformAttack(AttackArea, targetableFactions);
	}

	/// <summary>在指定区域结算攻击伤害（区域由攻击模板按 DamageAreaPath / AttackAreaPath 解析后传入）。
	/// area 为空时不结算——不静默回退根节点 AttackArea，避免"所见非所伤"。</summary>
	public void PerformAttack(Area2D? area, TargetableFactions targetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem)
	{
		//AttackTimer = AttackCooldown;
		GameLogger.Info(nameof(SampleEnemy), "Enemy PerformAttack");

		RefreshPlayerReference();
		if (area == null) return;
		DamageDispatcher.DealDamageFromArea(area, AttackDamage, this, targetableFactions);

	}

	public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null, Kuros.Core.Events.DamageSource damageSource = Kuros.Core.Events.DamageSource.DirectAttack, bool bypassMergeWindow = false)
	{
		bool dealt = base.TakeDamage(damage, attackOrigin, attacker, damageSource, bypassMergeWindow);
		// If we want to play hit animation manually since base FSM logic might not cover enemy without state machine
		if (_animationPlayer != null)
		{
			 _animationPlayer.Play("animations/hit");
		}
		return dealt;
	}

	protected override void Die()
	{
		GameLogger.Info(nameof(SampleEnemy), "Enemy died!");
		base.Die();
	}

		protected override void OnDeathFinalized()
		{
			RefreshPlayerReference();
			if (GrantsScore && !_scoreGranted && _player != null)
			{
				_player.AddScore(ScoreValue);
				_scoreGranted = true;
			}

			base.OnDeathFinalized();
		}
	private void RefreshPlayerReference()
	{
		if (_player != null && IsInstanceValid(_player)) return;
		_player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
		if (_player == null)
		{
			_player = GetTree().Root.FindChild("Player", true, false) as SamplePlayer;
		}
	}

	protected virtual float GetFrozenRemainingTime()
	{
		if (StateMachine?.CurrentState is EnemyFrozenState frozenState)
			return frozenState.GetRemainingTime();
		return 0f;
	}

	private void UpdateDebugOverlayText()
	{
		string stateName = StateMachine?.CurrentState?.Name ?? "None";
		var lines = new System.Collections.Generic.List<string>();

		// 行1：状态 + HP
		string statusLine = $"{Name} | State: {stateName} | HP: {CurrentHealth}/{MaxHealth}";
		if (stateName == "Frozen")
		{
			float remainingTime = GetFrozenRemainingTime();
			if (remainingTime > 0f)
				statusLine += $" | Frozen: {remainingTime:F2}s";
		}
		lines.Add(statusLine);

		if (_cachedAttackController == null || !IsInstanceValid(_cachedAttackController))
		{
			var attackState = StateMachine?.GetNodeOrNull("Attack");
			_cachedAttackController = attackState?.GetNodeOrNull<EnemyAttackController>("AttackController");
		}
		if (_cachedAttackController != null && IsInstanceValid(_cachedAttackController))
		{
			// 行2：当前攻击 + 冷却
			string attackInfo = "";
			if (stateName == "Attack")
			{
				string currentAttackName = GetCurrentAttackName();
				if (!string.IsNullOrEmpty(currentAttackName))
					attackInfo = $" | Attack: {currentAttackName}";
			}
			var (cdRemaining, cdDuration, cdName) = _cachedAttackController.GetShortestCooldownInfo();
			string cooldownInfo = "";
			if (cdRemaining > 0f)
			{
				string nameHint = string.IsNullOrEmpty(cdName) ? "" : $"({cdName})";
				cooldownInfo = $" | CD: {cdRemaining:F2}s/{cdDuration:F1}s {nameHint}";
			}
			lines.Add($"Attack:{attackInfo}{cooldownInfo}");

			// 行3：排队攻击等待超时（queued 攻击名 / 已计时间 / 是否可启动）
			if (_cachedAttackController.QueuedAttackTimeout > 0f && !string.IsNullOrEmpty(_cachedAttackController.QueuedAttackName))
			{
				lines.Add($"Queued: {_cachedAttackController.QueuedAttackName} "
					+ $"{_cachedAttackController.QueuedElapsed:F1}s/{_cachedAttackController.QueuedAttackTimeout:F1}s "
					+ $"canStart={_cachedAttackController.QueuedCanStart}");
			}

			// 行4：各攻击当前权重
			var weights = _cachedAttackController.GetAttackWeights();
			if (weights.Count > 0)
			{
				var parts = new System.Collections.Generic.List<string>();
				foreach (var kvp in weights)
					parts.Add($"{kvp.Key}={kvp.Value:F0}");
				lines.Add($"Weights: {string.Join(" ", parts)}");
			}
		}

		_debugOverlayText = string.Join("\n", lines);
	}

	private string GetCurrentAttackName()
	{
		// 尝试从缓存获取 AttackController
		if (_cachedAttackController != null && IsInstanceValid(_cachedAttackController))
		{
			// 通过反射获取 CurrentAttackName 属性
			var property = _cachedAttackController.GetType().GetProperty("CurrentAttackName");
			if (property != null)
			{
				var value = property.GetValue(_cachedAttackController);
				return value?.ToString() ?? string.Empty;
			}
		}

		// 尝试从 StateMachine/Attack/AttackController 获取
		if (StateMachine == null) return string.Empty;

		var attackState = StateMachine.GetNodeOrNull("Attack");
		if (attackState == null) return string.Empty;

		_cachedAttackController = attackState.GetNodeOrNull<EnemyAttackController>("AttackController");
		if (_cachedAttackController != null)
		{
			// 通过反射获取 CurrentAttackName 属性
			var property = _cachedAttackController.GetType().GetProperty("CurrentAttackName");
			if (property != null)
			{
				var value = property.GetValue(_cachedAttackController);
				return value?.ToString() ?? string.Empty;
			}
		}

		return string.Empty;
	}
}
