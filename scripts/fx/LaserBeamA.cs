using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	/// <summary>
	/// 激光束（敌人通用）：视觉/计时继承 <see cref="LaserBeamVisualBase"/>（光束 Grow→Beam→Fade、
	/// 光点独立生命周期），本类负责：朝向初始化 + 自动瞄准（AutoAimAtPlayer）、
	/// 伤害/击退（TargetableFactions 阵营过滤）、**首个目标截断（不可穿透）**——
	/// 光束长度 = 命中带内最近可命中目标沿光束轴的近边距离（玩家躲家具后：只打到家具，不穿透到玩家）。
	/// </summary>
	public partial class LaserBeamA : LaserBeamVisualBase, IFacingDirectional, IAttackerProvider
	{
		[ExportCategory("Damage")]
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
		[Export] public bool AllowSelfDamage { get; set; } = false;
		[Export(PropertyHint.Range, "0,500,1")] public int Damage = 0;

		[ExportCategory("Knockback")]
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

		[ExportCategory("Targeting")]
		/// <summary>自动瞄准：射出前物理查询前方可攻击对象，光束向目标微倾斜。false = 保持 FacingRight 水平方向。</summary>
		[Export] public bool AutoAimAtPlayer = true;
		/// <summary>垂直倾斜上限（度）：水平基础方向固定，仅在 ±此值内跟随目标高度微调（只影响视觉，判定带保持水平）。</summary>
		[Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 5f;
		/// <summary>水平基础朝向：true = 向右（0°），false = 向左（180°）。由生成方（EnemyAttackTemplate）按敌人朝向设置。</summary>
		[Export] public bool FacingRight { get; set; } = true;

		private GameActor? _attacker;

		/// <summary>已伤害目标（跨帧去重）：光束持续阶段每帧检测，每目标每光束最多一次伤害。</summary>
		private readonly HashSet<ulong> _damaged = new();

		/// <summary>单帧候选目标（阵营过滤后的真实接收者 + 沿光束轴的近边距离）。</summary>
		private readonly List<(Node Receiver, float NearEdge)> _targets = new();
		/// <summary>单帧截断距离 = 首个可命中目标的近边；无目标 = 不截断。视觉与伤害共用同一判据。</summary>
		private float _stopDistance = float.MaxValue;
		/// <summary>接触线容差（像素）：近边差在此范围内的目标视为"同一排"，一并命中。</summary>
		private const float StopEdgeTolerance = 2f;

		public override void _Ready()
		{
			base._Ready();
			ResolveAttacker();
		}

		/// <summary>
		/// 首帧方向初始化：按 FacingRight 设基础朝向；启用自动瞄准时对目标微倾斜。
		/// </summary>
		protected override void InitializeDirection()
		{
			// 根恒 0 旋转（旋转只作用于 Visual/判定带）：初始方向按 FacingRight
			float initAngle = FacingRight ? 0f : Mathf.Pi;
			if (_visual != null) _visual.Rotation = initAngle;
			else Rotation = initAngle; // 兼容无 Visual 节点的旧结构
			if (_hitArea != null) _hitArea.Rotation = initAngle;

			if (AutoAimAtPlayer && FindAimTarget() is Vector2 aimTarget)
				AimHorizontalWithVerticalTilt(aimTarget);
		}

		public void AimHorizontalWithVerticalTilt(Vector2 globalTarget)
		{
			Vector2 toTarget = globalTarget - GlobalPosition;
			float baseAngle = FacingRight ? 0f : Mathf.Pi;
			bool front = FacingRight ? toTarget.X >= 0f : toTarget.X <= 0f;
			float tilt = 0f;
			if (front && toTarget != Vector2.Zero)
			{
				float maxR = Mathf.DegToRad(MaxVerticalTiltDegrees);
				float dySign = FacingRight ? 1f : -1f;
				tilt = Mathf.Atan2(toTarget.Y * dySign, Mathf.Abs(toTarget.X));
				tilt = Mathf.Clamp(tilt, -maxR, maxR);
			}
			// 旋转只作用于视觉层与判定带（根恒 0 旋转）：判定带跟随瞄准角度旋转，与视觉光束方向一致
			float angle = baseAngle + tilt;
			if (_visual != null) _visual.Rotation = angle;
			else Rotation = angle;
			if (_hitArea != null) _hitArea.Rotation = angle;
		}

		public void LookAtGlobal(Vector2 globalTarget)
		{
			Vector2 dir = (globalTarget - GlobalPosition).Normalized();
			if (dir == Vector2.Zero) return;
			if (_visual != null) _visual.Rotation = dir.Angle();
			else Rotation = dir.Angle();
		}

		/// <summary>
		/// 单帧收集：命中带内、通过阵营过滤的真实接收者 + 各自沿光束轴的近边距离，
		/// 并求出截断距离（最小近边）——本帧视觉截断与伤害筛选共用同一结果（所见即所伤）。
		/// 判定带保持全长（基类 UpdateBeam），截断只作用于视觉与伤害筛选：
		/// 若连带一起截断，下一帧带不再覆盖首个目标 → 截断消失 → 反复伸缩抖动。
		/// </summary>
		private void RefreshTargets()
		{
			_targets.Clear();
			_stopDistance = float.MaxValue;
			if (_hitArea == null) return;
			if (Damage <= 0 && KnockbackDistance <= 0f) return;

			Vector2 beamDir = ResolveBeamDir();
			Vector2 origin = _hitArea.GlobalPosition;

			// Area 目标：只接受受击判定区（HitArea/TriggerArea），玩家攻击/交互 Area 探入光束不触发
			foreach (var area in _hitArea.GetOverlappingAreas())
			{
				if (area.Name != "HitArea" && area.Name != "TriggerArea") continue;
				AddTarget(area, origin, beamDir);
			}
			// Body 目标（DestructibleObject 等 StaticBody2D）
			foreach (var body in _hitArea.GetOverlappingBodies())
				AddTarget(body, origin, beamDir);
		}

		private void AddTarget(Node collider, Vector2 origin, Vector2 beamDir)
		{
			if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver) return;
			// 方向性目标（FireWallA 等屏障）拒收本方向时视为未命中 → 穿透：不伤害、也不构成遮挡
			// （与 DealDamage 同判据；投掷物侧同语义——放行方向继续飞）
			if (!DamageDispatcher.AcceptsAttackDirection(collider, beamDir, TargetableFactions, origin)) return;
			float nearEdge = DistanceAlongAxisToNearEdge(collider, origin, beamDir);
			if (nearEdge < _stopDistance) _stopDistance = nearEdge;
			_targets.Add((receiver, nearEdge));
		}

		/// <summary>伤害结算：仅命中截断距离内的目标（首个目标及其同排）——其后的目标被遮挡，不穿透。</summary>
		private void ApplyDamage()
		{
			if (_hitArea == null) return;
			if (Damage <= 0 && KnockbackDistance <= 0f) return;

			float stop = Mathf.Min(_stopDistance, MaxLength);
			Vector2 beamDir = ResolveBeamDir();
			foreach (var (receiver, nearEdge) in _targets)
			{
				if (nearEdge > stop + StopEdgeTolerance) continue;
				TryDamageReceiver(receiver, beamDir);
			}
		}

		/// <summary>光束轴向（俯视角取水平分量；判定带旋转 = 瞄准角度驱动）。</summary>
		private Vector2 ResolveBeamDir()
		{
			float beamAngle = _hitArea?.Rotation ?? (FacingRight ? 0f : Mathf.Pi);
			Vector2 beamDir = new(Mathf.Cos(beamAngle), 0f);
			return beamDir == Vector2.Zero ? new Vector2(FacingRight ? 1f : -1f, 0f) : beamDir;
		}

		/// <summary>视觉截断：光束只画到截断距离（首个目标近边）——判定带保持全长，仅视觉层缩短。</summary>
		protected override void UpdateBeam()
		{
			base.UpdateBeam();
			TruncateBeamVisual(_stopDistance);
		}

		public override void _Process(double delta)
		{
			// 伤害窗口只在"生长完成 → 全亮结束"内：淡出阶段视觉已消失，不再判定。
			// 同时停止 RefreshTargets —— 保留上一帧的截断距离，视觉不会因判定关闭而突然变长。
			//
			// 目标检索放在 base._Process 之前（UpdateBeam 要用本帧的截断距离）。因此这里一旦抛异常，
			// 基类就整帧不执行：计时器不减、_beamPhaseElapsed 冻在 GrowDuration、窗口永远开着 —— 光束永生。
			// 故两者都包住：异常只让本帧检索/结算失效，计时与销毁照常（异常只记录一次，避免刷屏）。
			bool windowOpen = IsDamageWindowOpen;
			if (windowOpen)
			{
				try { RefreshTargets(); }
				catch (System.Exception ex) { LogTargetError("RefreshTargets", ex); }
			}

			base._Process(delta);

			if (windowOpen)
			{
				try { ApplyDamage(); }
				catch (System.Exception ex) { LogTargetError("ApplyDamage", ex); }
			}
		}

		/// <summary>目标检索/结算异常只记录一次（含堆栈），之后静默防刷屏。</summary>
		private bool _targetErrorLogged;
		private void LogTargetError(string phase, System.Exception ex)
		{
			if (_targetErrorLogged) return;
			_targetErrorLogged = true;
			GD.PushError($"[LaserBeamA {GetInstanceId()}] {phase} 抛异常（此后静默）: {ex}");
		}

		private void TryDamageReceiver(Node receiver, Vector2 beamDir)
		{
			// 跨帧去重：每目标每光束最多一次伤害
			if (!_damaged.Add(receiver.GetInstanceId())) return;

			bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, _attacker,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, null, beamDir);
			if (!dealt) return;

			// 击退只对 GameActor（WorldItem 无速度概念）
			if (receiver is GameActor actor)
			{
				if (KnockbackDistance > 0f)
					actor.ApplyKnockbackDisplacement(beamDir, KnockbackDistance, KnockbackDuration);
			}
		}

		/// <summary>
		/// 瞄准目标：玩家组查找（同 DamageDispatcher.DealDamageFromArea 玩家发现先例）。
		/// 物理查询受带高/距离/方向限制，大部分时候检测不到玩家——瞄准必须全局可靠。
		/// 目标在光束背后（FacingRight 反侧）时由 AimHorizontalWithVerticalTilt 的 front 检查保持水平。
		/// </summary>
		private Vector2? FindAimTarget()
		{
			var player = GetTree().GetFirstNodeInGroup("player");
			return player is GameActor ga ? GetAimCenter(ga) : null;
		}

		/// <summary>取目标 HitArea CollisionShape2D 的世界坐标作为瞄准中心。</summary>
		private static Vector2 GetAimCenter(GameActor actor)
		{
			var hitArea = actor.GetNodeOrNull<Area2D>("HitArea")
				?? actor.FindChild("HitArea", recursive: true, owned: false) as Area2D;
			var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			return hitShape?.GlobalPosition ?? hitArea?.GlobalPosition ?? actor.GlobalPosition;
		}

		/// <summary>
		/// 显式攻击来源（由生成方传入，如 EnemyAttackTemplate 生成时设置）。
		/// 优先于父节点解析：父节点下第一个敌人不一定是发射者，解析错误会导致 AllowSelfDamage 保护失效（打自己）。
		/// </summary>
		public GameActor? Attacker
		{
			get => _attacker;
			set => _attacker = value;
		}

		private void ResolveAttacker()
		{
			if (_attacker != null) return;
			var parent = GetParent();
			if (parent == null) return;
			foreach (var child in parent.GetChildren())
			{ if (child.IsInGroup("enemies") && child is GameActor ga) { _attacker = ga; break; } }
		}
	}
}
