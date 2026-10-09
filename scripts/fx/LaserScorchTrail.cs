using System.Collections.Generic;
using Godot;

namespace Kuros.Fx
{
	/// <summary>
	/// 激光**线型焦痕**（连续拖尾，纯视觉）：一条 <see cref="Line2D"/>——放置方每帧把尖端位置
	/// 推给 <see cref="PushTip"/>，折线随之延长；尾端按"点存在时长"逐点淡出并剔除，全淡完自毁。
	/// 因为是**一条折线**（而不是一枚枚印记），所以是连续的焦痕线，没有接头。
	///
	/// · 位置由放置方给定（世界坐标）；场景里 `top_level = true` → 不随敌人/滑槽漂移；
	/// · 逐点透明度走 <see cref="Line2D.Gradient"/>（Godot 4 没有逐点颜色 API）：每帧把各点的
	///   alpha 写进渐变（颜色数 = 点数、偏移均分），于是"头部全亮、尾巴渐隐"；
	/// · 贴图（焦痕质感/噪点）之后添加：给本节点设 `texture` + `texture_mode = Tile` 即可，脚本不用改；
	/// · 笔头停住时不会继续长点（按 <see cref="PointStepPx"/> 节流），于是"光束不扫 → 焦痕自然收尾淡出"。
	/// </summary>
	public partial class LaserScorchTrail : Line2D
	{
		/// <summary>笔头每移动这么多像素才记一个点（太小堆点、太大折线感明显）。</summary>
		[Export(PropertyHint.Range, "1,64,1")] public float PointStepPx { get; set; } = 8f;
		/// <summary>每个点保持全亮的时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float HoldSeconds { get; set; } = 1.0f;
		/// <summary>之后逐点淡出时长（秒）。</summary>
		[Export(PropertyHint.Range, "0.05,10,0.05")] public float FadeSeconds { get; set; } = 1.0f;
		/// <summary>点数上限（超出剔除最老的点）。**要覆盖整条路径**：点数 ≈ 路径长 / PointStepPx，
		/// 不够就会被从线头开始切掉（高速扫过时一条线动辄几百点）。</summary>
		[Export(PropertyHint.Range, "2,512,1")] public int MaxPoints { get; set; } = 512;
		/// <summary>噪点图（之后添加）：喂给材质做"沿线破口"；留空 = 平滑带。
		/// 破口强度 / 噪点密度在材质参数上：`noise_break`（0 = 平滑）、`noise_scale`（1/px）。</summary>
		[Export] public Texture2D? NoiseTexture { get; set; }

		/// <summary>每个点的存在时长（与点一一对应）。</summary>
		private readonly List<float> _ages = new();
		private Vector2 _lastTip;
		private bool _hasTip;

		public override void _Ready()
		{
			// 焦痕线要圆头圆接，避免折角处出现尖角
			JointMode = LineJointMode.Round;
			BeginCapMode = LineCapMode.Round;
			EndCapMode = LineCapMode.Round;
			// 每实例一份渐变（共享资源会把颜色串到别的拖尾上）
			Gradient = new Gradient();
			// 拉伸模式：让 UV.y 稳定地横跨宽度（材质的横截面/破口都靠 UV.y 与 UV.x）
			TextureMode = LineTextureMode.Stretch;
			if (NoiseTexture != null && Material is ShaderMaterial mat)
				mat.SetShaderParameter("noise_texture", NoiseTexture);
		}

		/// <summary>放置方每帧调用：把尖端位置推给拖尾（自动按 <see cref="PointStepPx"/> 节流）。</summary>
		public void PushTip(Vector2 worldPos)
		{
			if (!_hasTip)
			{
				_hasTip = true;
				_lastTip = worldPos;
				AddPoint(worldPos);
				_ages.Add(0f);
				return;
			}
			if (worldPos.DistanceTo(_lastTip) < PointStepPx) return;

			_lastTip = worldPos;
			AddPoint(worldPos);
			_ages.Add(0f);
			while (GetPointCount() > Mathf.Max(MaxPoints, 2))
			{
				RemovePoint(0);
				_ages.RemoveAt(0);
			}
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;
			float hold = Mathf.Max(HoldSeconds, 0f);
			float fade = Mathf.Max(FadeSeconds, 0.01f);

			// 头部（最老的点）淡完就剔除
			int drop = 0;
			while (drop < _ages.Count && _ages[drop] > hold + fade) drop++;
			for (int i = 0; i < drop; i++) RemovePoint(0);
			_ages.RemoveRange(0, drop);

			if (_ages.Count == 0) { QueueFree(); return; }

			// 逐点 alpha → 渐变（颜色数 = 点数、偏移均分 → 头部全亮、尾巴渐隐）
			int n = _ages.Count;
			var colors = new Color[n];
			var offsets = new float[n];
			for (int i = 0; i < n; i++)
			{
				_ages[i] += dt;
				float age = _ages[i];
				float alpha = age <= hold ? 1f : 1f - Mathf.Clamp((age - hold) / fade, 0f, 1f);
				colors[i] = new Color(1f, 1f, 1f, Mathf.Max(alpha, 0f));
				offsets[i] = n <= 1 ? 0f : i / (float)(n - 1);
			}
			if (Gradient != null)
			{
				Gradient.Colors = colors;
				Gradient.Offsets = offsets;
			}
		}
	}
}
