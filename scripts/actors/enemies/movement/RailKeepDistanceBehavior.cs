using Godot;
using Kuros.Actors.Enemies;

/// <summary>
/// "沿轨跟随玩家"（龙门**两轴**，机械桩类敌人共用）：
///   · 机械轴保持间距 N —— 玩家离得太远 → 朝玩家靠近；太近 → 背离退开；带内 → 停住；
///   · 滑槽轴**对齐玩家** —— 交给滑槽自己按它的 <see cref="SlideRailMount.Speed"/> 恒速滑过去
///     （与磁铁臂的龙门结构同一套：横向归机械、纵向归滑槽）。
/// 与 <see cref="RailChaseMovement"/> 配对——那个只会**贴到人身上**，本组件负责**维持在 N**（同一根轨迹、同一套行程）。
/// 移动不改动画选择：由宿主/动画控制器按状态决定（本炮台 Attack 期间播 preheat/attack/recover ✓；
/// 其余时候播 idle/d_idle ✓ —— 这套骨架是悬浮机械，**没有走路循环**，不会、也不该出现 walk/d_walk）。
///
/// 平滑（P2 跟随同款）：期望速度与"离 N 还差多少"成正比（越接近越慢）+ 上限 <see cref="Speed"/>，
/// 再让实际速度**一阶滞后**地追上去 → 起步、收住、换向都不是瞬间满速，没有"一步到位"的顿挫。
/// 只有两个量：<see cref="Speed"/>（上限）与 <see cref="Smoothing"/>（时间常数 1/Smoothing = 从静止加满速的秒数），
/// 加速度/刹车距离都由它们推导，不需要另配。
///
/// 为什么不是复用 <see cref="Kuros.Actors.Enemies.States.EnemyKeepDistanceState"/>：那个是自由 2D 行为
/// （-toPlayer 方向 + 八向射线避障 + MoveAndSlide + ClampPositionToScreen + FlipFacing），
/// 在 rail 上会和每帧硬钳互相打架 ✗。但它的**调参来源**可以复用：速度默认取 <see cref="SampleEnemy.Speed"/>。
///
/// 三条铁律：
///   1. 速度**记在自己身上**（`_velocity` 跨帧记忆）——敌人自己的 `Enemy.Velocity` 会被静止占位状态
///      每帧清零，读它等于每帧从 0 起步，平滑会全部失效。
///   2. 位移提交**看状态**：Attack 状态下 `EnemyAttackState` 每帧已经替模板调过一次 `MoveAndSlide`，
///      这里再调一次会让实际速度翻倍（magnet / 大招同款铁律）；其余状态没人提交，由本组件提交。
///   3. **只应由宿主显式启用**（<see cref="Active"/>），绝不自动接管：magnet 的相位机、过场大招、
///      RailChaseMovement 都在写同一根轴，同一时刻只能有一个写入者。
/// </summary>
public partial class RailKeepDistanceBehavior : Node
{
	/// <summary>是否接管本帧的沿轨移动（由宿主按状态开关）。</summary>
	[Export] public bool Active { get; set; }

	/// <summary>维持的间距 N（px，沿机械轴）：太远靠近、太近退开；0 = 取 BehaviorConfig.MinComfortDistance。</summary>
	[Export(PropertyHint.Range, "0,3000,10")] public float KeepDistance { get; set; }

	/// <summary>机械轴速度上限（px/s）；0 = Enemy.Speed。
	/// 追求/贴近速度由它封顶，接近 N 时会自动慢下来（不需要另配刹车）。</summary>
	[Export(PropertyHint.Range, "0,3000,10")] public float Speed { get; set; }

	/// <summary>平滑度（1/s）：时间常数 1/Smoothing，即"从静止加速到满速"的秒数；越小越慢越软。
	/// 同时决定收敛增益（期望速度 = 误差 × Smoothing）——两者同源，所以接近 N 的减速与起步的加速是同一手感。</summary>
	[Export(PropertyHint.Range, "0.1,30,0.1")] public float Smoothing { get; set; } = 5f;

	/// <summary>到位死区**下限**（px）：实际死区 = max(本值, 速度 ÷ 物理帧率)，与 magnet 同约定。</summary>
	[Export(PropertyHint.Range, "1,64,1")] public float MinDeadzone { get; set; } = 8f;

	[Export] public bool EnableDebugLogs { get; set; } = false;

	private SampleEnemy? _enemy;
	private SlideRailMount? _rail;
	/// <summary>本组件自己的速度（跨帧记忆）：一阶滞后需要它，且不能读 Enemy.Velocity——静止状态每帧清零。</summary>
	private Vector2 _velocity;
	private bool _missingRailWarned;
	private bool _noDistanceWarned;

	public override void _PhysicsProcess(double delta)
	{
		if (!Active)
		{
			_velocity = Vector2.Zero;   // 关掉时清掉跨帧速度：下次启用从静止重新起步
			return;
		}

		_enemy ??= GetParent<SampleEnemy>();
		if (_enemy == null || !GodotObject.IsInstanceValid(_enemy)) return;
		if (_enemy.IsDead || _enemy.IsDeathSequenceActive) return;

		var player = _enemy.PlayerTarget;
		if (player == null || !GodotObject.IsInstanceValid(player))
			player = GetTree()?.GetFirstNodeInGroup("player") as SamplePlayer;   // 基类引用只在检测查询里刷新，这里自己解析
		if (player == null) return;

		_rail ??= SlideRailMount.FindFor(_enemy);
		if (_rail == null || !GodotObject.IsInstanceValid(_rail))
		{
			if (!_missingRailWarned)
			{
				_missingRailWarned = true;
				GD.PushWarning($"{Name}: 未挂在滑槽（SlideRailMount）下，沿轨保持距离不会生效");
			}
			return;
		}
		if (!_rail.IsResolved) _rail.ResolveNow();   // 首物理帧解析（与机械侧同一约定）

		float dt = (float)delta;
		var axis = _rail.CarriageAxis;

		float selfCoord = axis == SlideRailMount.RailAxis.X ? _enemy.GlobalPosition.X : _enemy.GlobalPosition.Y;
		float playerCoord = axis == SlideRailMount.RailAxis.X ? player.GlobalPosition.X : player.GlobalPosition.Y;
		float dx = playerCoord - selfCoord;

		float keep = KeepDistance > 0f
			? KeepDistance
			: _enemy.BehaviorConfig?.MinComfortDistance ?? 0f;
		if (keep <= 0f)
		{
			if (!_noDistanceWarned) { _noDistanceWarned = true; GD.PushWarning($"{Name}: 间距 N 解析为 0（KeepDistance 与 BehaviorConfig.MinComfortDistance 都没配）——本组件不做事"); }
			return;
		}

		float speed = Speed > 0f
			? Speed
			: _enemy.Speed;
		if (speed <= 0f) return;

		// 滑槽轴：对齐玩家（龙门纵向）。滑槽自己恒速滑 + 每帧硬钳，这里只管下发目标；
		// 无行程的滑槽由 SetTarget 自行忽略。
		_rail.SetTarget(_rail.GetCoordinate(player.GlobalPosition));

		// 机械轴：目标 = 玩家往自己这侧留 N（太远 → 目标在前方；太近 → 目标在身后）
		float side = dx >= 0f ? 1f : -1f;
		float targetCoord = playerCoord - side * keep;
		float error = targetCoord - selfCoord;   // 与 dx 同号 = 太远，反号 = 太近

		float deadzone = Mathf.Max(MinDeadzone, speed / Mathf.Max(1f, Engine.PhysicsTicksPerSecond));

		// 期望速度：与误差成正比（越接近 N 越慢）、上限 Speed；带内期望 0（只留浮点微动）
		float desired = Mathf.Abs(error) <= deadzone ? 0f : Mathf.Clamp(error * Smoothing, -speed, speed);

		// 一阶滞后：实际速度朝期望速度靠，加速度 = Speed × Smoothing（静止 → 满速刚好 1/Smoothing 秒）。
		// 起步/收住/换向都因此是渐变的（P2 跟随的平滑度，只是把位置收敛换成了速度）。
		float accel = speed * Mathf.Max(0.1f, Smoothing);
		_velocity = _velocity.MoveToward(
			axis == SlideRailMount.RailAxis.X ? new Vector2(desired, 0f) : new Vector2(0f, desired),
			accel * dt);
		_enemy.Velocity = _velocity;

		// 位移提交：Attack 状态下 EnemyAttackState 每帧已经替模板调过一次 MoveAndSlide ✗ 再调会让速度翻倍；
		// 其余状态（Idle/Walk…）没人提交 —— 由本组件自己提交（magnet 的同款判据）。
		if (!IsAttackStateActive()) _enemy.MoveAndSlide();

		if (EnableDebugLogs)
		{
			string action = Mathf.IsZeroApprox(desired) ? "保持" : desired * side > 0f ? "靠近" : "退避";
			float axisSpeed = _velocity.X + _velocity.Y;
			GD.Print($"{Name}: 沿轨{action}（|dx|={Mathf.Abs(dx):F0} → N={keep:F0}，速度 {axisSpeed:F0}/{speed:F0}）");
		}
	}

	/// <summary>宿主是否正处在 Attack 状态（那时位移由攻击状态提交，本组件不能再调 MoveAndSlide）。</summary>
	private bool IsAttackStateActive()
		=> (_enemy?.StateMachine?.CurrentState?.Name ?? string.Empty) == "Attack";
}
