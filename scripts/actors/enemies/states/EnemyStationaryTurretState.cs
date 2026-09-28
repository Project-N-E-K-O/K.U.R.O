using Godot;

namespace Kuros.Actors.Enemies.States
{
	/// <summary>
	/// 机械桩炮台的静止占位状态（Idle / Walk / CooldownFrozen 共用）。
	/// 不做移动、不做状态跳转推断——**不要改回 EnemyIdleState/EnemyWalkState**：那两者会追玩家、
	/// 按 BehaviorConfig 切 CloseIn/KeepDistance，与"钉在桩上、由 FacingController 与沿轨行为驱动"的语义打架
	/// （与 MagnetRailState 同一思路）。
	/// 唯一职责：站住不动 + （可选）轮询"攻击控制器放行了没"并切进 Attack。
	/// </summary>
	public partial class EnemyStationaryTurretState : EnemyState
	{
		/// <summary>是否在本状态里轮询开火（Idle/Walk = true；CooldownFrozen = false）。</summary>
		[Export] public bool AllowAttack { get; set; } = true;

		public override void Enter()
		{
			Enemy.Velocity = Vector2.Zero;
		}

		public override void PhysicsUpdate(double delta)
		{
			// 站住：清零残余速度，但**不**调 MoveAndSlide——位移只由沿轨行为/攻击状态提交
			Enemy.Velocity = Vector2.Zero;

			if (!AllowAttack) return;
			if (Enemy.CanStartAttack())
				ChangeState("Attack");
		}
	}
}
