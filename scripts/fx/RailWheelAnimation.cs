using Godot;

namespace Kuros.Fx
{
	/// <summary>滑轨轮子：按滑轨当前的 Y 轴移动速度驱动 AnimatedSprite2D。
	/// 向下（+Y）正放、向上（-Y）倒放；SpeedScale = |vy| / MaxSpeed，封顶 1；静止时 0（停帧）。</summary>
	public partial class RailWheelAnimation : AnimatedSprite2D
	{
		/// <summary>要循环播放的动画名（SpriteFrames 里的名字）。</summary>
		[Export] public string AnimationName { get; set; } = "default";
		/// <summary>达到满速（SpeedScale = 1）对应的滑轨 Y 速度（px/s）。</summary>
		[Export(PropertyHint.Range, "10,2000,10")] public float MaxSpeed { get; set; } = 400f;

		/// <summary>低于该速度视为静止，避免指数跟随的微小抖动让轮子来回抽帧。</summary>
		private const float StopSpeed = 1f;

		private double _lastY;

		public override void _Ready()
		{
			// 排在滑轨移动之后采样，保证读到的是本帧已经移动过的位置
			ProcessPhysicsPriority = 100;
			_lastY = GlobalPosition.Y;
			if (!IsPlaying())
				Play(AnimationName);
		}

		public override void _PhysicsProcess(double delta)
		{
			if (delta <= 0.0) return;

			double y = GlobalPosition.Y;
			float velocityY = (float)((y - _lastY) / delta);
			_lastY = y;

			float absSpeed = Mathf.Abs(velocityY);
			float scale = absSpeed <= StopSpeed
				? 0f
				: Mathf.Clamp(absSpeed / Mathf.Max(MaxSpeed, 1f), 0f, 1f);
			float direction = velocityY > 0f ? 1f : velocityY < 0f ? -1f : 0f;

			SpeedScale = direction * scale;
		}
	}
}
