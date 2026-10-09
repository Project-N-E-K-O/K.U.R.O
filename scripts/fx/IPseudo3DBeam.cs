namespace Kuros.Fx
{
	/// <summary>伪体积光束（能力接口）：<see cref="FanBeam"/> 按自己的时间轴与调参下发伪体积参数，
	/// 只有实现了它的子束才有伪 3D 行为——时间轴（什么时候拉起权重）与数值都归扇束，
	/// 光束只负责把参数落到自己的材质上。与 <see cref="IFollowAnchor"/> / <see cref="IAttackerProvider"/> 同一套路：
	/// 扇束不需要知道子束具体是哪个类（换别的光束场景照样生效）。
	/// </summary>
	public interface IPseudo3DBeam
	{
		/// <summary>伪体积权重（0 = 平面，1 = 全量）；由扇束按时间轴下发（本特效：段 3 内 0→1）。</summary>
		float Pseudo3DWeight { get; set; }

		/// <summary>透视锥化的倾斜角（度，权重 1 时生效）：光束朝镜头倾多少，越大远端张得越猛。</summary>
		float Pseudo3DTiltDegrees { get; set; }

		/// <summary>透视强度（度，即 shader 的 fov）：越小越接近正交，越大透视越强。</summary>
		float Pseudo3DFovDegrees { get; set; }

		/// <summary>转动高光强度（权重 1 时生效）。</summary>
		float Pseudo3DSpinStrength { get; set; }
	}
}
