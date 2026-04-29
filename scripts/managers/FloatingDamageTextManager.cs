using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Managers
{
	/// <summary>
	/// 浮动伤害文字管理器 - 监听伤害事件并生成飘字
	/// 这是一个单例管理器
	/// </summary>
	public partial class FloatingDamageTextManager : Node
	{
		[Export] public string FloatingTextScenePath = "res://scenes/ui/FloatingDamageText.tscn";
		[Export] public bool EnableFloatingText = true;
	
	private static FloatingDamageTextManager? _instance;
	private Dictionary<GameActor, FloatingDamageText> _recentDamageTexts = new Dictionary<GameActor, FloatingDamageText>();
	private PackedScene? _floatingTextScene;

		public static FloatingDamageTextManager Instance => _instance!;

		public override void _Ready()
		{
			if (_instance != null && _instance != this)
			{
				QueueFree();
				return;
			}

			_instance = this;

			// 加载飘字场景
			_floatingTextScene = GD.Load<PackedScene>(FloatingTextScenePath);
			if (_floatingTextScene == null)
			{
				GameLogger.Warn(nameof(FloatingDamageTextManager), 
					$"未能加载飘字场景: {FloatingTextScenePath}");
			}

			// 订阅全局伤害事件
			GameActor.AnyDamageTaken += OnAnyDamageTaken;
			// 订阅带来源的伤害总线，用于识别暴击追加伤害
			DamageEventBus.SubscribeWithSource(OnDamageResolvedWithSource);
		}

		public override void _ExitTree()
		{
			if (_instance == this)
			{
				_instance = null;
			}
			GameActor.AnyDamageTaken -= OnAnyDamageTaken;
			DamageEventBus.UnsubscribeWithSource(OnDamageResolvedWithSource);
		}

		/// <summary>
		/// 处理伤害事件 - 在受伤时生成或合并飘字
		/// 支持伤害合并和方向推动效果
		/// </summary>
		private void OnAnyDamageTaken(GameActor victim, GameActor? attacker, int damage)
		{
			if (!EnableFloatingText) return;
			if (victim == null || damage <= 0) return;
			if (_floatingTextScene == null) return;

			// 计算伤害来源方向
			Vector2 damageDirection = Vector2.Right;
			if (attacker != null && attacker.GlobalPosition != victim.GlobalPosition)
			{
				// 从攻击者指向受害者的方向
				damageDirection = (victim.GlobalPosition - attacker.GlobalPosition).Normalized();
			}

			// 红色暴击飘字只由 DamageSource.CritBonus 触发（见 OnDamageResolvedWithSource），
			// 此处不做阈值判断，避免大伤害/投掷物误判为暴击
			bool isCritical = false;

			// 检查是否有最近的飘字可以合并伤害
			if (_recentDamageTexts.TryGetValue(victim, out var recentText))
			{
				if (IsInstanceValid(recentText) && recentText.CanMergeDamage())
				{
					// 合并伤害到现有飘字
					recentText.AddDamage(damage, damageDirection, isCritical);
					return;
				}
				else
				{
					// 飘字已过期或无效，移除引用
					_recentDamageTexts.Remove(victim);
				}
			}

			// 创建新飘字（将目标位置传递给飘字脚本，由它自己计算显示位置）
			var newText = CreateFloatingDamageText(damage, victim.GlobalPosition, damageDirection, isCritical);
			if (newText != null)
			{
				_recentDamageTexts[victim] = newText;
			}
		}
		/// <summary>
		/// 监听带来源的伤害总线：当 source == CritBonus 时，将对应飘字升级为暴击显示（红色）。
		/// 此回调在 AnyDamageTaken（生成飘字）之后触发，因此可直接修改刚创建的飘字。
		/// </summary>
		private void OnDamageResolvedWithSource(GameActor attacker, GameActor target, int damage, DamageSource source)
		{
			if (source != DamageSource.CritBonus) return;
			if (!_recentDamageTexts.TryGetValue(target, out var text)) return;
			if (IsInstanceValid(text))
			{
				text.SetCritical();
			}
		}

		/// <summary>
		/// 创建飘字实例
		/// </summary>
		private FloatingDamageText? CreateFloatingDamageText(int damage, Vector2 position, Vector2 damageDirection, bool isCritical)
		{
			if (_floatingTextScene == null) return null;

			var floatingText = _floatingTextScene.Instantiate<FloatingDamageText>();
			if (floatingText == null)
			{
				GameLogger.Warn(nameof(FloatingDamageTextManager), 
					"无法实例化飘字场景");
				return null;
			}

			GetTree().Root?.AddChild(floatingText);
			floatingText.Initialize(damage, position, damageDirection, isCritical);
			
			return floatingText;
		}

		/// <summary>
		/// 显示伤害飘字
		/// </summary>
		public void ShowFloatingDamage(int damage, Vector2 position, Vector2 damageDirection, bool isCritical = false)
		{
			CreateFloatingDamageText(damage, position, damageDirection, isCritical);
		}

		/// <summary>
		/// 显示治疗飘字
		/// </summary>
		public void ShowFloatingHealing(int amount, Vector2 position, float horizontalDriftDirection = 0f)
		{
			if (!EnableFloatingText || _floatingTextScene == null)
			{
				return;
			}

			var floatingText = _floatingTextScene.Instantiate<FloatingDamageText>();
			if (floatingText == null)
			{
				GameLogger.Warn(nameof(FloatingDamageTextManager), 
					"无法实例化飘字场景");
				return;
			}

			GetTree().Root?.AddChild(floatingText);
			floatingText.InitializeHealing(amount, position, new Vector2(horizontalDriftDirection, 0));
		}
	}
}
