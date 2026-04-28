using Godot;
using System;

namespace Kuros.UI
{
	/// <summary>
	/// 伤害飘字 - 显示并动画化伤害数字，支持伤害合并和方向推动效果
	/// </summary>
	public partial class FloatingDamageText : Control
	{
		[Export] public float DurationSeconds = 1.5f;
		[Export] public float FloatHeight = 80f;
		[Export] public float HorizontalDrift = 40f; // 水平漂移距离
		[Export] public Color DamageColor = Colors.Yellow;
		[Export] public Color CriticalColor = Colors.Red;
		[Export] public Color HealColor = Colors.Green;
		[Export] public int BaseFontSize = 32;
		[Export] public float DamageMergeWindowSeconds = 0.3f; // 伤害合并时间窗口（秒内的伤害会合并）
		
		[ExportCategory("显示位置")]
		[Export] public Vector2 BasePositionOffset = new Vector2(0, -50);  // 相对于目标位置的基础偏移
		[Export] public Vector2 AdditionalOffset = Vector2.Zero;           // 额外的位置调整
		
		[ExportCategory("随机偏移")]
		[Export] public float RandomOffsetXRange = 60f;  // X轴随机偏移范围（总范围）
		[Export] public float RandomOffsetYRange = 20f;  // Y轴随机偏移范围（总范围）

		private Label? _label;
		private Vector2 _startPosition;
		private Vector2 _pushDirection = Vector2.Right; // 推开方向（根据受击方向反向）
		private float _elapsedTime = 0f;
		private int _totalDamage = 0; // 累积伤害值
		private bool _isCritical = false;
		private int _baseFontSize;
		private float _lastDamageTime = 0f; // 记录上次伤害的时间，用于判断是否应该重置合并窗口

		public override void _Ready()
		{
			_label = GetNode<Label>("Label");
			_baseFontSize = _label?.GetThemeFontSize("font_size") ?? BaseFontSize;
			
			if (_label != null)
			{
				_label.AddThemeColorOverride("font_color", DamageColor);
			}
		}

		public override void _Process(double delta)
		{
			_elapsedTime += (float)delta;
			_lastDamageTime += (float)delta; // 不断累加，用于判断合并窗口

			float progress = _elapsedTime / DurationSeconds;

			if (progress >= 1f)
			{
				QueueFree();
				return;
			}

			// 计算位置：向上浮动 + 根据推开方向的水平漂移
			float currentY = _startPosition.Y - (FloatHeight * progress);
			float horizontalOffset = HorizontalDrift * progress * _pushDirection.X*-1;
			GlobalPosition = new Vector2(_startPosition.X + horizontalOffset, currentY);

			// 计算透明度（逐渐消退）
			if (_label != null)
			{
				float alpha = Mathf.Lerp(1f, 0f, progress);
				var color = _label.GetThemeColor("font_color");
				color.A = alpha;
				_label.AddThemeColorOverride("font_color", color);

				// 微妙的缩放效果：前期稍微放大，后期缩小
				float scaleValue = Mathf.Lerp(1.1f, 0.8f, progress);
				Scale = new Vector2(scaleValue, scaleValue);
			}
		}

		/// <summary>
		/// 初始化飘字（新伤害）
		/// </summary>
		/// <param name="damage">伤害值</param>
		/// <param name="targetPosition">目标全局位置（受害者位置）</param>
		/// <param name="damageDirection">伤害来源方向（飘字会向相反方向推开）</param>
		/// <param name="isCritical">是否暴击</param>
		public void Initialize(int damage, Vector2 targetPosition, Vector2 damageDirection, bool isCritical = false)
		{
			// 计算最终显示位置：受害者位置 + 基础偏移 + 随机偏移 + 额外偏移
			Vector2 displayPosition = targetPosition + BasePositionOffset;
			
			// 添加随机偏移
			float randomOffsetX = (GD.Randf() * RandomOffsetXRange) - (RandomOffsetXRange / 2f);
			float randomOffsetY = (GD.Randf() * RandomOffsetYRange) - (RandomOffsetYRange / 2f);
			displayPosition += new Vector2(randomOffsetX, randomOffsetY);
			
			// 应用额外偏移
			displayPosition += AdditionalOffset;
			
			GlobalPosition = displayPosition;
			_startPosition = displayPosition;
			_totalDamage = damage;
			_isCritical = isCritical;
			_elapsedTime = 0f;
			_lastDamageTime = 0f;

			// 设置推开方向（与伤害来源方向相反）
			if (damageDirection.LengthSquared() > 0.01f)
			{
				_pushDirection = -damageDirection.Normalized();
			}
			else
			{
				_pushDirection = Vector2.Right; // 默认向右
			}

			UpdateDisplay();
		}

		/// <summary>
		/// 添加伤害（伤害合并）- 在时间窗口内多次伤害会合并显示
		/// </summary>
		public void AddDamage(int additionalDamage, Vector2 damageDirection, bool isCritical = false)
		{
			_lastDamageTime = 0f; // 重置计时器
			_totalDamage += additionalDamage;
			_isCritical = _isCritical || isCritical; // 只要有一次暴击就显示暴击

			// 更新推开方向（以最近的伤害方向为准）
			if (damageDirection.LengthSquared() > 0.01f)
			{
				_pushDirection = -damageDirection.Normalized();
			}

			UpdateDisplay();
		}

		/// <summary>
		/// 检查伤害是否仍在合并时间窗口内
		/// </summary>
		public bool CanMergeDamage()
		{
			return _lastDamageTime < DamageMergeWindowSeconds;
		}

		/// <summary>
		/// 更新显示内容
		/// </summary>
		private void UpdateDisplay()
		{
			if (_label == null) return;

			_label.Text = _totalDamage.ToString();

			// 根据是否暴击选择颜色和大小
			if (_isCritical)
			{
				_label.AddThemeColorOverride("font_color", CriticalColor);
				_label.AddThemeFontSizeOverride("font_size", (int)(_baseFontSize * 1.5f));
			}
			else
			{
				_label.AddThemeColorOverride("font_color", DamageColor);
				_label.AddThemeFontSizeOverride("font_size", _baseFontSize);
			}

			// 重置alpha
			var currentColor = _label.GetThemeColor("font_color");
			currentColor.A = 1f;
			_label.AddThemeColorOverride("font_color", currentColor);
		}

		/// <summary>
		/// 初始化治疗飘字
		/// </summary>
		public void InitializeHealing(int amount, Vector2 position, Vector2 sourceDirection = default)
		{
			GlobalPosition = position;
			_startPosition = position;
			_totalDamage = amount;
			_elapsedTime = 0f;
			_lastDamageTime = 0f;

			// 治疗通常向外扩散
			if (sourceDirection.LengthSquared() > 0.01f)
			{
				_pushDirection = sourceDirection.Normalized();
			}
			else
			{
				_pushDirection = Vector2.Right;
			}

			if (_label != null)
			{
				_label.Text = $"+{amount}";
				_label.AddThemeColorOverride("font_color", HealColor);
				_label.AddThemeFontSizeOverride("font_size", _baseFontSize);

				var currentColor = _label.GetThemeColor("font_color");
				currentColor.A = 1f;
				_label.AddThemeColorOverride("font_color", currentColor);
			}

			Scale = Vector2.One;
		}
	}
}
