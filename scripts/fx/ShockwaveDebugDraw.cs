using Godot;

namespace Kuros.Fx
{
	/// <summary>
	/// 冲击波判定调试绘制（高 z 独立子节点，避免被环的 Mesh 盖住）：
	/// 逻辑波前圆、缺口视觉半宽、缺口安全角（含羽化 + 安全边距）、最大半径。
	/// </summary>
	public partial class ShockwaveDebugDraw : Node2D
	{
		public ShockwaveController? Controller { get; set; }

		public override void _Draw()
		{
			Controller?.DrawDamageDebug(this);
		}
	}
}
