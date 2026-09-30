using Godot;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// F1 炮台（Enemy_F1_rogueAI_Cannon）：机械桩固定炮位，攻击特效是"蓄力能量球 → 直线能量弹"。
	/// 全部炮位逻辑（锁定朝向、转身门、镜像前向锚点、沿轨间距、拒绝位移）都在
	/// <see cref="EnemyF1RogueAITurret"/> 里，本类只用于把场景与类型对上；炮台特有的差异走场景配置。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAICannon : EnemyF1RogueAITurret
	{
	}
}
