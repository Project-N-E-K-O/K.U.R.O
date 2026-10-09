using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	/// <summary>
	/// RogueAIOverload（本体大招）的**竖向激光墙**：与 LaserBeamA / LaserBeamPlayerWeapon 平级，
	/// 视觉与计时全部继承 <see cref="LaserBeamVisualBase"/>（Grow→Beam→Fade + 光点独立生命周期）。
	/// 与 LaserBeamA 的三点不同（所以是独立子类，而不是给通用激光加开关）：
	///   1. **方向固定竖直**（<see cref="AngleDegrees"/>，默认 90 = 向下），不瞄准、不随敌人朝向翻转；
	///   2. **跟随生成锚点**（<see cref="IFollowAnchor"/>，生成方注入 marker）——跟着本体横扫；
	///   3. **伤害按 <see cref="DamageTickInterval"/> 周期性结算**（扫过去的激光墙要能反复命中），
	///      且**不做"首个目标截断"**：激光墙应贯穿整个高度，不被半路的家具/目标截短。
	/// 出现时机与存活时长交给 AttackEffectEntry 的阶段绑定（OnActive 生成、OnActiveEnd 销毁）。
	/// </summary>
	public partial class RogueAIOverloadBeam : LaserBeamVisualBase, IFollowAnchor, IAttackerProvider
	{
		[ExportCategory("Direction 方向")]
		/// <summary>光束方向（度）：90 = 垂直向下（默认）。旋转只作用于视觉层与判定带，根节点恒 0。</summary>
		[Export(PropertyHint.Range, "-180,180,1")] public float AngleDegrees { get; set; } = 90f;

		[ExportCategory("Follow 跟随")]
		/// <summary>每帧同步到产生它的锚点（生成方注入 marker/敌人根）——竖光束跟着本体横扫用。</summary>
		[Export] public bool SyncToAnchor { get; set; } = true;
		/// <summary>产生它的锚点（EnemyAttackTemplate 生成时注入）。</summary>
		public Node2D? FollowAnchor { get; set; }

		[ExportCategory("Damage")]
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions TargetableFactions = TargetableFactions.Player;
		[Export] public bool AllowSelfDamage { get; set; } = false;
		[Export(PropertyHint.Range, "0,500,1")] public int Damage = 0;
		/// <summary>重复伤害间隔（秒）：每过这段时间清空一次"已伤害"账本，同一目标可被反复命中。
		/// 0 = 每束只打一次。</summary>
		[Export(PropertyHint.Range, "0,3,0.05")] public float DamageTickInterval { get; set; } = 0.5f;
		/// <summary>命中目标后截断（不可穿透）：像 <see cref="LaserBeamA"/> 那样——视觉截到最近**可命中**目标的
		/// 近边、伤害也只结算到首个目标（连同排）；判定带保持全长（带跟着截断会在下一帧丢掉首个目标 → 反复伸缩）。
		/// 默认关：过载激光墙本来就该贯穿整个高度。</summary>
		[Export] public bool TruncateOnHit { get; set; }

		[ExportCategory("Knockback")]
		/// <summary>击退距离（沿光束轴向 = 竖直方向；横光束那样只取水平分量的旧规则不适用于本条）。</summary>
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

		/// <summary>攻击来源（生成方注入；用于自伤保护）。</summary>
		public GameActor? Attacker { get; set; }

		/// <summary>**视觉层**（Visual 子节点）的绘制层级：只影响画出来的那棵子树，
		/// 根节点与判定带不动（判定带不参与绘制；根节点的 z 会把它一起改，语义上不该碰）。
		/// 根下没有 Visual 子节点时退化为写根节点。</summary>
		public int VisualZIndex
		{
			get => _visual != null ? _visual.ZIndex : ZIndex;
			set
			{
				if (_visual != null) _visual.ZIndex = value;
				else ZIndex = value;
			}
		}

		/// <summary>已伤害目标（去重账本）：按 DamageTickInterval 清空。</summary>
		private readonly HashSet<ulong> _damaged = new();
		private float _damageTickTimer;

		/// <summary>单帧候选目标（阵营/方向过滤后的真实接收者 + 沿光束轴的近边距离）。只在 TruncateOnHit 开启时收集。</summary>
		private readonly List<(Node Receiver, float NearEdge)> _targets = new();
		/// <summary>单帧截断距离 = 首个可命中目标的近边；无目标 = 不截断（视觉与伤害共用同一判据）。</summary>
		private float _stopDistance = float.MaxValue;
		/// <summary>接触线容差（像素）：近边差在此范围内的目标视为"同一排"，一并命中。</summary>
		private const float StopEdgeTolerance = 2f;

		/// <summary>已落到视觉层/判定带的角度，用于检测 AngleDegrees 的运行时变化。</summary>
		private float _appliedAngleDegrees = float.NaN;

		/// <summary>本帧是否被目标截断（视觉长度 &lt; 自由长度；仅在 <see cref="TruncateOnHit"/> 开启时有意义）。
		/// 由 <see cref="UpdateBeam"/> 每帧推导——"打在目标上"的附属表现（末端溅射加强等）按它区分。</summary>
		public bool IsTruncated { get; private set; }

		/// <summary>首帧方向：固定 AngleDegrees（不做瞄准、不随朝向翻）。</summary>
		protected override void InitializeDirection() => ApplyDirection();

		/// <summary>把当前 <see cref="AngleDegrees"/> 落到视觉层与判定带（根节点恒 0）。
		/// AngleDegrees 是**活属性**：扇束张开时逐帧改角度，这里按变化重落。</summary>
		private void ApplyDirection()
		{
			float angle = Mathf.DegToRad(AngleDegrees);
			if (_visual != null) _visual.Rotation = angle;
			else Rotation = angle;
			if (_hitArea != null) _hitArea.Rotation = angle;
			_appliedAngleDegrees = AngleDegrees;
		}

		public override void _Process(double delta)
		{
			// 角度变了先重落（本帧的判定带方向要跟着走），再跑基类的生长/伤害
			if (!Mathf.IsEqualApprox(_appliedAngleDegrees, AngleDegrees)) ApplyDirection();

			// 跟随放最前：本帧的命中判定要基于更新后的位置
			if (SyncToAnchor && FollowAnchor != null && GodotObject.IsInstanceValid(FollowAnchor))
				GlobalPosition = FollowAnchor.GlobalPosition;

			// 命中截断：候选目标要先于基类 UpdateBeam（截断用本帧数据，与 PlayerWeapon 同序）
			if (TruncateOnHit) RefreshTargets();

			base._Process(delta);

			// 伤害窗口 = 生长完成 → 全亮结束（淡出不结算，见基类 IsDamageWindowOpen）
			if (!IsDamageWindowOpen) return;

			ApplyDamage();

			if (DamageTickInterval <= 0f) return;
			_damageTickTimer += (float)delta;
			if (_damageTickTimer >= DamageTickInterval)
			{
				_damageTickTimer = 0f;
				_damaged.Clear();
			}
		}

		/// <summary>命中带内所有合法接收者（无截断：激光墙贯穿；走到束内的目标也会被打到）。</summary>
		/// <summary>截断（子类钩子）：先让基类写好本帧长度/宽度/判定带，再把**判定带与视觉两层一起**
		/// 截到同一世界终点——判据来自伤害层（<see cref="_stopDistance"/> 由 <see cref="RefreshTargets"/>
		/// 用伤害谓词收集），所以"能挡光的 = 能挨打的"仍然成立，且两层断在同一条线上。
		/// 两层各自从自己的原点量（视觉层与判定带是两个独立节点、原点可不同）：
		///   · 判定带从判定带原点量：长度 = stop + <see cref="StopEdgeTolerance"/>；
		///   · 视觉从 Visual 原点量：长度 = stop − (Visual偏移 − 判定带偏移)·方向。
		/// 带多留的一格容差是防抖：带正好缩到目标近边，下一帧就丢目标 → 截断消失 → 反复伸缩。
		/// 同时用与截断同一判据记下 <see cref="IsTruncated"/>。</summary>
		protected override void UpdateBeam()
		{
			base.UpdateBeam();
			if (!TruncateOnHit)
			{
				IsTruncated = false;
				return;
			}
			float stop = Mathf.Min(_stopDistance, MaxLength);
			IsTruncated = stop < _currentLength;
			if (IsTruncated)
			{
				// 视觉从 Visual 原点量到同一世界终点（Visual 有偏移时也能切在目标上）
				Vector2 dir = ResolveBeamDir();
				Vector2 originDelta = (_visual?.Position ?? Vector2.Zero) - (_hitArea?.Position ?? Vector2.Zero);
				TruncateBeamVisual(stop - originDelta.Dot(dir));
			}
			TruncateHitBand(IsTruncated ? stop + StopEdgeTolerance : _currentLength);
		}

		/// <summary>判定带跟着截断（带从判定带原点向远端截到给定长度）：高度照旧（DetectionRadius×2）、
		/// 形状居中前移半个长度。</summary>
		private void TruncateHitBand(float bandLength)
		{
			if (_hitShape?.Shape is not RectangleShape2D rs) return;
			float len = Mathf.Max(bandLength, 0f);
			rs.Size = new Vector2(len, DetectionRadius * 2f);
			_hitShape.Position = new Vector2(len * 0.5f, 0f);
		}

		/// <summary>单帧收集：命中带内通过阵营/方向过滤的真实接收者 + 各自沿光束轴的近边距离，并求出截断距离
		/// （最小近边）——本帧视觉截断与伤害筛选共用同一结果（所见即所伤）。只在 <see cref="TruncateOnHit"/> 开启时调用。</summary>
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
			foreach (var body in _hitArea.GetOverlappingBodies())
				AddTarget(body, origin, beamDir);
		}

		private void AddTarget(Node collider, Vector2 origin, Vector2 beamDir)
		{
			// 目标可能刚被 QueueFree、而物理重叠表还滞后一两帧 → 跳过已失效对象（项目同款保护）
			if (!GodotObject.IsInstanceValid(collider)) return;
			// 发射者自己不算目标（TargetableFactions 含 Enemy 时，本体的判定区就在光束起点附近）
			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(collider, Attacker)) return;
			if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver) return;
			// 方向性目标（FireWallA 等屏障）拒收本方向时视为未命中 → 穿透：不伤害、也不构成遮挡
			if (!DamageDispatcher.AcceptsAttackDirection(collider, beamDir, TargetableFactions, origin)) return;

			float nearEdge = DistanceAlongAxisToNearEdge(collider, origin, beamDir);
			if (nearEdge < _stopDistance) _stopDistance = nearEdge;
			_targets.Add((receiver, nearEdge));
		}

		private void ApplyDamage()
		{
			if (_hitArea == null) return;
			if (Damage <= 0 && KnockbackDistance <= 0f) return;

			Vector2 beamDir = ResolveBeamDir();

			// 不可穿透：只结算截断距离内的目标（首个目标及其同排）——与视觉截断同一判据
			if (TruncateOnHit)
			{
				float stop = Mathf.Min(_stopDistance, MaxLength);
				foreach (var (receiver, nearEdge) in _targets)
				{
					if (nearEdge > stop + StopEdgeTolerance) continue;
					TryDamageReceiver(receiver, beamDir);
				}
				return;
			}

			// 贯穿（默认）：带内所有合法接收者
			foreach (var area in _hitArea.GetOverlappingAreas())
			{
				// 刚被销毁的对象可能还留在滞后的重叠表里 → 跳过（碰 .Name 前先验有效性）
				if (!GodotObject.IsInstanceValid(area)) continue;
				if (area.Name != "HitArea" && area.Name != "TriggerArea") continue;
				TryDamage(area, beamDir);
			}
			foreach (var body in _hitArea.GetOverlappingBodies())
			{
				if (!GodotObject.IsInstanceValid(body)) continue;
				TryDamage(body, beamDir);
			}
		}

		private void TryDamage(Node collider, Vector2 beamDir)
		{
			// 发射者自己不算目标（TargetableFactions 含 Enemy 时，本体的判定区就在光束起点附近，
			// 不排除会把自己打一遍）
			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(collider, Attacker)) return;
			if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver) return;
			TryDamageReceiver(receiver, beamDir);
		}

		private void TryDamageReceiver(Node receiver, Vector2 beamDir)
		{
			if (!_damaged.Add(receiver.GetInstanceId())) return;

			bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, Attacker,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, null, beamDir);
			if (!dealt) return;

			// 击退沿光束轴向（本条 = 竖直）——目标被从光束起点往外推
			if (receiver is GameActor actor && KnockbackDistance > 0f)
				actor.ApplyKnockbackDisplacement(beamDir, KnockbackDistance, KnockbackDuration);
		}

		/// <summary>光束轴向：判定带旋转即方向（竖向 (0,±1)、横向 (±1,0) 通用）。</summary>
		private Vector2 ResolveBeamDir()
		{
			float angle = _hitArea?.Rotation ?? Mathf.DegToRad(AngleDegrees);
			Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
			return dir.LengthSquared() < 0.0001f ? Vector2.Down : dir.Normalized();
		}
	}
}
