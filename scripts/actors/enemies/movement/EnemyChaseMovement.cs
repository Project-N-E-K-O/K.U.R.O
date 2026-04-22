using Godot;
using Godot.Collections;
using Kuros.Core.Effects;

public partial class EnemyChaseMovement : Node
{
	private const string MovementMetaKey = "__movement_component_registered";

	[Export] public string IdleStateName = "Idle";
	[Export] public string WalkStateName = "Walk";
	private static readonly StringName AttackStateName = new("Attack");
	private static readonly StringName HitStateName = new("Hit");
	private static readonly StringName FrozenStateName = new("Frozen");
	private static readonly StringName CooldownStateName = new("CooldownFrozen");
	private static readonly StringName DyingStateName = new("Dying");
	private static readonly StringName DeadStateName = new("Dead");

	[Export] public Array<StringName> BlockedStates { get; set; } = new Array<StringName>
	{
		AttackStateName,
		HitStateName,
		FrozenStateName,
		CooldownStateName,
		DyingStateName,
		DeadStateName
	};

	protected SampleEnemy? Enemy;

	/// <summary>
	/// 可选的导航代理节点，存在时使用寻路避障，否则退回直线追踪。
	/// </summary>
	protected NavigationAgent2D? NavAgent;

	/// <summary>
	/// avoidance 计算出的安全速度（由 velocity_computed 信号写入）
	/// </summary>
	private Vector2 _safeVelocity = Vector2.Zero;
	private bool _hasSafeVelocity = false;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		Enemy = GetParent<SampleEnemy>();
		if (Enemy == null)
		{
			GD.PushWarning($"{Name}: EnemyChaseMovement must be a child of SampleEnemy.");
			QueueFree();
			return;
		}

		if (Enemy.HasMeta(MovementMetaKey))
		{
			GD.PushWarning($"{Name}: {Enemy.Name} already has a movement component. Removing duplicate.");
			QueueFree();
			Enemy = null;
			return;
		}

		Enemy.SetMeta(MovementMetaKey, this);

		// 尝试从敌人节点获取 NavigationAgent2D（可选）
		NavAgent = Enemy.GetNodeOrNull<NavigationAgent2D>("NavigationAgent2D");
		if (NavAgent != null)
		{
			// 连接 velocity_computed 信号以接收 avoidance 计算后的安全速度
			NavAgent.VelocityComputed += OnVelocityComputed;
		}
	}

	/// <summary>
	/// 接收 NavigationAgent2D avoidance 计算完毕后的安全速度
	/// </summary>
	private void OnVelocityComputed(Vector2 safeVelocity)
	{
		_safeVelocity = safeVelocity;
		_hasSafeVelocity = true;
	}

	public override void _ExitTree()
	{
		if (NavAgent != null)
		{
			NavAgent.VelocityComputed -= OnVelocityComputed;
		}

		if (Enemy != null && Enemy.HasMeta(MovementMetaKey))
		{
			var ownerVariant = Enemy.GetMeta(MovementMetaKey);
			if (ownerVariant.VariantType == Variant.Type.Object)
			{
				var owner = ownerVariant.As<Node>();
				if (owner == this)
				{
					Enemy.RemoveMeta(MovementMetaKey);
				}
			}
			else
			{
				Enemy.RemoveMeta(MovementMetaKey);
			}
		}

		base._ExitTree();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint() || Enemy == null) return;
		if (Enemy.StateMachine == null) return;

		string currentState = Enemy.StateMachine.CurrentState?.Name ?? string.Empty;
		if (IsBlocked(currentState))
		{
			Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * (float)delta);
			Enemy.MoveAndSlide();
			return;
		}

		if (Enemy.IsPlayerWithinDetectionRange())
		{
			EnsureState(WalkStateName, currentState);
			Vector2 direction = GetMoveDirection();
			Vector2 desiredVelocity = direction * Enemy.Speed;

			if (NavAgent != null && NavAgent.AvoidanceEnabled)
			{
				// 将期望速度提交给 avoidance 系统，等待 velocity_computed 回调
				NavAgent.SetVelocity(desiredVelocity);

				// 取 avoidance 计算出的安全方向，但保持 Enemy.Speed 大小
				if (_hasSafeVelocity && _safeVelocity.LengthSquared() > 0.01f)
				{
					Enemy.Velocity = _safeVelocity.Normalized() * Enemy.Speed;
				}
				else
				{
					Enemy.Velocity = desiredVelocity;
				}
				_hasSafeVelocity = false;
			}
			else
			{
				Enemy.Velocity = desiredVelocity;
			}

			if (desiredVelocity.X != 0)
			{
				Enemy.FlipFacing(desiredVelocity.X > 0);
			}
		}
		else
		{
			EnsureState(IdleStateName, currentState);
			Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2 * (float)delta);
		}

		Enemy.MoveAndSlide();
		Enemy.ClampPositionToScreen();
	}

	/// <summary>
	/// 获取本帧移动方向：有 NavigationAgent2D 时使用寻路，否则直线朝向玩家。
	/// </summary>
	protected virtual Vector2 GetMoveDirection()
	{
		if (NavAgent != null && Enemy != null)
		{
			var player = Enemy.PlayerTarget;
			if (player != null)
			{
				NavAgent.TargetPosition = player.GlobalPosition;
			}

			if (!NavAgent.IsNavigationFinished())
			{
				Vector2 nextPoint = NavAgent.GetNextPathPosition();
				Vector2 dir = (nextPoint - Enemy.GlobalPosition).Normalized();
				if (!dir.IsZeroApprox())
					return dir;
			}
		}

		// 回退：直线追踪
		return Enemy?.GetDirectionToPlayer() ?? Vector2.Zero;
	}

	private bool IsBlocked(string stateName)
	{
		foreach (var blocked in BlockedStates)
		{
			if (blocked == stateName) return true;
		}
		return false;
	}

	private void EnsureState(string targetState, string currentState)
	{
		if (string.IsNullOrEmpty(targetState)) return;
		if (currentState == targetState) return;
		Enemy?.StateMachine?.ChangeState(targetState);
	}

}
