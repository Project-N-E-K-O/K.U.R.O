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

		[ExportCategory("Knockback")]
		/// <summary>击退距离（沿光束轴向 = 竖直方向；横光束那样只取水平分量的旧规则不适用于本条）。</summary>
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

		/// <summary>攻击来源（生成方注入；用于自伤保护）。</summary>
		public GameActor? Attacker { get; set; }

		/// <summary>已伤害目标（去重账本）：按 DamageTickInterval 清空。</summary>
		private readonly HashSet<ulong> _damaged = new();
		private float _damageTickTimer;

		/// <summary>已落到视觉层/判定带的角度，用于检测 AngleDegrees 的运行时变化。</summary>
		private float _appliedAngleDegrees = float.NaN;

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
		private void ApplyDamage()
		{
			if (_hitArea == null) return;
			if (Damage <= 0 && KnockbackDistance <= 0f) return;

			Vector2 beamDir = ResolveBeamDir();
			foreach (var area in _hitArea.GetOverlappingAreas())
			{
				if (area.Name != "HitArea" && area.Name != "TriggerArea") continue;
				TryDamage(area, beamDir);
			}
			foreach (var body in _hitArea.GetOverlappingBodies())
				TryDamage(body, beamDir);
		}

		private void TryDamage(Node collider, Vector2 beamDir)
		{
			// 发射者自己不算目标（TargetableFactions 含 Enemy 时，本体的判定区就在光束起点附近，
			// 不排除会把自己打一遍）
			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(collider, Attacker)) return;
			if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver) return;
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
