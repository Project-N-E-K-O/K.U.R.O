using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// 轨道类炮台（Cannon / MachineGun）的 SimpleMeleeAttack：射出的弹体（<see cref="EnergyBullet"/> /
	/// <see cref="EnergyOrbShot"/>）挂到**玩家父级（World）**，与玩家同属一个 y-sorted 父节点，按 Y 排序
	/// （与罐子 <see cref="JarAssistAttack"/> 的父级处理同一套）。
	/// 其他特效（蓄力/枪口闪光等）仍走基类——挂在敌人父级、保留原行为。
	/// </summary>
	public partial class EnemyF1RogueAITurretSimpleMeleeAttack : EnemySimpleMeleeAttack
	{
		protected override Node? ResolveEffectParent(AttackEffectEntry? entry, Node2D effect)
		{
			if (effect is EnergyBullet or EnergyOrbShot)
			{
				var player = Enemy?.PlayerTarget;
				if (player != null && GodotObject.IsInstanceValid(player) && player.GetParent() is Node world)
					return world;

				if (GetTree()?.GetFirstNodeInGroup("player") is Node2D groupPlayer && groupPlayer.GetParent() is Node groupWorld)
					return groupWorld;
			}

			return base.ResolveEffectParent(entry, effect);
		}
	}
}
