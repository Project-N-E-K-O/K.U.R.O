using Godot;
using System;
using Kuros.Core;
using Kuros.Actors.Enemies;
using Kuros.Items.Attributes;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
	/// <summary>
	/// 主角色控制器，用于控制带有 SpineSprite 动画的 CharacterBody2D 角色
	/// 与 StateMachine 协同工作，集成 WeaponSkillController 功能
	/// 继承自 SamplePlayer 以确保敌人能够正确检测和攻击
	/// </summary>
	public partial class MainCharacter : SamplePlayer
	{
		[ExportCategory("Animation")]
	[Export] public NodePath SpineSpritePath { get; set; } = new NodePath("SpineSprite");
	[Export] public string IdleAnimationName { get; set; } = "idle";
	[Export] public string WalkAnimationName { get; set; } = "walk";
	[Export] public string RunAnimationName { get; set; } = "run";
	[Export] public string AttackAnimationName { get; set; } = "attack";
	[Export] public float WalkAnimationSpeed { get; set; } = 1.5f;
	[Export] public float RunAnimationSpeed { get; set; } = 2.0f;
	[Export] public float RunSpeedMultiplier { get; set; } = 2.0f; // 跑步速度倍数
	[Export] public float AnimationMixDuration { get; set; } = 0.1f; // 动画混合时长

	[ExportCategory("Combat")]
	[Export] public new Area2D AttackArea { get; private set; } = null!;

	[ExportCategory("Input")]
	[Export] public string MoveLeftAction { get; set; } = "move_left";
	[Export] public string MoveRightAction { get; set; } = "move_right";
	[Export] public string MoveForwardAction { get; set; } = "move_forward";
	[Export] public string MoveBackAction { get; set; } = "move_back";
	[Export] public string AttackAction { get; set; } = "attack";
	[Export] public string RunAction { get; set; } = "run";

	// Spine 相关（使用 Node 引用，通过 Call 调用 GDScript 方法）
	private Node? _spineController;
	private string _currentAnimation = string.Empty;

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("player");

		// 尝试查找 AttackArea
		if (AttackArea == null)
		{
			AttackArea = GetNodeOrNull<Area2D>("AttackArea");
		}

		// 初始化 SpineSprite 和 AnimationState
		// 注意：SamplePlayer._Ready() 已经会尝试查找 InventoryComponent 和 WeaponSkillController
		// 如果场景中没有这些组件，会有警告但不会影响基本功能
		InitializeSpine();

		// 检查并验证组件初始化
		ValidateComponents();

		// 检查状态机是否正确初始化
		if (StateMachine == null)
		{
			GD.PushError($"[{Name}] StateMachine 未找到！请确保场景中有 StateMachine 子节点。");
		}
		else if (StateMachine.CurrentState == null)
		{
			GD.PushWarning($"[{Name}] StateMachine 当前状态为 null，请确保 InitialState 已设置。");
		}
		else
		{
			GD.Print($"[{Name}] StateMachine 初始化成功，当前状态: {StateMachine.CurrentState.Name}");
		}

		// 状态机会在进入 Idle 状态时播放待机动画
		// 这里不需要手动播放，让状态机管理
	}

	/// <summary>
	/// 验证组件是否正确初始化
	/// </summary>
	private void ValidateComponents()
	{
		// 检查 InventoryComponent
		if (InventoryComponent == null)
		{
			GD.PushWarning($"[{Name}] InventoryComponent 未找到！请确保场景中有 'Inventory' 子节点（PlayerInventoryComponent）。");
		}
		else
		{
			GD.Print($"[{Name}] InventoryComponent 初始化成功: {InventoryComponent.Name}");
		}

		// 检查 WeaponSkillController
		if (WeaponSkillController == null)
		{
			GD.PushWarning($"[{Name}] WeaponSkillController 未找到！");
		}
		else
		{
			GD.Print($"[{Name}] WeaponSkillController 初始化成功: {WeaponSkillController.Name}");
		}

		// 检查 PlayerItemInteractionComponent
		var itemInteraction = GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
		if (itemInteraction == null)
		{
			GD.PushWarning($"[{Name}] PlayerItemInteractionComponent 未找到！请确保场景中有 'ItemInteraction' 子节点。");
		}
		else
		{
			GD.Print($"[{Name}] PlayerItemInteractionComponent 初始化成功: {itemInteraction.Name}");
		}
	}


	/// <summary>
	/// 初始化 SpineController（查找挂载了 SpineController.gd 的 SpineSprite 节点）
	/// </summary>
	private void InitializeSpine()
	{
		// 尝试通过路径获取 SpineSprite 节点
		Node? spineNode = null;
		if (!SpineSpritePath.IsEmpty)
		{
			spineNode = GetNodeOrNull(SpineSpritePath);
		}

		// 如果路径获取失败，尝试按名称查找
		if (spineNode == null)
		{
			spineNode = GetNodeOrNull("SpineSprite");
		}

		// 如果还是找不到，尝试递归查找
		if (spineNode == null)
		{
			spineNode = FindChild("SpineSprite", recursive: true, owned: false);
		}

		if (spineNode == null)
		{
			GD.PushWarning($"[{Name}] 未找到 SpineSprite 节点！请确保场景中有 SpineSprite 子节点，并且挂载了 SpineController.gd 脚本。");
			return;
		}

		// SpineController.gd 应该直接挂载在 SpineSprite 节点上
		_spineController = spineNode;

		// 验证节点是否有 play 方法（即是否挂载了 SpineController.gd）
		if (_spineController != null && _spineController.HasMethod("play"))
		{
			GD.Print($"[{Name}] SpineController 初始化成功");
		}
		else
		{
			GD.PushWarning($"[{Name}] SpineSprite 节点未挂载 SpineController.gd 脚本！请在 SpineSprite 节点上附加 scripts/controllers/SpineController.gd 脚本。");
			_spineController = null;
		}
	}

	// 注意：需要保留 _UnhandledInput 来调用基类方法，让状态机处理输入
	// StateMachine 会自动调用 _PhysicsProcess 和 _UnhandledInput
	public override void _UnhandledInput(InputEvent @event)
	{
		// 调用基类方法，让 SamplePlayer 处理快捷栏切换等输入
		// 然后 SamplePlayer 会调用 base._UnhandledInput，让状态机处理攻击等输入
		base._UnhandledInput(@event);
	}

		/// <summary>
		/// 播放 Spine 动画（供状态机调用）
		/// 这个方法会被状态机状态调用，替代 AnimationPlayer
		/// </summary>
		/// <param name="animName">动画名称</param>
		/// <param name="loop">是否循环</param>
		/// <param name="timeScale">时间缩放（播放速度）</param>
		public void PlaySpineAnimation(string animName, bool loop = true, float timeScale = 1.0f)
		{
			// 如果 SpineController 未初始化，跳过
			if (_spineController == null)
			{
				GD.PushWarning($"[{Name}] SpineController 未初始化，无法播放动画: {animName}");
				return;
			}

			// 对于非循环动画（如攻击），即使名称相同也强制播放
			// 对于循环动画，如果已经在播放则跳过（避免重复播放）
			if (loop && _currentAnimation == animName)
			{
				return;
			}

			_currentAnimation = animName;

			try
			{
				// 调用 SpineController.gd 的 play 方法
				// play(anim: String, loop := true, mix_duration := 0.1, time_scale := 1.0)
				_spineController.Call("play", animName, loop, AnimationMixDuration, timeScale);
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[{Name}] 播放动画失败: {animName}, 错误: {ex.Message}");
			}
		}


		/// <summary>
		/// 执行攻击检测（集成 WeaponSkillController）
		/// 这个方法会被 PlayerAttackTemplate 或状态机调用
		/// </summary>
		public new void PerformAttackCheck()
		{
			if (AttackArea == null)
			{
				GD.PushWarning($"[{Name}] AttackArea 未设置，无法执行攻击检测");
				return;
			}

			// 计算基础伤害
			float baseDamage = AttackDamage;

			// 应用 InventoryComponent 的攻击力加成
			if (InventoryComponent != null)
			{
				baseDamage += InventoryComponent.GetSelectedAttributeValue(ItemAttributeIds.AttackPower, 0f);
			}

			// 应用 WeaponSkillController 的伤害倍率
			if (WeaponSkillController != null)
			{
				baseDamage = WeaponSkillController.ModifyAttackDamage(baseDamage);
			}

			// 执行攻击检测
			var bodies = AttackArea.GetOverlappingBodies();
			int hitCount = 0;
			foreach (var body in bodies)
			{
				if (body is SampleEnemy enemy)
				{
					enemy.TakeDamage((int)baseDamage, GlobalPosition, this);
					hitCount++;
					GameLogger.Info(nameof(MainCharacter), $"击中敌人: {enemy.Name}, 伤害: {baseDamage}");
				}
			}

			if (hitCount == 0)
			{
				GameLogger.Info(nameof(MainCharacter), "未击中任何敌人");
			}
		}

	public override void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null)
	{
		base.TakeDamage(damage, attackOrigin, attacker);
		// 状态机会处理受伤状态切换，不需要额外逻辑
	}

	protected override void OnDeathFinalized()
		{
			EffectController?.ClearAll();
			GameLogger.Warn(nameof(MainCharacter), "角色死亡！");
			// 可以在这里添加游戏结束逻辑
			// GetTree().ReloadCurrentScene();
		}
	}
}
