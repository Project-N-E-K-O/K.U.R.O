using Godot;

namespace Kuros.Fx
{
	/// <summary>
	/// "伪体积"光束（**FanBeam 专用**；过载激光墙仍用平面的 <see cref="RogueAIOverloadBeam"/>）：
	/// 把扇束下发的伪 3D 权重（<see cref="Pseudo3DWeight"/>，本特效里**段 1/段 2/停顿恒 0、段 3 内 0→1**）
	/// 落到材质上——
	///   `volume`（**透视锥化的整体过渡**：0 = 平面 → 1 = 满强度，线性混合；fov / tilt_deg 决定满强度的形状——
	///    宽度沿长度按**直边梯形**展开，末端倍率 r = 1/(1 − k)，k = 0.95·raw/(raw+1)
	///    （raw = 2·tan(fov/2)·sin(tilt)）——软饱和、无骤跳）
	///   `spin_strength`（转动高光，"旋转"的可见强度，× 权重）。
	/// 所以段 3 里是**边延伸边越来越宽、转动越来越明显**；权重 0 时与平面光束逐像素一致。
	/// 锥化是零投影实现的（顶点加高 + 片元按 UV.x 反向缩放），长度方向的取样不压缩；
	/// 几何/判定/tick 结算全部照旧（视觉与伤害分离）。
	/// </summary>
	public partial class RogueAIOverloadBeamSpin : RogueAIOverloadBeam, IPseudo3DBeam
	{
		/// <summary>伪 3D 权重（0 = 平面，1 = 全量）；由 <see cref="FanBeam"/> 按时间轴下发。</summary>
		public float Pseudo3DWeight { get; set; }

		/// <summary>透视锥化倾斜角（度）；由 <see cref="FanBeam"/> 下发。</summary>
		public float Pseudo3DTiltDegrees { get; set; } = 35f;

		/// <summary>透视强度（= shader 的 fov）；由 <see cref="FanBeam"/> 下发。</summary>
		public float Pseudo3DFovDegrees { get; set; } = 45f;

		/// <summary>转动高光强度；由 <see cref="FanBeam"/> 下发。</summary>
		public float Pseudo3DSpinStrength { get; set; } = 0.7f;

		private ShaderMaterial? _glowMaterial;
		private ShaderMaterial? _beamMaterial;

		public override void _Ready()
		{
			base._Ready();

			// 基类 _Ready 已按实例复制过材质（防止多实例互相串 fade）→ 拿副本写参数
			_glowMaterial = _glowSprite?.Material as ShaderMaterial;
			_beamMaterial = _beamSprite?.Material as ShaderMaterial;
			ApplyParams();
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			ApplyParams();
		}

		private void ApplyParams()
		{
			// fov / tilt 决定"满强度"的形状（全量下发，权重不乘进去——乘进去在大 fov 下会前段猛、后段钝、起点跳变）；
			// 整条效果的过渡走 volume（0 = 平面 → 1 = 满强度线性混合）。spin 高光仍按权重拉起来。
			// 其余材质项（spin_speed / spin_width / depth_falloff / far_tint）保持材质上的值，脚本不碰。
			float spin = Pseudo3DSpinStrength * Pseudo3DWeight;
			_glowMaterial?.SetShaderParameter("tilt_deg", Pseudo3DTiltDegrees);
			_glowMaterial?.SetShaderParameter("fov", Pseudo3DFovDegrees);
			_glowMaterial?.SetShaderParameter("volume", Pseudo3DWeight);
			_glowMaterial?.SetShaderParameter("spin_strength", spin);
			_beamMaterial?.SetShaderParameter("tilt_deg", Pseudo3DTiltDegrees);
			_beamMaterial?.SetShaderParameter("fov", Pseudo3DFovDegrees);
			_beamMaterial?.SetShaderParameter("volume", Pseudo3DWeight);
			_beamMaterial?.SetShaderParameter("spin_strength", spin);
		}
	}
}
