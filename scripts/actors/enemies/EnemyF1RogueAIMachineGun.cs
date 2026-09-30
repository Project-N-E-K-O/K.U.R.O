using Godot;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// F1 机枪台（Enemy_F1_rogueAI_MachineGun）：机械桩固定炮位，与炮台同结构——锁定朝向、按玩家位置转身、
	/// 蓄力/开火/收招走 preheat/attack/recover，攻击期间沿轨维持间距。差异只在攻击特效（连发弹幕）。
	/// 全部炮位逻辑在 <see cref="EnemyF1RogueAITurret"/> 里，本类只用于把场景与类型对上。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIMachineGun : EnemyF1RogueAITurret
	{
	}
}
