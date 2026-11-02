using System.Collections.Generic;
using Godot;

namespace Kuros.Effects
{
	/// <summary>
	/// 全屏数字干扰（纯视觉 · EFFECT_STANDARD「全局反馈」）：由攻击特效生成，自管理时长。
	/// InterferenceLayer 下**多个全屏 ColorRect 叠加**（类似 zone 的三层视觉），每层独立材质/
	/// seed/方块参数；材质上作者化的 strength 作为该层权重，运行时统一乘 fade。
	/// 使用与 InterferenceZone 相同的 shader（方块内屏幕扭曲 + 彩虹渐变 + 绿色滤镜）。
	/// 强度按自身 Duration 衰减；播放结束自毁。
	/// </summary>
	[GlobalClass]
	public partial class ScreenInterferenceEffect : Node2D
	{
		[ExportCategory("Timing 时长")]
		/// <summary>总时长（秒）。默认 2.4 = RogueAISlamB 的 ActiveDuration，干扰随攻击一起收尾。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.05")] public float Duration { get; set; } = 2.4f;
		/// <summary>起量时间（秒）：从 0 拉到 MaxStrength，避免第一帧突然全屏故障。</summary>
		[Export(PropertyHint.Range, "0,1,0.01")] public float FadeInDuration { get; set; } = 0.1f;
		/// <summary>峰值干扰强度（0~1）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float MaxStrength { get; set; } = 1f;
		/// <summary>衰减曲线指数：1 = 线性，越大后期衰减越快（默认 1.3，前段保持、末段快速归零）。</summary>
		[Export(PropertyHint.Range, "0.2,4,0.1")] public float DecayExponent { get; set; } = 1.3f;

		private sealed class RectLayer
		{
			public ColorRect Rect = null!;
			public ShaderMaterial Material = null!;
			/// <summary>该层权重：取自材质里作者化的 strength（0 = 该层关闭）。</summary>
			public float Weight;
		}

		private readonly List<RectLayer> _layers = new();
		private float _elapsed;
		/// <summary>传给 shader 的自定义时间（方块刷新/色相），所有层共用。</summary>
		private float _shaderTime;
		private bool _finished;

		public override void _Ready()
		{
			ProcessMode = ProcessModeEnum.Always;

			// 收集**所有 CanvasLayer** 下的 ColorRect：
			// 干扰层（layer 100）与扫描线层（layer 101）分开，
			// 让扫描线在自己的层里采样到完整的 A/B/C 输出（同层屏幕采样读不到同层更早的绘制）。
			foreach (Node layerNode in GetChildren())
			{
				if (layerNode is not CanvasLayer canvas) continue;

				foreach (Node child in canvas.GetChildren())
				{
					if (child is not ColorRect rect) continue;
					if (rect.Material is not ShaderMaterial shared) continue;

					// 每个实例独立材质；材质上写的 strength 作为该层权重（运行时再被 fade 覆盖）
					var material = (ShaderMaterial)shared.Duplicate();
					rect.Material = material;
					Variant authored = material.GetShaderParameter("strength");
					float weight = authored.VariantType == Variant.Type.Nil
						? 1f
						: Mathf.Clamp(authored.AsSingle(), 0f, 1f);

					_layers.Add(new RectLayer { Rect = rect, Material = material, Weight = weight });
					material.SetShaderParameter("strength", 0f);
					material.SetShaderParameter("effect_time", 0f);
					rect.Visible = false;
				}
			}

			if (_layers.Count == 0)
			{
				GD.PushWarning($"[{nameof(ScreenInterferenceEffect)}] 没有找到带材质的 ColorRect，特效自毁");
				QueueFree();
			}
		}

		public override void _Process(double delta)
		{
			if (_finished || _layers.Count == 0) return;

			// 本节点是 Always（暂停时也要能隐藏/收尾），但 shader 计时必须跟游戏时间一致：
			// 暂停时冻结 _elapsed/_shaderTime，并隐藏覆盖层，避免它盖住暂停菜单。
			if (GetTree().Paused)
			{
				SetVisible(false);
				return;
			}

			_elapsed += (float)delta;
			_shaderTime += (float)delta;

			if (_elapsed >= Duration)
			{
				_finished = true;
				SetStrength(0f);
				QueueFree();
				return;
			}

			float t = Mathf.Clamp(_elapsed / Mathf.Max(Duration, 0.001f), 0f, 1f);
			float fadeIn = 1f;
			if (FadeInDuration > 0.001f)
			{
				float f = Mathf.Clamp(_elapsed / FadeInDuration, 0f, 1f);
				fadeIn = f * f * (3f - 2f * f);   // smoothstep
			}

			float strength = MaxStrength * fadeIn * Mathf.Pow(1f - t, Mathf.Max(DecayExponent, 0.01f));
			SetStrength(strength);
		}

		private void SetStrength(float strength)
		{
			foreach (var layer in _layers)
			{
				float s = strength * layer.Weight;
				layer.Rect.Visible = s > 0.001f;
				layer.Material.SetShaderParameter("strength", s);
				layer.Material.SetShaderParameter("effect_time", _shaderTime);
			}
		}

		private void SetVisible(bool visible)
		{
			foreach (var layer in _layers)
				layer.Rect.Visible = visible;
		}
	}
}
