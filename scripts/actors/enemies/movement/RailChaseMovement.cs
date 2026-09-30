using Godot;

/// <summary>
/// 滑槽追击移动组件：把"标准追击"限制在滑槽里。
/// 与 <see cref="EnemyChaseMovement"/> 同构（Idle/Walk 切换、受阻状态处理、停步判定全部继承），只改两处：
///   1. 移动方向只取机械轴向（默认 X）朝玩家，另一个轴完全不动——纵向交给滑槽带着升降；
///   2. 每帧收尾硬钳：机械轴向夹在 Carriage 行程区间内，滑槽轴向的局部坐标钉回挂载点，
///      击退/爆炸等外部推力同样推不出滑槽。
/// 挂在敌人根节点的直接子节点上（与 EnemyFreezeOnTurnMovement 同层）；注册 __movement_component_registered 后
/// Walk/Idle 状态不再自行移动。未挂在 SlideRailMount 下时退化为自由追击（便于单独生成测试）。
/// </summary>
public partial class RailChaseMovement : EnemyChaseMovement
{
	/// <summary>机械轴向的死区：和玩家对齐后停住，避免在玩家两侧逐帧来回抖。</summary>
	private const float AxisDeadzone = 8f;

	private SlideRailMount? _rail;
	private bool _resolved;
	private float _carriageLo;
	private float _carriageHi;
	private Vector2 _mountLocal;

	/// <summary>外部接管移动：true 时本组件不再自行追击/减速/MoveAndSlide，只保留每帧硬钳（ClampToRail）。
	/// 供"自己驱动位移"的技能使用（如 rogueAI 大招的蓄力/冲刺）；用完**必须复位**，否则永远不再追击。</summary>
	public bool ExternalDrive { get; set; }

	public override void _Ready()
	{
		base._Ready();
		if (Enemy == null) return;

		// 收尾钳位必须排在本帧所有移动之后：状态机的 MoveAndSlide 走默认优先级 0，
		// 本组件抬到正优先级 → 最后执行（与 Magnet"先移动、再 ClampToRail"同一时序）
		ProcessPhysicsPriority = 100;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint() || Enemy == null) return;

		ResolveRail();
		// 外部接管（技能自己在 _PhysicsProcess 写 Velocity）时让位，但限位兜底照旧——冲刺也出不了滑槽
		if (!ExternalDrive) base._PhysicsProcess(delta);
		ClampToRail();
	}

	/// <summary>只沿机械轴向朝玩家推进；没有滑槽时退回基类的自由追击。</summary>
	protected override Vector2 GetMoveDirection()
	{
		if (_rail == null || Enemy?.PlayerTarget == null) return base.GetMoveDirection();

		Vector2 playerPos = Enemy.PlayerTarget.GlobalPosition;
		Vector2 selfPos = Enemy.GlobalPosition;

		if (_rail.CarriageAxis == SlideRailMount.RailAxis.X)
		{
			float dx = playerPos.X - selfPos.X;
			return Mathf.Abs(dx) <= AxisDeadzone ? Vector2.Zero : new Vector2(Mathf.Sign(dx), 0f);
		}

		float dy = playerPos.Y - selfPos.Y;
		return Mathf.Abs(dy) <= AxisDeadzone ? Vector2.Zero : new Vector2(0f, Mathf.Sign(dy));
	}

	/// <summary>首物理帧解析滑槽（生成器是 AddChild 之后才摆位置，_Ready 时还没到位）。</summary>
	private void ResolveRail()
	{
		if (_resolved) return;
		_resolved = true;

		_rail = SlideRailMount.FindFor(Enemy);
		if (_rail == null)
		{
			GD.PushWarning($"{Name}: {Enemy!.Name} 未挂在 SlideRailMount 下，滑槽追击退化为自由移动。");
			return;
		}

		_rail.ResolveNow();
		_carriageLo = _rail.CarriageLo;
		_carriageHi = _rail.CarriageHi;
		_mountLocal = Enemy.Position;
	}

	/// <summary>限位兜底：与状态无关，每帧无条件执行（Hit/Frozen 等不驱动移动的状态也钳位）。</summary>
	private void ClampToRail()
	{
		if (_rail == null || Enemy == null) return;

		// 滑槽轴向：局部坐标钉回挂载点（滑槽怎么升降都跟着走）
		var local = Enemy.Position;
		if (_rail.Axis == SlideRailMount.RailAxis.X) local.X = _mountLocal.X;
		else local.Y = _mountLocal.Y;
		Enemy.Position = local;

		// 机械轴向：区间用排序后的 Lo/Hi（不能拿 Near/Far 当上下界）
		var pos = Enemy.GlobalPosition;
		if (_rail.CarriageAxis == SlideRailMount.RailAxis.X)
			pos.X = Mathf.Clamp(pos.X, _carriageLo, _carriageHi);
		else
			pos.Y = Mathf.Clamp(pos.Y, _carriageLo, _carriageHi);
		Enemy.GlobalPosition = pos;
	}
}
