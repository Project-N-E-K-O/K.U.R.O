using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// F1 机枪台（Enemy_F1_rogueAI_MachineGun）：机械桩固定炮位，与炮台同结构——锁定朝向、按玩家位置转身、
	/// 蓄力/开火/收招走 preheat/attack/recover，攻击期间沿轨维持间距。差异：攻击特效（连发弹幕）+ 贴脸近战招，
	/// 常态免伤（仅攻击窗口可受伤害）已上移到共用基类，两炮台一致。
	/// 全部炮位逻辑在 <see cref="EnemyF1RogueAITurret"/> 里。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIMachineGun : EnemyF1RogueAITurret
	{
		/// <summary>Hit 纯伤害由弱点状态窗口控制，未配置时全程开放；状态效果遵循原判定。</summary>
		public override bool CanBeAffected(ActorEffect? effect)
		{
			if (effect == null && StateMachine?.CurrentState?.Name == "Hit") return IsHitWeakpointOpen();
			return base.CanBeAffected(effect);
		}

		/// <summary>近战招（MeleeAttack / MeleeAttackPro）期间站桩：不沿轨保持间距、也不推滑槽升降——贴脸出招时不该边退边打。
		/// 扫射（SimpleMeleeAttack）不受影响，保持原"攻击期间沿桩退开"的设计。</summary>
		protected override bool ShouldSuppressSpacingForAttack()
			=> IsAttackRunning("MeleeAttack") || IsAttackRunning("MeleeAttackPro");
	}
}
