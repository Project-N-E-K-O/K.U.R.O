using Godot;
using Kuros.Core.Effects;

namespace Kuros.Environments
{
	/// <summary>
	/// rogueAI 的 down 阶段护甲：在多阶段受损物之上增加"命中门"。
	/// 收起时只关 HitArea + <see cref="CanBeAffected"/> 恒 false——玩家攻击/组扫描
	/// 特效都找不到它，避免在 up 阶段被提前拆掉；**视觉与命中判定解耦**（<see cref="SetArmorVisible"/>），
	/// part2 要求护甲全程显示、仅 down 之后可被攻击。
	/// 击破仍走基类终态（禁命中、播最后一段 BreakAnimation、留尸），由
	/// EnemyF1RogueAIPart2 监听死亡打开本体伤害门。
	/// </summary>
	public partial class RogueAIBumper : DestructibleStageController
	{
		private bool _active;

		/// <summary>启用/收起护甲的**命中判定**（幂等）：收起 = HitArea 关闭 + CanBeAffected 恒 false。
		/// 视觉不归这里管，见 <see cref="SetArmorVisible"/>。</summary>
		public void SetArmorActive(bool active)
		{
			_active = active;

			if (GetNodeOrNull<Area2D>("HitArea") is not { } hitArea) return;
			hitArea.SetDeferred(Area2D.PropertyName.Monitoring, active);
			hitArea.SetDeferred(Area2D.PropertyName.Monitorable, active);
			foreach (var child in hitArea.GetChildren())
				if (child is CollisionShape2D hitShape)
					hitShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, !active);
		}

		/// <summary>护甲视觉显隐（与命中判定解耦）：part2 要求 up 阶段也全程显示，只锁伤害。</summary>
		public void SetArmorVisible(bool visible)
		{
			Visible = visible;
		}

		/// <summary>收起状态下拒绝一切伤害（HitArea 关闭只挡碰撞索敌；组扫描类特效靠这道门拦）。</summary>
		public override bool CanBeAffected(ActorEffect? effect) => _active && base.CanBeAffected(effect);
	}
}
