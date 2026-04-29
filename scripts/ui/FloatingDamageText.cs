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
		[Export] public Color DamageColor = Colors.White;
		[Export] public Color CriticalColor = Colors.Red;
		[Export] public Color HealColor = Colors.Green;
		[Export] public float DamageMergeWindowSeconds = 0.3f; // 伤害合并时间窗口（秒内的伤害会合并）
		
		[ExportCategory("显示位置")]
		[Export] public Vector2 BasePositionOffset = new Vector2(0, -50);  // 相对于目标位置的基础偏移
		[Export] public Vector2 AdditionalOffset = Vector2.Zero;           // 额外的位置调整
		
		[ExportCategory("随机偏移")]
		[Export] public float RandomOffsetXRange = 60f;  // X轴随机偏移范围（总范围）
		[Export] public float RandomOffsetYRange = 20f;  // Y轴随机偏移范围（总范围）

		[ExportCategory("伤害缩放与弹出速度")]
		[Export] public float ReferenceDamage = 5f;     // 基准
		[Export] public float MinDamageScale = 0.75f;     
		[Export] public float MaxDamageScale = 2.5f;     

		private Label? _label;
		private Vector2 _startPosition;
		private Vector2 _pushDirection = Vector2.Right; // 推开方向（根据受击方向反向）
		private float _elapsedTime = 0f;
		private int _totalDamage = 0; // 累积伤害值
		private bool _isCritical = false;
		private float _lastDamageTime = 0f; // 记录上次伤害的时间，用于判断是否应该重置合并窗口

		// 伤害缩放后的运行时参数
		private float _effectiveFloatHeight;
		private float _effectiveDrift;
		// 由伤害量决定的基础缩放比，_Process 中的动画曲线在此基础上乘算
		private float _damageScale = 1f;
		// 当前目标颜色（纯色，不含 alpha；_Process 每帧写入 _label.Modulate = color * alpha）
		private Color _currentLabelColor;

		public override void _Ready()
		{
			_label = GetNode<Label>("Label");
			_currentLabelColor = DamageColor;
			_effectiveFloatHeight = FloatHeight;
			_effectiveDrift = HorizontalDrift;
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

			// 计算位置：向上浮动 + 根据推开方向的水平漂移（均使用伤害缩放后的值）
			float currentY = _startPosition.Y - (_effectiveFloatHeight * progress);
			float horizontalOffset = _effectiveDrift * progress * _pushDirection.X * -1;
			GlobalPosition = new Vector2(_startPosition.X + horizontalOffset, currentY);

			// 缩放动画
			float animScale = Mathf.Lerp(1.1f, 0.5f, progress);
			float baseScale = _isCritical ? _damageScale * 1.5f : _damageScale;
			float s = baseScale * animScale;
			Scale = new Vector2(s, s);
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
			ApplyDamageScale(_totalDamage);

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

			ApplyDamageScale(_totalDamage);
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
		/// 将此飘字标记为暴击（由外部管理器调用）
		/// </summary>
		public void SetCritical()
		{
			_isCritical = true;
			UpdateDisplay();
		}

		/// <summary>
		/// 根据伤害量计算并更新运行时缩放参数（上浮高度、漂移距离、整体 Scale）
		/// LabelSettings 会覆盖 AddThemeFontSizeOverride，所以字号大小改用 Scale 控制。
		/// </summary>
		private void ApplyDamageScale(int damage)
		{
			float scale = ReferenceDamage > 0f
				? Mathf.Clamp(damage / ReferenceDamage, MinDamageScale, MaxDamageScale)
				: 1f;
			_damageScale = scale;
			_effectiveFloatHeight = FloatHeight * scale;
			_effectiveDrift = HorizontalDrift * scale;
			// 立即应用初始缩放，让飘字一出现就是正确大小
			Scale = new Vector2(scale, scale);
		}

		/// <summary>
		/// 更新显示内容
		/// </summary>
		private void UpdateDisplay()
		{
			if (_label == null) return;

			_label.Text = _totalDamage.ToString();

			// 根据是否暴击选择颜色，暴击时 Scale 额外 ×1.5
			if (_isCritical)
			{
				_currentLabelColor = CriticalColor;
				Scale = new Vector2(_damageScale * 1.5f, _damageScale * 1.5f);
			}
			else
			{
				_currentLabelColor = DamageColor;
				Scale = new Vector2(_damageScale, _damageScale);
			}

			// 重置 Scale
			if (_label != null)
			{
				var c = _currentLabelColor;
				c.A = 1f;
				_label.Modulate = c;
			}
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
				var c = HealColor;
				c.A = 1f;
				_label.Modulate = c;
			}

			Scale = Vector2.One;
		}
	}
}
