using Godot;
using System;
using System.Collections.Generic;
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
	[Export] public NodePath OutlineSpineSpritePath { get; set; } = new NodePath("OutlineSpineSprite");
	[Export] public string IdleAnimationName { get; set; } = "idle";
	[Export] public string WalkAnimationName { get; set; } = "walk";
	[Export] public string RunAnimationName { get; set; } = "run";
	[Export] public string DashAnimationName { get; set; } = "dash";
	[Export] public string DashBackAnimationName { get; set; } = "backdash";
	[Export] public string AttackAnimationName { get; set; } = "attack";
	[Export] public string IdleHoldingAnimationName { get; set; } = "idle_holding_item";
	[Export] public string RunHoldingAnimationName { get; set; } = "run_holding_item";
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

	[ExportCategory("Combat/Invincible Frames")]
	[Export] public bool EnableHitInvincibility { get; set; } = true;
	[Export(PropertyHint.Range, "0,5,0.01,or_greater")] public float HitInvincibilityDuration { get; set; } = 1.0f;
	[Export] public bool EnableInvincibleFlash { get; set; } = true;
	[Export(PropertyHint.Range, "0,1,0.01")] public float InvincibleFlashMinAlpha { get; set; } = 0.35f;
	[Export(PropertyHint.Range, "0,60,0.1,or_greater")] public float InvincibleFlashSpeed { get; set; } = 18.0f;

	// Spine 相关（使用 Node 引用，通过 Call 调用 GDScript 方法）
	private Node? _spineController;
	private readonly List<Node> _outlineSpineControllers = new();
	private CanvasItem? _spineBoneNode;
	private Color _defaultOutlineModulate = new Color(0.02f, 0.02f, 0.02f, 0.85f);
	private string _currentAnimation = string.Empty;
	/// <summary>当前正在播放的 Spine 动画名（供外部判断是否已在该动画，避免重复重播）。</summary>
	public string CurrentAnimationName => _currentAnimation;
	/// <summary>当前 Spine 动画的播放时间（秒）——供切换动画时保持时间轴位置（hit 帧不因切换丢失）。</summary>
	public float CurrentAnimationTime
	{
		get
		{
			if (_spineController == null) return 0f;
			var result = _spineController.Call("get_track_time");
			return result.AsSingle();
		}
	}
	private float _hitInvincibilityRemaining = 0.0f;
	private float _invincibleFlashElapsed = 0.0f;
	private float _defaultSpineAlpha = 1.0f;
	private float _defaultSpriteAlpha = 1.0f;
	private bool _pendingHitKnockback;

	public bool IsHitInvincible => _hitInvincibilityRemaining > 0.0f;

	public override void _Ready()
	{
		// 必须在 base._Ready() 之前初始化 SpineController，
		// 否则 StateMachine 进入 Idle 状态时 _spineController 仍为 null，
		// 导致默认动画无法播放，角色显示 "--Empty--"
		InitializeSpine();

		base._Ready();
		AddToGroup("player");

		// 尝试查找 AttackArea
		if (AttackArea == null)
		{
			AttackArea = GetNodeOrNull<Area2D>("AttackArea");
		}
		_defaultSpineAlpha = _spineCharacter != null ? _spineCharacter.Modulate.A : 1.0f;
		_defaultSpriteAlpha = _sprite != null ? _sprite.Modulate.A : 1.0f;
		SyncOutlineFacing(FacingRight);

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
		UpdateSpineBoneNodeVisibility();
	}

	/// <summary>
	/// 验证组件是否正确初始化
	/// </summary>
	private void ValidateComponents()
	{
		// 检查 InventoryComponent
		if (InventoryComponent == null)
		{
			//GD.PushWarning($"[{Name}] InventoryComponent 未找到！请确保场景中有 'Inventory' 子节点（PlayerInventoryComponent）。");
		}
		else
		{
			//GD.Print($"[{Name}] InventoryComponent 初始化成功: {InventoryComponent.Name}");
		}

		// 检查 WeaponSkillController
		if (WeaponSkillController == null)
		{
			//GD.PushWarning($"[{Name}] WeaponSkillController 未找到！");
		}
		else
		{
			//GD.Print($"[{Name}] WeaponSkillController 初始化成功: {WeaponSkillController.Name}");
		}

		// 检查 PlayerItemInteractionComponent
		var itemInteraction = GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
		if (itemInteraction == null)
		{
			//GD.PushWarning($"[{Name}] PlayerItemInteractionComponent 未找到！请确保场景中有 'ItemInteraction' 子节点。");
		}
		else
		{
			//GD.Print($"[{Name}] PlayerItemInteractionComponent 初始化成功: {itemInteraction.Name}");
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
			//GD.PushWarning($"[{Name}] 未找到 SpineSprite 节点！请确保场景中有 SpineSprite 子节点，并且挂载了 SpineController.gd 脚本。");
			return;
		}

		// SpineController.gd 应该直接挂载在 SpineSprite 节点上
		_spineController = spineNode;

		// 验证节点是否有 play 方法（即是否挂载了 SpineController.gd）
		if (_spineController != null && _spineController.HasMethod("play"))
		{
			//GD.Print($"[{Name}] SpineController 初始化成功");
		}
		else
		{
			//GD.PushWarning($"[{Name}] SpineSprite 节点未挂载 SpineController.gd 脚本！请在 SpineSprite 节点上附加 scripts/controllers/SpineController.gd 脚本。");
			_spineController = null;
		}

		ResolveOutlineSpines();
	}

	private void ResolveOutlineSpines()
	{
		_outlineSpineControllers.RemoveAll(node => node == null || !IsInstanceValid(node));
		if (_outlineSpineControllers.Count > 0)
		{
			return;
		}

		if (!OutlineSpineSpritePath.IsEmpty)
		{
			Node? configuredNode = GetNodeOrNull(OutlineSpineSpritePath);
			if (configuredNode != null)
			{
				if (configuredNode.HasMethod("play"))
				{
					_outlineSpineControllers.Add(configuredNode);
				}

				foreach (Node child in configuredNode.GetChildren())
				{
					if (child.HasMethod("play") && child.Name.ToString().StartsWith("OutlineSpineSprite", StringComparison.OrdinalIgnoreCase))
					{
						_outlineSpineControllers.Add(child);
					}
				}
			}
		}

		foreach (Node candidate in FindChildren("OutlineSpineSprite*", recursive: true, owned: false))
		{
			if (!candidate.HasMethod("play") || _outlineSpineControllers.Contains(candidate))
			{
				continue;
			}

			_outlineSpineControllers.Add(candidate);
		}

		if (_outlineSpineControllers.Count == 0)
		{
			return;
		}

		Variant outlineColor = _outlineSpineControllers[0].Get("modulate");
		if (outlineColor.VariantType == Variant.Type.Color)
		{
			_defaultOutlineModulate = outlineColor.AsColor();
		}
	}

	private void SyncOutlineFacing(bool faceRight)
	{
		ResolveOutlineSpines();
		if (_outlineSpineControllers.Count == 0)
		{
			return;
		}

		float sign = faceRight ? 1.0f : -1.0f;
		if (FaceLeftByDefault)
		{
			sign *= -1.0f;
		}

		foreach (Node outlineNode in _outlineSpineControllers)
		{
			if (!IsInstanceValid(outlineNode))
			{
				continue;
			}

			Variant scaleVariant = outlineNode.Get("scale");
			if (scaleVariant.VariantType != Variant.Type.Vector2)
			{
				continue;
			}

			Vector2 scale = scaleVariant.AsVector2();
			float absX = Mathf.Abs(scale.X);
			outlineNode.Set("scale", new Vector2(absX * sign, scale.Y));
		}
	}

	public override void FlipFacing(bool faceRight)
	{
		base.FlipFacing(faceRight);
		SyncOutlineFacing(faceRight);
	}

	// 注意：需要保留 _UnhandledInput 来调用基类方法，让状态机处理输入
	// StateMachine 会自动调用 _PhysicsProcess 和 _UnhandledInput
	public override void _UnhandledInput(InputEvent @event)
	{
		// 调用基类方法，让 SamplePlayer 处理快捷栏切换等输入
		// 然后 SamplePlayer 会调用 base._UnhandledInput，让状态机处理攻击等输入
		base._UnhandledInput(@event);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		UpdateHitInvincibility(delta);
		UpdateSpineBoneNodeVisibility();
	}

	private void UpdateSpineBoneNodeVisibility(string? stateOrAnimationName = null)
	{
		ResolveSpineBoneNode();
		if (_spineBoneNode == null || !IsInstanceValid(_spineBoneNode))
		{
			return;
		}

		string currentName = string.IsNullOrWhiteSpace(stateOrAnimationName)
			? StateMachine?.CurrentState?.Name ?? string.Empty
			: stateOrAnimationName;

		bool shouldShow = ShouldShowSpineBoneNode(currentName);
		if (_spineBoneNode.Visible == shouldShow)
		{
			return;
		}

		_spineBoneNode.Visible = shouldShow;
	}

	private bool ShouldShowSpineBoneNode(string? stateOrAnimationName)
	{
		if (string.IsNullOrWhiteSpace(stateOrAnimationName))
		{
			return false;
		}

		return stateOrAnimationName.Equals("Idle", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("Walk", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("Run", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("Stun", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("Hit", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("Frozen", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals(IdleAnimationName, StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals(WalkAnimationName, StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals(RunAnimationName, StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("stun", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("IdleHolding", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("RunHolding", StringComparison.OrdinalIgnoreCase)
			|| stateOrAnimationName.Equals("Throw", StringComparison.OrdinalIgnoreCase)
				|| stateOrAnimationName.Equals("Dash", StringComparison.OrdinalIgnoreCase);
			
	}

	private void ResolveSpineBoneNode()
	{
		if (_spineBoneNode != null && IsInstanceValid(_spineBoneNode))
		{
			return;
		}

		_spineBoneNode = GetNodeOrNull<CanvasItem>("SpineSprite/SpineBoneNode")
			?? GetNodeOrNull<CanvasItem>("SpineBoneNode")
			?? FindChild("SpineBoneNode", recursive: true, owned: false) as CanvasItem;
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
			UpdateSpineBoneNodeVisibility(animName);

			try
			{
				// 调用 SpineController.gd 的 play 方法
				// play(anim: String, loop := true, mix_duration := 0.1, time_scale := 1.0)
				_spineController.Call("play", animName, loop, AnimationMixDuration, timeScale);

				ResolveOutlineSpines();
				foreach (Node outlineNode in _outlineSpineControllers)
				{
					if (IsInstanceValid(outlineNode) && outlineNode.HasMethod("play"))
					{
						outlineNode.Call("play", animName, loop, AnimationMixDuration, timeScale);
					}
				}
			}
			catch (Exception)
			{
				//GD.PushWarning($"[{Name}] 播放动画失败: {animName}");
			}
		}

		/// <summary>
		/// 从指定时间点播放 Spine 动画（跳帧）。
		/// </summary>
		/// <param name="animName">动画名称</param>
		/// <param name="startTime">起始时间（秒），从该时间点继续播放</param>
		/// <param name="loop">是否循环</param>
		/// <param name="timeScale">时间缩放</param>
		public void PlaySpineAnimationFrom(string animName, float startTime, bool loop = false, float timeScale = 1.0f)
		{
			if (_spineController == null)
			{
				GD.PushWarning($"[{Name}] SpineController 未初始化，无法播放动画: {animName}");
				return;
			}

			_currentAnimation = animName;
			UpdateSpineBoneNodeVisibility(animName);

			try
			{
				_spineController.Call("play_from", animName, startTime, loop, AnimationMixDuration, timeScale);

				ResolveOutlineSpines();
				foreach (Node outlineNode in _outlineSpineControllers)
				{
					if (IsInstanceValid(outlineNode) && outlineNode.HasMethod("play_from"))
					{
						outlineNode.Call("play_from", animName, startTime, loop, AnimationMixDuration, timeScale);
					}
				}
			}
			catch (Exception)
			{
			}
		}

		public Node? GetSpineControllerNode()
		{
			return _spineController;
		}

		/// <summary>
		/// 动态修改当前 Spine 动画的播放速度，无需重启动画。
		/// </summary>
		public void SetSpineAnimationSpeed(float timeScale)
		{
			if (_spineController == null || !IsInstanceValid(_spineController))
				return;

			if (_spineController.HasMethod("change_time_scale"))
			{
				_spineController.Call("change_time_scale", timeScale);
			}

			ResolveOutlineSpines();
			foreach (Node outlineNode in _outlineSpineControllers)
			{
				if (IsInstanceValid(outlineNode) && outlineNode.HasMethod("change_time_scale"))
				{
					outlineNode.Call("change_time_scale", timeScale);
				}
			}
		}


		/// <summary>
		/// 执行攻击检测（集成 WeaponSkillController）
		/// 这个方法会被 PlayerAttackTemplate 或状态机调用
		/// </summary>
		public new void PerformAttackCheck()
		{
			if (ResolveAttackAreaForHitDetection() == null)
			{
				// GD.PushWarning($"[{Name}] AttackArea 未设置，无法执行攻击检测");
				return;
			}

			float baseDamage = AttackDamage;
			if (InventoryComponent != null)
			{
				baseDamage += InventoryComponent.GetSelectedAttributeValue(ItemAttributeIds.AttackPower, 0f);
			}
			// 基础伤害倍率（构筑效果如"空载增幅"写入;作用于总基础伤害,技能倍率之前）
			baseDamage *= BasicAttackMultiplier;

			if (WeaponSkillController != null)
			{
				baseDamage = WeaponSkillController.ModifyAttackDamage(baseDamage);
			}

			// int loggedDamage = Mathf.Max(0, Mathf.RoundToInt(baseDamage));
			int hitCount = ApplyDamageWithArea(baseDamage, null);

			// if (hitCount == 0)
			// {
			// 	GameLogger.Info(nameof(MainCharacter), "未击中任何敌人");
			// }
		}

	public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null, Kuros.Core.Events.DamageSource damageSource = Kuros.Core.Events.DamageSource.DirectAttack, bool bypassMergeWindow = false)
	{
		if (damage > 0 && IsHitInvincible && !IsDeathSequenceActive && !IsDead)
		{
			// 无敌帧期间完全忽略受击：不掉血、不击退、不进入受击反应。
			// 注意：不清零 Velocity，避免抹掉触发无敌的那次击退所赋予的速度。
			_pendingHitKnockback = false;
			GameLogger.Info(nameof(MainCharacter), $"{Name} is invincible and ignored incoming damage/knockback.");
			return false;
		}

		// 用基类返回值判断伤害是否被接受（合并窗口下扣血延迟到窗口到期结算，
		// 不能用 CurrentHealth 前后比较——恒为相等会吞掉击退标记）
		bool dealt = base.TakeDamage(damage, attackOrigin, attacker, damageSource, bypassMergeWindow);
		// IgnoreHitStateOnDamage（如齿轮关节 buff）时连击退一起抑制：
		// 敌人击退依赖 ConsumePendingHitKnockback，保留标记会导致不打断攻击却仍被击退
		_pendingHitKnockback = dealt && !IgnoreHitStateOnDamage;
		// 状态机会处理受伤状态切换，不需要额外逻辑
		return dealt;
	}

	public bool ConsumePendingHitKnockback()
	{
		if (!_pendingHitKnockback)
		{
			return false;
		}

		_pendingHitKnockback = false;
		return true;
	}

	/// <summary>这一下命中是否允许被击退（**只读、不消费**）：给"要不要顺带做外部位移"的攻击方读
	/// （如 BoomDmgEffect 的冰冻位移分支）。真正施加击退时由 <see cref="AllowsKnockback"/> 消费。</summary>
	public bool HasPendingHitKnockback => _pendingHitKnockback;

	/// <summary>击退放行钩子（EFFECT_STANDARD.md 第四条）：玩家只有"刚结算的这次伤害允许被击退"时
	/// 才推得动。无敌帧 / 护盾完全格挡 / IgnoreHitStateOnDamage 都是 false。
	/// 攻击方一律直接调 <c>ApplyKnockbackDisplacement</c>，不要再自己判断无敌帧。</summary>
	protected override bool AllowsKnockback() => ConsumePendingHitKnockback();

	public void StartHitInvincibility(float durationOverride = -1.0f)
	{
		if (!EnableHitInvincibility || IsDeathSequenceActive || IsDead)
		{
			return;
		}

		float duration = durationOverride >= 0.0f ? durationOverride : HitInvincibilityDuration;
		_hitInvincibilityRemaining = Mathf.Max(0.0f, duration);
		_invincibleFlashElapsed = 0.0f;

		if (!EnableInvincibleFlash)
		{
			RestoreInvincibleFlash();
			return;
		}

		ApplyInvincibleFlashAlpha(InvincibleFlashMinAlpha);
	}

	public void ClearHitInvincibility()
	{
		_hitInvincibilityRemaining = 0.0f;
		_invincibleFlashElapsed = 0.0f;
		_pendingHitKnockback = false;
		RestoreInvincibleFlash();
	}

	private void UpdateHitInvincibility(double delta)
	{
		if (_hitInvincibilityRemaining <= 0.0f)
		{
			return;
		}

		_hitInvincibilityRemaining = Mathf.Max(0.0f, _hitInvincibilityRemaining - (float)delta);

		if (EnableInvincibleFlash)
		{
			_invincibleFlashElapsed += (float)delta * InvincibleFlashSpeed;
			float pulse = (Mathf.Sin(_invincibleFlashElapsed) + 1.0f) * 0.5f;
			float alpha = Mathf.Lerp(InvincibleFlashMinAlpha, 1.0f, pulse);
			ApplyInvincibleFlashAlpha(alpha);
		}

		if (_hitInvincibilityRemaining <= 0.0f)
		{
			RestoreInvincibleFlash();
		}
	}

	private void ApplyInvincibleFlashAlpha(float alpha)
	{
		if (_spineCharacter != null && IsInstanceValid(_spineCharacter))
		{
			var color = _spineCharacter.Modulate;
			color.A = alpha;
			_spineCharacter.Modulate = color;
		}

		if (_sprite != null && IsInstanceValid(_sprite))
		{
			var color = _sprite.Modulate;
			color.A = alpha;
			_sprite.Modulate = color;
		}

		ResolveOutlineSpines();
		foreach (Node outlineNode in _outlineSpineControllers)
		{
			if (!IsInstanceValid(outlineNode))
			{
				continue;
			}

			var color = _defaultOutlineModulate;
			color.A = Mathf.Clamp(_defaultOutlineModulate.A * alpha, 0.0f, 1.0f);
			outlineNode.Set("modulate", color);
		}
	}

	private void RestoreInvincibleFlash()
	{
		if (_spineCharacter != null && IsInstanceValid(_spineCharacter))
		{
			var color = _spineCharacter.Modulate;
			color.A = _defaultSpineAlpha;
			_spineCharacter.Modulate = color;
		}

		if (_sprite != null && IsInstanceValid(_sprite))
		{
			var color = _sprite.Modulate;
			color.A = _defaultSpriteAlpha;
			_sprite.Modulate = color;
		}

		ResolveOutlineSpines();
		foreach (Node outlineNode in _outlineSpineControllers)
		{
			if (IsInstanceValid(outlineNode))
			{
				outlineNode.Set("modulate", _defaultOutlineModulate);
			}
		}
	}

	/// <summary>开关投掷指示器（ThrowCoreEffect 调用）。</summary>
	public void EnableThrowIndicator(bool enable)
	{
		var indicator = GetThrowIndicatorNode();
		if (indicator != null && IsInstanceValid(indicator))
			((CanvasItem)indicator).Visible = enable;
	}

	/// <summary>获取投掷指示器节点。</summary>
	public Node? GetThrowIndicatorNode()
	{
		return GetNodeOrNull("ThrowTrajectoryPreview");
	}

	protected override void OnDeathFinalized()
		{
			ClearHitInvincibility();
			EffectController?.ClearAll();
			GameLogger.Warn(nameof(MainCharacter), "角色死亡！");
			// 可以在这里添加游戏结束逻辑
			// GetTree().ReloadCurrentScene();
		}
	}
}
