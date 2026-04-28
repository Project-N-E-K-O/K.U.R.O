using Godot;

namespace Kuros.Effects
{
	/// <summary>
	/// 砸击效果淡出脚本：在指定时长内淡出消失
	/// </summary>
	public partial class SmashEffectFadeOut : Node2D
	{
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float FadeOutDuration = 0.6f;

		private Sprite2D? _sprite;
		private float _fadeTimer = 0f;
		private Color _initialColor;

		public override void _Ready()
		{
			_sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
			if (_sprite != null)
			{
				_initialColor = _sprite.Modulate;
				_fadeTimer = 0f;
			}
			else
			{
				GD.PushWarning($"{Name}: 未找到 Sprite2D 子节点");
			}
		}

		public override void _Process(double delta)
		{
			if (_sprite == null) return;

			_fadeTimer += (float)delta;
			float progress = Mathf.Clamp(_fadeTimer / FadeOutDuration, 0f, 1f);

			// 线性淡出透明度
			Color fadedColor = _initialColor;
			fadedColor.A = Mathf.Lerp(_initialColor.A, 0f, progress);
			_sprite.Modulate = fadedColor;

			// 完全透明后销毁
			if (progress >= 1f)
			{
				QueueFree();
			}
		}
	}
}
