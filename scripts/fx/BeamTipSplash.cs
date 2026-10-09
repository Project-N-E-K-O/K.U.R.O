using Godot;

namespace Kuros.Fx
{
	/// <summary>
	/// 光束末端溅射（粒子节点，挂在束场景内、随束复制——扇束 N 条束 = N 个溅射）：
	/// 每帧把自己定位到父束的**当前视觉末端**（<see cref="LaserBeamVisualBase.CurrentLength"/> 已含命中截断）——
	/// 打中目标时火花自动喷在截断点上并持续粘着目标，目标消失自动跟回自由端，全程无需检测。
	/// 朝向：节点转到光束角 + 180°，粒子沿局部 +X（背向束轴）喷出，锥角/速度/重力在场景的粒子材质里调。
	/// 状态：前摇（<see cref="LaserBeamVisualBase.BeamPhaseElapsed"/> &lt; 0）内不发射；
	/// 父束被截断（<see cref="RogueAIOverloadBeam.IsTruncated"/>）期间发射量/速度加强——推导，无导出。
	/// </summary>
	public partial class BeamTipSplash : GpuParticles2D
	{
		// 命中加强：AmountRatio 只能 0~1，所以用"平时欠发射、命中拉满"做量差
		private const float IdleAmountRatio = 0.7f;
		private const float HitSpeedScale = 1.5f;

		private RogueAIOverloadBeam? _beam;

		public override void _Ready()
		{
			_beam = GetParent() as RogueAIOverloadBeam;
			Emitting = false;
			if (_beam == null)
				GD.PushWarning($"{Name}: 父节点不是 RogueAIOverloadBeam，溅射不跟随末端");
		}

		public override void _Process(double delta)
		{
			// 父束销毁后本节点也会一起销毁，这里只兜住极端帧
			if (_beam == null || !GodotObject.IsInstanceValid(_beam))
			{
				Emitting = false;
				return;
			}

			// 前摇内（阶段时钟为负）光束还没开始 → 不喷
			if (_beam.BeamPhaseElapsed < 0f)
			{
				Emitting = false;
				return;
			}

			// 束根恒不旋转（旋转落在 Visual / 判定带上）→ 局部坐标即世界方向
			float rad = Mathf.DegToRad(_beam.AngleDegrees);
			Position = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * Mathf.Max(_beam.CurrentLength, 0f);
			Rotation = rad + Mathf.Pi;   // 局部 +X 指向束轴反向 = 向后溅射

			bool hit = _beam.IsTruncated;
			AmountRatio = hit ? 1f : IdleAmountRatio;
			SpeedScale = hit ? HitSpeedScale : 1f;
			Emitting = true;
		}
	}
}
