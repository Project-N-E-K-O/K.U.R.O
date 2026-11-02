using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Systems.FSM;
using Kuros.Actors.Heroes.States;
using Kuros.Actors.Heroes;
using Kuros.Systems.Inventory;
using Kuros.Items;
using Kuros.Managers;
using Kuros.Systems.AI;
using Kuros.UI;
using Kuros.Utils;
using Kuros.Core.Events;

public partial class SamplePlayer : GameActor, IPlayerStatsSource, IThrowableModifierProvider
{
	[ExportCategory("Debug")]
	[Export] public bool EnableStateDebugOverlay = false;
	[Export] public Vector2 DebugOverlayOffset = new(-90f, -90f);
	[Export(PropertyHint.Range, "8,128,1")] public int DebugOverlayFontSize = 14;
	[Export] public Color DebugOverlayColor = new(1f, 0.95f, 0.2f, 1f);
	
	[ExportCategory("Combat")]
	[Export] public Area2D AttackArea { get; private set; } = null!;
	[Export] public Area2D? HitArea { get; private set; }
	[Export] public bool SyncMainAttackAreaWithEquippedWeaponArea { get; set; } = true;	
	[Export] public bool FollowSyncedAttackAreaWithAttackBoneMotion { get; set; } = true;	
	[Export] public NodePath AttackMotionBonePath { get; set; } = new("SpineSprite/SpineBoneNode");	
	private CollisionShape2D? _attackCollisionShape;
	private Area2D? _cachedAttackAreaOwner;
	private CollisionShape2D? _mainAttackCollisionShape;	
	private Vector2 _defaultAttackShapePosition;
	private float _defaultAttackShapeRotation;
	private Vector2 _defaultAttackShapeScale;
	private Shape2D? _defaultAttackShape;
	// 缓存 AttackArea 的初始碰撞配置
	private uint _defaultAttackAreaCollisionMask;
	private uint _defaultAttackAreaCollisionLayer;
	private bool _defaultAttackAreaMonitoring;
	private bool _defaultAttackAreaMonitorable;
	private Vector2 _currentAttackShapeBasePosition;
	private float _currentAttackShapeBaseRotation;
	private Vector2 _currentAttackAreaBaseScale = Vector2.One;
	private Vector2 _attackAnchorRestLocalPosition;
	private Vector2 _currentAttackAnchorMotionOffset = Vector2.Zero;
	private Node2D? _attackMotionBoneNode;
	private PlayerItemAttachment? _itemAttachment;
	public PlayerItemAttachment? ItemAttachment => _itemAttachment;
	//private AiDecisionBridge? _aiDecisionBridge;
	//private AiDecisionExecutor? _aiDecisionExecutor;
	private readonly Godot.Collections.Array<Rid> _attackQueryExclude = new();
	//private Vector2 _aiMovementInput = Vector2.Zero;
	//private bool _aiRunPressed;
	//private bool _aiAttackQueued;
	//private bool _aiPickupQueued;
	//private bool _aiMoveLeftQueued;
	//private bool _aiMoveRightQueued;
	public PlayerFrozenState? FrozenState { get; private set; }
	public PlayerInventoryComponent? InventoryComponent { get; private set; }
	public InventoryContainer? Backpack => InventoryComponent?.Backpack;
	public PlayerWeaponSkillController? WeaponSkillController { get; private set; }
	
	[ExportCategory("UI")]
	[Export] public Label StatsLabel { get; private set; } = null!; // Drag & Drop in Editor
	// [ExportCategory("AI")]
	// [Export] public Key AiAutopilotToggleKey { get; set; } = Key.F6;

	[ExportCategory("Equipment")]
	/// <summary>
	/// 左手附件點的節點路徑（可在編輯器中設置）
	/// 如果未設置或路徑無效，會嘗試使用 LeftHandAttachmentName 進行搜索
	/// </summary>
	[Export] public NodePath? LeftHandAttachmentPath { get; set; }
	
	/// <summary>
	/// 左手附件點的節點名稱（用於後備搜索）
	/// 當 LeftHandAttachmentPath 無效時，會使用此名稱在子節點中搜索
	/// </summary>
	[Export] public string LeftHandAttachmentName { get; set; } = "left_hand_attachment";
	
	/// <summary>
	/// 缓存的左手附件点节点引用
	/// </summary>
	private Node2D? _cachedLeftHandAttachment;
	
	/// <summary>
	/// 标记是否已搜索过左手附件点（避免重复搜索和日志）
	/// </summary>
	private bool _leftHandAttachmentSearched = false;
	
	/// <summary>
	/// 当前左手装备的物品（从快捷栏获取）
	/// 右手保持小木剑（快捷栏索引0）
	/// </summary>
	public ItemDefinition? LeftHandItem { get; private set; }
	
	/// <summary>
	/// 当前左手物品对应的快捷栏槽位索引（0-4，对应数字键1-5）
	/// -1 表示未装备任何物品
	/// </summary>
	public int LeftHandSlotIndex { get; private set; } = -1;

	/// <summary>
	/// 预输入的目标槽位：攻击/投掷期间切换武器只更新视觉高亮（背包选中框跟随），
	/// 实际武器待状态结束后应用——视觉与判定分离，切换手感不卡顿。
	/// -1 = 无预输入。
	/// </summary>
	private int _pendingQuickBarSlot = -1;

	/// <summary>视觉选中的槽位（预输入优先）：攻击中返回玩家想切换的目标，否则为实际槽位。</summary>
	public int VisualQuickBarSlot => _pendingQuickBarSlot >= 0 ? _pendingQuickBarSlot : LeftHandSlotIndex;

	private int _score = 0;
	private int _gold = 0; // 金币数量
	private string _pendingAttackSourceState = string.Empty;
	private string _debugOverlayText = string.Empty;
	private Vector2 _aiMovementInput = Vector2.Zero;
	private bool _aiRunPressed;
	private bool _aiAttackQueued;
	private bool _aiPickupQueued;
	private bool _aiMoveLeftQueued;
	private bool _aiMoveRightQueued;
	public string LastMovementStateName { get; private set; } = "Idle";
	public bool AiInputOverrideEnabled { get; private set; }

	private readonly Kuros.Systems.InputTracking.InputHoldTracker _holdTracker = new();

	// IPlayerStatsSource interface implementation
	public event Action<int, int, int>? StatsUpdated;
	
	// CurrentHealth property for IPlayerStatsSource
	int IPlayerStatsSource.CurrentHealth => CurrentHealth;
	
	// MaxHealth property for IPlayerStatsSource (wraps base class field)
	int IPlayerStatsSource.MaxHealth => MaxHealth;
	
	// Score property for IPlayerStatsSource
	int IPlayerStatsSource.Score => _score;
	
	// Public properties for convenience
	public int Score => _score;
	
	// Signal for UI updates (Alternative to direct reference)
	[Signal] public delegate void StatsChangedEventHandler(int health, int score);
	[Signal] public delegate void GoldChangedEventHandler(int gold);
	//[Signal] public delegate void AiInputOverrideChangedEventHandler(bool enabled);

	//public bool AiInputOverrideEnabled { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("player");
		// 长按阈值与长按标志从设置读取（设置菜单可调）：
		// 白名单动作全部注册进仲裁器——同键时长按动作阈值触发、短按动作按下即触发
		// （如 dash=短按Shift、run=长按Shift 分流）；place 恒为长按（放置=长按，拾取=短按）
		ReapplyLongPressFlags();

		// 设置菜单勾选/取消"长按触发"或调阈值后即时同步到仲裁器（否则已实例化玩家用旧标志）
		var gsmSettings = Kuros.Managers.GameSettingsManager.Instance;
		if (gsmSettings != null)
		{
			gsmSettings.InputBindingsChanged += ReapplyLongPressFlags;
		}
		
		// Fallback: Try to find nodes if not assigned in editor (Backward compatibility)
		if (AttackArea == null) AttackArea = GetNodeOrNull<Area2D>("AttackArea");
		if (HitArea == null) HitArea = GetNodeOrNull<Area2D>("HitArea");
		if (FrozenState == null) FrozenState = StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
		if (StatsLabel == null) StatsLabel = GetNodeOrNull<Label>("../UI/PlayerStats");
		if (InventoryComponent == null) InventoryComponent = GetNodeOrNull<PlayerInventoryComponent>("Inventory");
		if (WeaponSkillController == null) WeaponSkillController = GetNodeOrNull<PlayerWeaponSkillController>("WeaponSkillController");
		_itemAttachment = GetNodeOrNull<PlayerItemAttachment>("ItemAttachment");
		//_aiDecisionBridge = GetNodeOrNull<AiDecisionBridge>("AiDecisionBridge");
		//_aiDecisionExecutor = GetNodeOrNull<AiDecisionExecutor>("AiDecisionExecutor");
		if (_itemAttachment != null)
		{
			var callable = new Callable(this, MethodName.OnEquippedAttackAreaChanged);
			if (!_itemAttachment.IsConnected(PlayerItemAttachment.SignalName.EquippedAttackAreaChanged, callable))
			{
				_itemAttachment.EquippedAttackAreaChanged += OnEquippedAttackAreaChanged;
			}
		}

		ResolveAttackMotionBoneNode();

		CacheMainAttackAreaDefaults();
		CallDeferred(MethodName.OnEquippedAttackAreaChanged);
		
		// 连接快捷栏变化信号，确保左手物品与选中槽位严格对应
		ConnectQuickBarSignals();
		
		// 设置左手默认选中快捷栏2（索引1）
		// 使用 CallDeferred 确保在快捷栏初始化完成后再设置
		CallDeferred(MethodName.InitializeLeftHandSelection);
		CallDeferred(MethodName.ApplyUnarmedSkillIfEmpty);
		
		UpdateStatsUI();
		//UpdateDebugOverlayText();
		//QueueRedraw();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		_holdTracker.Process((float)delta);
		UpdateSyncedAttackAreaAttackBoneMotion();

		// 攻击/投掷结束后应用预输入的武器切换（视觉高亮已提前跟随，此处同步实际武器）
		if (_pendingQuickBarSlot >= 0 && !IsWeaponSwitchLocked())
			ApplyPendingQuickBarSlot();

		if (!EnableStateDebugOverlay) return;

		UpdateDebugOverlayText();
		QueueRedraw();
	}

	public override void _Draw()
	{
		base._Draw();
		if (!EnableStateDebugOverlay) return;

		var font = ThemeDB.FallbackFont;
		if (font == null) return;

		DrawString(font, DebugOverlayOffset, _debugOverlayText, HorizontalAlignment.Left, -1f, DebugOverlayFontSize, DebugOverlayColor);
	}

	public override void _ExitTree()
	{
		var gsmSettings = Kuros.Managers.GameSettingsManager.Instance;
		if (gsmSettings != null)
		{
			gsmSettings.InputBindingsChanged -= ReapplyLongPressFlags;
		}

		if (_itemAttachment != null)
		{
			var callable = new Callable(this, MethodName.OnEquippedAttackAreaChanged);
			if (_itemAttachment.IsConnected(PlayerItemAttachment.SignalName.EquippedAttackAreaChanged, callable))
			{
				_itemAttachment.EquippedAttackAreaChanged -= OnEquippedAttackAreaChanged;
			}
		}

		base._ExitTree();
	}

	private void CacheMainAttackAreaDefaults()
	{
		if (AttackArea == null)
		{
			return;
		}

		_mainAttackCollisionShape = AttackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (_mainAttackCollisionShape == null)
		{
			foreach (Node child in AttackArea.GetChildren())
			{
				if (child is CollisionShape2D shape)
				{
					_mainAttackCollisionShape = shape;
					break;
				}
			}
		}

		if (_mainAttackCollisionShape == null)
		{
			return;
		}

		_defaultAttackShapePosition = _mainAttackCollisionShape.Position;
		_defaultAttackShapeRotation = _mainAttackCollisionShape.Rotation;
		_defaultAttackShapeScale = _mainAttackCollisionShape.Scale;
		_defaultAttackShape = _mainAttackCollisionShape.Shape?.Duplicate() as Shape2D;
		_currentAttackShapeBasePosition = _defaultAttackShapePosition;
		_currentAttackShapeBaseRotation = _defaultAttackShapeRotation;
		_currentAttackAreaBaseScale = AttackArea.Scale;
		_attackAnchorRestLocalPosition = Vector2.Zero;
		_currentAttackAnchorMotionOffset = Vector2.Zero;

		// 缓存 AttackArea 的初始碰撞配置
		CacheInitialAttackAreaDefaults();
	}

	private void OnEquippedAttackAreaChanged()
	{
		if (!SyncMainAttackAreaWithEquippedWeaponArea || AttackArea == null)
		{
			return;
		}

		_mainAttackCollisionShape ??= AttackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (_mainAttackCollisionShape == null)
		{
			return;
		}

		if (_itemAttachment == null)
		{
			RestoreDefaultMainAttackArea();
			return;
		}

		if (!_itemAttachment.TryGetEquippedAttackAreaTemplate(out var templateShape, out var templateTransform, out var templateMask) || templateShape == null)
		{
			RestoreDefaultMainAttackArea();
			return;
		}

		AttackArea.Monitoring = true;
		AttackArea.Monitorable = false;
		if (templateMask != 0)
		{
			AttackArea.CollisionMask = templateMask;
		}
		AttackArea.CollisionLayer = 0;

		// 武器实例挂玩家身上，与玩家 AttackArea 共享同一缩放链（玩家根 scale 0.33）——两侧抵消，
		// 烘焙只需保留武器场景局部缩放（武器根/AttackArea/shape）；除以 parentScale 会让玩家 shape 放大 1/0.33 倍
		Vector2 templateScale = GetGlobalScaleFromTransform(templateTransform);
		Vector2 bakedScale = templateScale;
		Shape2D syncedShape = DuplicateShapeWithBakedScale(templateShape, bakedScale);

		// The weapon scene's local transform is relative to its own root/icon setup,
		// not the player root. Only copy the shape size here and keep the player's
		// default hitbox anchor so the attack area remains in front of the character.
		// 例外：武器胶囊旋转 ±90°（横向胶囊）——CapsuleShape2D 本身垂直，左右拉伸只能靠节点旋转表达；
		// 烘焙轴缩放已按旋转后轴自动交换（GetGlobalScaleFromTransform 取轴长），这里补上节点旋转与锚定
		bool horizontalCapsule = syncedShape is CapsuleShape2D && IsCapsuleHorizontal(templateTransform);
		_currentAttackShapeBasePosition = ComputeForwardAnchoredAttackShapePosition(syncedShape, horizontalCapsule);
		_currentAttackShapeBaseRotation = horizontalCapsule ? Mathf.Pi * 0.5f : _defaultAttackShapeRotation;
		_currentAttackAreaBaseScale = new Vector2(Mathf.Abs(AttackArea.Scale.X), Mathf.Abs(AttackArea.Scale.Y));
		AttackArea.Scale = _currentAttackAreaBaseScale;
		_mainAttackCollisionShape.Scale = Vector2.One;
		_mainAttackCollisionShape.Shape = syncedShape;
		RefreshAttackAnchorTracking(resetOffset: true);
		ApplyAttackAreaFacingTransform(FacingRight);
	}

	/// <summary>恢复玩家默认 AttackArea（投掷类道具——投掷武器/投掷家具——选中时不继承其 AttackArea，用默认范围）。</summary>
	public void RestoreDefaultAttackArea() => RestoreDefaultMainAttackArea();

	private void RestoreDefaultMainAttackArea()
	{
		if (_mainAttackCollisionShape == null)
		{
			return;
		}

		_mainAttackCollisionShape.Position = _defaultAttackShapePosition;
		_mainAttackCollisionShape.Rotation = _defaultAttackShapeRotation;
		_mainAttackCollisionShape.Scale = _defaultAttackShapeScale;
		if (_defaultAttackShape != null)
		{
			_mainAttackCollisionShape.Shape = _defaultAttackShape.Duplicate() as Shape2D;
		}

		// 恢复 AttackArea 的碰撞配置
		if (AttackArea != null)
		{
			AttackArea.CollisionMask = _defaultAttackAreaCollisionMask;
			AttackArea.CollisionLayer = _defaultAttackAreaCollisionLayer;
			AttackArea.Monitoring = _defaultAttackAreaMonitoring;
			AttackArea.Monitorable = _defaultAttackAreaMonitorable;
		}

		_currentAttackShapeBasePosition = _defaultAttackShapePosition;
		_currentAttackShapeBaseRotation = _defaultAttackShapeRotation;
		_currentAttackAreaBaseScale = AttackArea != null
			? new Vector2(Mathf.Abs(AttackArea.Scale.X), Mathf.Abs(AttackArea.Scale.Y))
			: Vector2.One;
		RefreshAttackAnchorTracking(resetOffset: true);
		ApplyAttackAreaFacingTransform(FacingRight);
	}

	private void CacheInitialAttackAreaDefaults()
	{
		if (AttackArea == null)
		{
			return;
		}

		_defaultAttackAreaCollisionMask = AttackArea.CollisionMask;
		_defaultAttackAreaCollisionLayer = AttackArea.CollisionLayer;
		_defaultAttackAreaMonitoring = AttackArea.Monitoring;
		_defaultAttackAreaMonitorable = AttackArea.Monitorable;
	}

	private void RefreshAttackAnchorTracking(bool resetOffset)
	{
		if (resetOffset)
		{
			_currentAttackAnchorMotionOffset = Vector2.Zero;
		}

		if (!TryGetCurrentAttackAnchorLocalPosition(out var localPosition))
		{
			_attackAnchorRestLocalPosition = Vector2.Zero;
			return;
		}

		_attackAnchorRestLocalPosition = localPosition;
	}

	private void UpdateSyncedAttackAreaAttackBoneMotion()
	{
		if (!SyncMainAttackAreaWithEquippedWeaponArea || !FollowSyncedAttackAreaWithAttackBoneMotion)
		{
			return;
		}

		if (AttackArea == null || _mainAttackCollisionShape == null)
		{
			return;
		}

		if (!TryGetCurrentAttackAnchorLocalPosition(out var localPosition))
		{
			if (_currentAttackAnchorMotionOffset != Vector2.Zero)
			{
				_currentAttackAnchorMotionOffset = Vector2.Zero;
				ApplyAttackAreaFacingTransform(FacingRight);
			}
			return;
		}

		Vector2 newOffset = localPosition - _attackAnchorRestLocalPosition;
		if (newOffset.IsEqualApprox(_currentAttackAnchorMotionOffset))
		{
			return;
		}

		_currentAttackAnchorMotionOffset = newOffset;
		ApplyAttackAreaFacingTransform(FacingRight);
	}

	private bool TryGetCurrentAttackAnchorLocalPosition(out Vector2 localPosition)
	{
		if (_itemAttachment != null && _itemAttachment.TryGetAttackAnchorGlobalPosition(out var globalPosition))
		{
			localPosition = ToLocal(globalPosition);
			return true;
		}

		ResolveAttackMotionBoneNode();
		if (_attackMotionBoneNode != null && IsInstanceValid(_attackMotionBoneNode))
		{
			localPosition = ToLocal(_attackMotionBoneNode.GlobalPosition);
			return true;
		}

		localPosition = Vector2.Zero;
		return false;
	}

	private void ResolveAttackMotionBoneNode()
	{
		if (_attackMotionBoneNode != null && IsInstanceValid(_attackMotionBoneNode))
		{
			return;
		}

		if (AttackMotionBonePath != null && !AttackMotionBonePath.IsEmpty)
		{
			_attackMotionBoneNode = GetNodeOrNull<Node2D>(AttackMotionBonePath);
			if (_attackMotionBoneNode != null)
			{
				return;
			}

			_attackMotionBoneNode = GetNodeOrNull<Node2D>($"../{AttackMotionBonePath}");
			if (_attackMotionBoneNode != null)
			{
				return;
			}
		}

		_attackMotionBoneNode = GetNodeOrNull<Node2D>("SpineSprite/SpineBoneNode")
			?? FindChild("SpineBoneNode", recursive: true, owned: false) as Node2D;
	}

	private void ApplyAttackAreaFacingTransform(bool faceRight)
	{
		if (AttackArea == null || _mainAttackCollisionShape == null)
		{
			return;
		}

		AttackArea.Scale = new Vector2(
			Mathf.Abs(_currentAttackAreaBaseScale.X),
			Mathf.Abs(_currentAttackAreaBaseScale.Y));

		Vector2 basePosition = _currentAttackShapeBasePosition;
		Vector2 facingPosition = new Vector2(
			faceRight ? Mathf.Abs(basePosition.X) : -Mathf.Abs(basePosition.X),
			basePosition.Y);
		_mainAttackCollisionShape.Position = facingPosition + _currentAttackAnchorMotionOffset;

		_mainAttackCollisionShape.Rotation = faceRight
			? _currentAttackShapeBaseRotation
			: -_currentAttackShapeBaseRotation;
	}

	private void ApplyUnarmedSkillIfEmpty()
	{
		if (InventoryComponent == null) return;
		//if (InventoryComponent.GetActiveCombatWeaponDefinition() == null)
		if (InventoryComponent.GetSelectedBackpackStack() == null)

		{
			WeaponSkillController?.ApplyUnarmedFallback();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsFalling) return; // 不吞掉暂停菜单等全局输入。
		// 处理数字键 1-5 切换快捷栏物品（对应快捷栏槽位 0-4）
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			// if (keyEvent.Keycode == AiAutopilotToggleKey)
			// {
			// 	_aiDecisionExecutor ??= GetNodeOrNull<AiDecisionExecutor>("AiDecisionExecutor");
			// 	if (_aiDecisionExecutor != null)
			// 	{
			// 		_aiDecisionExecutor.SetAutopilotEnabled(!_aiDecisionExecutor.AutoPilotEnabled);
			// 		GameLogger.Info(nameof(SamplePlayer), $"AI autopilot toggled: {(_aiDecisionExecutor.AutoPilotEnabled ? "ON" : "OFF")}");
			// 	}

			// 	GetViewport().SetInputAsHandled();
			// 	return;
			// }

			// bool isPipeHotkey = keyEvent.Keycode == Key.Backslash || keyEvent.Unicode == '|';
			// if (isPipeHotkey)
			if (AiInputOverrideEnabled)
			{
				// _ = RequestAiDecisionTestAsync();
				GetViewport().SetInputAsHandled();
				return;
			}

			// if (_aiDecisionExecutor?.AutoPilotEnabled == true)
			// {
			// 	GetViewport().SetInputAsHandled();
			// 	return;
			// }

			int? slotIndex = null;
			
			// 数字键 1-5 对应快捷栏槽位 0-4
			if (keyEvent.Keycode == Key.Key1)
			{
				slotIndex = 0; // 快捷栏槽位1
			}
			else if (keyEvent.Keycode == Key.Key2)
			{
				slotIndex = 1; // 快捷栏槽位2
			}
			else if (keyEvent.Keycode == Key.Key3)
			{
				slotIndex = 2; // 快捷栏槽位3
			}
			else if (keyEvent.Keycode == Key.Key4)
			{
				slotIndex = 3; // 快捷栏槽位4
			}
			else if (keyEvent.Keycode == Key.Key5)
			{
				slotIndex = 4; // 快捷栏槽位5
			}
			
			if (slotIndex.HasValue)
			{
				// 攻击/投掷中的切换由预输入处理（视觉立即跟随，实际武器状态结束后应用）
				SwitchToQuickBarSlot(slotIndex.Value);
				GetViewport().SetInputAsHandled();
			}
		}

		// weapon_skill_block 已废弃（废案）：此处原为对该动作的输入检查，
		// 对不存在的动作调用 IsActionPressed 会让引擎每个输入事件打一条 ERROR，
		// 控制台 I/O 拖帧（实测 90 秒 484 次报错，与周期性帧尖峰强相关）。

		base._UnhandledInput(@event);
	}

	public void SetAiInputOverrideEnabled(bool enabled)
	{
		if (AiInputOverrideEnabled == enabled)
		{
			return;
		}

		AiInputOverrideEnabled = enabled;
		if (!enabled)
		{
			ClearAiControlCommands();
		}

		//EmitSignal(SignalName.AiInputOverrideChanged, enabled);
	}

	public void SetAiDesiredMovement(Vector2 movementInput, bool runPressed)
	{
		_aiMovementInput = movementInput.LimitLength(1f);
		_aiRunPressed = runPressed;
	}

	public void QueueAiAttack()
	{
		_aiAttackQueued = true;
	}

	public void QueueAiPickup()
	{
		_aiPickupQueued = true;
	}

	public void QueueAiMoveLeft()
	{
		_aiMoveLeftQueued = true;
	}

	public void QueueAiMoveRight()
	{
		_aiMoveRightQueued = true;
	}

	public void ClearAiControlCommands()
	{
		_aiMovementInput = Vector2.Zero;
		_aiRunPressed = false;
		_aiAttackQueued = false;
		_aiPickupQueued = false;
		_aiMoveLeftQueued = false;
		_aiMoveRightQueued = false;
	}

	/// <summary>攻击输入屏蔽(A_010 瞄准模式:攻击键被征用为"确认放置")——
	/// 状态机的攻击触发/按住连击查询全部经 IsControlledActionPressed / ConsumeControlledActionJustPressed,
	/// 置位期间这两处对 "attack" 一律返回 false;瞄准控制器自己用 Input 直读检测确认。</summary>
	public bool SuppressAttackInput { get; set; }
	public bool IsFalling => StateMachine?.CurrentState is PlayerFallState;
	public void ClearFallInputBuffers()
	{
		_holdTracker.ResetTransientState();
		_pendingQuickBarSlot = -1;
		ClearAiControlCommands();
		if (StateMachine != null)
			foreach (Node state in StateMachine.GetChildren())
				if (state is PlayerState playerState) playerState.ClearBufferedInputs();
	}

	/// <summary>拾取/放置输入屏蔽(A_010 瞄准模式:右键被征用为"取消")——
	/// take_up/place 的全部消费点(Idle/RunHolding 短按拾取、ItemInteraction 长按放置/短按拾取)
	/// 都经 WasActionShortPressed / WasActionLongPressTriggered,置位期间这两个方法对 take_up/place 返回 false。</summary>
	public bool SuppressInteractInput { get; set; }

	public Vector2 GetControlledMovementInput()
	{
		if (IsFalling) return Vector2.Zero;
		return AiInputOverrideEnabled
			? _aiMovementInput
			: Input.GetVector("move_left", "move_right", "move_forward", "move_back");
	}

	public bool IsControlledActionPressed(string actionName)
	{
		if (IsFalling) return false;
		if (!AiInputOverrideEnabled)
		{
			if (actionName == "attack" && SuppressAttackInput)
				return false;
			if (actionName == "attack" && UIManager.IsMouseOverUI && Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsKeyPressed(Key.Enter) && !Input.IsKeyPressed(Key.F))
				return false;
			// 走仲裁器：同键长短按分流（run 等长按动作达阈值后才视为按住）
			return _holdTracker.IsActionHeld(actionName);
		}

		return actionName switch
		{
			"run" => _aiRunPressed,
			_ => false
		};
	}

	public bool ConsumeControlledActionJustPressed(string actionName)
	{
		if (IsFalling) return false;
		if (!AiInputOverrideEnabled)
		{
			if (actionName == "attack" && SuppressAttackInput)
				return false;
			if (actionName == "attack" && UIManager.IsMouseOverUI && Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsKeyPressed(Key.Enter) && !Input.IsKeyPressed(Key.F))
				return false;
			// 走仲裁器：同键长短按分流（短按动作延迟到松开确认）
			return _holdTracker.WasActionJustPressed(actionName);
		}

		return actionName switch
		{
			"attack" => ConsumeAiFlag(ref _aiAttackQueued),
			"take_up" => ConsumeAiFlag(ref _aiPickupQueued),
			"move_left" => ConsumeAiFlag(ref _aiMoveLeftQueued),
			"move_right" => ConsumeAiFlag(ref _aiMoveRightQueued),
			_ => false
		};
	}

	/// <summary>重新应用全部动作的长按标志与阈值到仲裁器（初始与设置变更时调用）。</summary>
	private void ReapplyLongPressFlags()
	{
		var gsm = Kuros.Managers.GameSettingsManager.Instance;
		float holdThreshold = gsm?.HoldThresholdSeconds ?? 0.35f;
		foreach (var (action, _) in Kuros.Core.InputActions.RebindableActions)
		{
			bool isLongPress = gsm?.IsActionLongPress(action) ?? action == "place";
			_holdTracker.Register(action, longPressThreshold: holdThreshold, isLongPress: isLongPress);
		}
	}

	/// <summary>全局仲裁即时按下查询（NPC/电梯/对话等外部节点无玩家引用时用）：
	/// 从 player 组解析玩家走仲裁器；玩家不可用时回退 Input 直读。</summary>
	public static bool IsActionJustPressedGlobal(string actionName)
	{
		if (Godot.Engine.GetMainLoop() is SceneTree tree
			&& tree.GetFirstNodeInGroup("player") is SamplePlayer player)
		{
			return player.IsActionJustPressedArbitrated(actionName);
		}
		return Godot.Input.IsActionJustPressed(actionName);
	}

	/// <summary>非消耗的仲裁即时按下查询（攻击模板等每帧多调场景用；同键长按激活时短按动作返回 false）。</summary>
	public bool IsActionJustPressedArbitrated(string actionName)
	{
		if (IsFalling) return false;
		if (actionName == "attack" && UIManager.IsMouseOverUI && Input.IsMouseButtonPressed(MouseButton.Left))
			return false;
		return _holdTracker.WasActionJustPressed(actionName);
	}

	/// <summary>仲裁按住查询（长按激活时短按动作的 hold 被屏蔽，避免同键长按连击）。</summary>
	public bool IsActionHeldArbitrated(string actionName)
	{
		if (IsFalling) return false;
		return _holdTracker.IsActionHeld(actionName);
	}

	private static bool ConsumeAiFlag(ref bool flag)
	{
		if (!flag)
		{
			return false;
		}

		flag = false;
		return true;
	}
	public bool IsActionLongPressHeld(string actionName) => !IsFalling && _holdTracker.IsLongPressHeld(actionName);
	public bool WasActionLongPressTriggered(string actionName)
	{
		if (IsFalling) return false;
		if (SuppressInteractInput && (actionName == "take_up" || actionName == "place")) return false;
		return _holdTracker.WasLongPressTriggered(actionName);
	}
	public bool WasActionShortPressed(string actionName)
	{
		if (IsFalling) return false;
		if (SuppressInteractInput && (actionName == "take_up" || actionName == "place")) return false;
		return _holdTracker.WasShortPressed(actionName);
	}
	public bool WasActionJustPressed(string actionName) => !IsFalling && _holdTracker.WasActionJustPressed(actionName);
	public float GetActionHoldDuration(string actionName) => _holdTracker.GetHoldDuration(actionName);

	// private async System.Threading.Tasks.Task RequestAiDecisionTestAsync()
	// {
	// 	_aiDecisionBridge ??= GetNodeOrNull<AiDecisionBridge>("AiDecisionBridge");
	// 	if (_aiDecisionBridge == null)
	// 	{
	// 		GameLogger.Warn(nameof(SamplePlayer), "AI test skipped: AiDecisionBridge node not found.");
	// 		return;
	// 	}

	// 	if (_aiDecisionBridge.RequestInFlight)
	// 	{
	// 		GameLogger.Info(nameof(SamplePlayer), "AI test skipped: request already in flight.");
	// 		return;
	// 	}

	// 	GameLogger.Info(nameof(SamplePlayer), "AI test request started (|). ");
	// 	var result = await _aiDecisionBridge.RequestDecisionAsync("根据当前游戏状态给出下一步行动建议，严格返回 JSON，字段为 intent, target, urgency, duration_seconds, reason。");
	// 	if (result.Success)
	// 	{
	// 		string text = result.ResponseText ?? string.Empty;
	// 		if (text.Length > 800)
	// 		{
	// 			text = text.Substring(0, 800) + "...<truncated>";
	// 		}
	// 		GameLogger.Info(nameof(SamplePlayer), $"AI test response(done_reason={result.DoneReason}, used_thinking_fallback={result.UsedThinkingFallback}): {text}");
	// 	}
	// 	else
	// 	{
	// 		GameLogger.Error(nameof(SamplePlayer), $"AI test failed: {result.ErrorMessage}");
	// 	}
	// }
	
	/// <summary>
	/// 切换到指定快捷栏槽位的物品（预输入式）：
	/// 视觉高亮（背包选中框）立即跟随目标槽位；
	/// 攻击/投掷状态中只记录预输入，实际武器（LeftHandItem/判定）待状态结束后应用。
	/// 严格绑定：LeftHandSlotIndex 和 LeftHandItem 必须严格对应。
	/// </summary>
	/// <param name="slotIndex">快捷栏槽位索引（0-4，对应数字键1-5）</param>
	private bool IsWeaponSwitchLocked()
	{
		if (IsFalling) return true;
		var currentState = StateMachine?.CurrentState;
		string name = currentState?.Name ?? string.Empty;
		if (name == "Throw") return true; // 投掷是单次动作，全程锁定
		if (name == "Attack" && currentState is PlayerAttackState attackState)
		{
			// 连击时状态不退出，但每个攻击的 Recovery（后摇）是切换窗口——判定已完成
			return !attackState.IsInRecovery;
		}
		return false;
	}

	/// <summary>
	/// UI（鼠标滚轮切换等）调用的公开切换入口：接受切换意图（视觉立即跟随），
	/// 实际切换由预输入机制在状态允许时应用。返回 true 表示意图已接受。
	/// </summary>
	public bool TrySwitchQuickBarSlot(int slotIndex)
	{
		if (IsFalling) return false;
		if (slotIndex < 0 || slotIndex > 4) return false;
		SwitchToQuickBarSlot(slotIndex);
		return true;
	}

	private void SwitchToQuickBarSlot(int slotIndex)
	{
		// 验证槽位索引范围（0-4）
		if (slotIndex < 0 || slotIndex > 4)
		{
			return;
		}

		// 家具槽有物品时禁止切换快捷栏槽位（硬性约束，不做预输入）
		if (InventoryComponent?.HasFurnitureItem == true)
		{
			return;
		}

		// 预输入：记录目标槽位，视觉高亮立即跟随（背包选中框反馈——切换手感不卡顿）
		_pendingQuickBarSlot = slotIndex;
		UpdateBattleHUDHandHighlight();

		// 攻击/投掷中只记录，状态结束后由 _Process 应用（视觉与判定分离）
		if (!IsWeaponSwitchLocked())
			ApplyPendingQuickBarSlot();
	}

	/// <summary>应用预输入的槽位：实际武器/物品/手上视觉切换（攻击/投掷结束后调用）。</summary>
	private void ApplyPendingQuickBarSlot()
	{
		if (_pendingQuickBarSlot < 0) return;
		int slotIndex = _pendingQuickBarSlot;
		_pendingQuickBarSlot = -1;

		// 如果 QuickBar 还未初始化，只记录选中的槽位索引，稍后在 QuickBar 设置后会同步
		if (InventoryComponent?.QuickBar == null)
		{
			LeftHandSlotIndex = slotIndex;
			if (InventoryComponent != null)
				InventoryComponent.SelectedQuickBarSlot = slotIndex;
			return;
		}

		// 严格绑定：设置 LeftHandSlotIndex，然后同步 LeftHandItem
		LeftHandSlotIndex = slotIndex;
		if (InventoryComponent != null)
			InventoryComponent.SelectedQuickBarSlot = slotIndex;

		SyncLeftHandItemFromSlot();
		UpdateHandItemVisual();
	}
	
	/// <summary>当前构筑对一次性投掷道具的修饰聚合(IThrowableModifierProvider):
	/// 由玩家身上实现 IThrowableModifiersContributor 的效果逐层叠加(B_001 轻量化/B_002 重量化等)。
	/// 投掷与轨迹预览单点消费,无卡时返回 None(原行为)。</summary>
	public Kuros.Items.ThrowableModifiers GetThrowableModifiers()
	{
		Kuros.Items.ThrowableModifiers mods = Kuros.Items.ThrowableModifiers.None;
		EffectController?.ForEachEffect<Kuros.Items.IThrowableModifiersContributor>(
			c => mods = c.ModifyThrowableModifiers(mods));
		return mods;
	}

	/// <summary>
	/// 同步左手物品：从当前选中的快捷栏槽位获取物品，确保严格对应
	/// </summary>
	public void SyncLeftHandItemFromSlot()
	{
		// 家具槽优先：当家具槽有物品时，始终使用家具槽的物品
		if (InventoryComponent?.HasFurnitureItem == true)
		{
			LeftHandItem = InventoryComponent.FurnitureSlotStack!.Item;
			return;
		}

		if (LeftHandSlotIndex < 0 || LeftHandSlotIndex > 4)
		{
			// 如果槽位索引无效，清除左手物品
			LeftHandItem = null;
			return;
		}
		
		if (InventoryComponent?.QuickBar == null)
		{
			LeftHandItem = null;
			return;
		}
		
		var stack = InventoryComponent.QuickBar.GetStack(LeftHandSlotIndex);
		
		// 检查槽位是否有有效物品（排除空白道具）
		if (stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item")
		{
			// 严格绑定：LeftHandItem 必须等于选中槽位的物品
			LeftHandItem = stack.Item;
		}
		else
		{
			// 槽位为空或只有空白道具，清除左手物品
			LeftHandItem = null;
		}
	}
	
	/// <summary>
	/// 快捷栏槽位变化时的回调：如果变化的是当前选中的槽位，同步更新左手物品
	/// </summary>
	private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
	{
		// 如果变化的是当前选中的槽位，同步更新左手物品
		if (slotIndex == LeftHandSlotIndex)
		{
			SyncLeftHandItemFromSlot();
			UpdateHandItemVisual();
		}
	}
	
	/// <summary>
	/// 快捷栏整体变化时的回调：同步更新左手物品
	/// </summary>
	private void OnQuickBarInventoryChanged()
	{
		// 如果当前有选中的槽位，同步更新左手物品
		if (LeftHandSlotIndex >= 0 && LeftHandSlotIndex <= 4)
		{
			SyncLeftHandItemFromSlot();
			UpdateHandItemVisual();
		}
	}

	/// <summary>
	/// 家具槽变化时的回调：同步更新左手物品
	/// </summary>
	private void OnFurnitureSlotChanged()
	{
		// 家具槽清空后（投掷/放下），恢复快捷栏选中状态。
		// 持家具期间 SwitchToQuickBarSlot 被阻断，LeftHandSlotIndex 可能已冻结或为 -1。
		// 主动重新切换到最后记忆的槽位，确保 LeftHandItem、LeftHandSlotIndex、
		// SelectedQuickBarSlot 三者重新严格对应，避免空手攻击视觉异常。
		if (InventoryComponent?.HasFurnitureItem == false)
		{
			int targetSlot = (LeftHandSlotIndex >= 0 && LeftHandSlotIndex <= 4)
				? LeftHandSlotIndex
				: (InventoryComponent?.SelectedQuickBarSlot ?? 0);
			// SwitchToQuickBarSlot 内部会调用 SyncLeftHandItemFromSlot + UpdateHandItemVisual
			SwitchToQuickBarSlot(targetSlot);
			return;
		}
		SyncLeftHandItemFromSlot();
		UpdateHandItemVisual();
	}
	
	/// <summary>
	/// 更新手上物品的视觉显示
	/// </summary>
	public void UpdateHandItemVisual()
	{
		// 獲取左手附件點（使用緩存和後備機制）
		var leftHandAttachment = GetLeftHandAttachment();
		
		if (leftHandAttachment != null)
		{
			// 查找左手附件点下的所有子节点（这些是附加的物品）
			var children = leftHandAttachment.GetChildren();
			foreach (Node child in children)
			{
				if (child is Node2D node2d)
				{
					// 如果左手有物品，显示；如果没有，隐藏
					node2d.Visible = LeftHandItem != null;
				}
			}
		}
		// 注意：如果找不到左手附件点，静默忽略。这不是致命错误，可能场景中未配置左手物品显示功能。
	}
	
	/// <summary>
	/// 獲取左手附件點節點，使用緩存和健壯的後備機制
	/// 優先使用編輯器設置的路徑，然後嘗試按名稱搜索
	/// </summary>
	/// <returns>左手附件點節點，如果找不到則返回 null</returns>
	private Node2D? GetLeftHandAttachment()
	{
		// 如果已經緩存了有效的節點引用，直接返回
		if (_cachedLeftHandAttachment != null && IsInstanceValid(_cachedLeftHandAttachment))
		{
			return _cachedLeftHandAttachment;
		}

		// 允许重试搜索，避免场景延迟挂载导致永久找不到左手挂点。
		// 仅用于抑制重复“未找到”日志，不阻止后续再次解析。
		_leftHandAttachmentSearched = true;
		
		// 方法1：嘗試使用編輯器設置的路徑
		if (LeftHandAttachmentPath?.IsEmpty == false)
		{
			var nodeFromPath = GetNodeOrNull<Node2D>(LeftHandAttachmentPath);
			if (nodeFromPath != null)
			{
				_cachedLeftHandAttachment = nodeFromPath;
				GD.Print($"GetLeftHandAttachment: Found attachment point via editor path: {LeftHandAttachmentPath}");
				return _cachedLeftHandAttachment;
			}
		}
		
		// 方法2：使用 FindChild 按名稱搜索（後備機制）
		if (!string.IsNullOrEmpty(LeftHandAttachmentName))
		{
			var nodeByName = FindChild(LeftHandAttachmentName, recursive: true, owned: false) as Node2D;
			if (nodeByName != null)
			{
				_cachedLeftHandAttachment = nodeByName;
				GD.Print($"GetLeftHandAttachment: Found attachment point via FindChild with name: '{LeftHandAttachmentName}'");
				return _cachedLeftHandAttachment;
			}
		}
		
		// 方法3：搜索帶有 "left_hand" 組的節點
		var nodesInGroup = GetTree().GetNodesInGroup("left_hand_attachment");
		foreach (var node in nodesInGroup)
		{
			// 檢查是否是此玩家的子節點
			if (node is Node2D node2d && IsAncestorOf(node2d))
			{
				_cachedLeftHandAttachment = node2d;
				GD.Print($"GetLeftHandAttachment: Found attachment point via group 'left_hand_attachment': {node2d.GetPath()}");
				return _cachedLeftHandAttachment;
			}
		}
		
		// 所有方法都失敗 - 这不是致命错误，左手物品视觉显示功能将被禁用
		// 如需启用此功能，请在 Player 场景中添加名为 'left_hand_attachment' 的 Node2D 子节点
		return null;
	}
	
	/// <summary>
	/// 清除左手附件點的緩存（當場景結構改變時調用）
	/// </summary>
	public void InvalidateLeftHandAttachmentCache()
	{
		_cachedLeftHandAttachment = null;
		_leftHandAttachmentSearched = false;
	}
	
	/// <summary>
	/// 通知 BattleHUD 更新左右手高亮
	/// </summary>
	private void UpdateBattleHUDHandHighlight()
	{
		BattleHUD? battleHUD = null;
		if (UIManager.Instance != null)
		{
			battleHUD = UIManager.Instance.GetUI<BattleHUD>("BattleHUD");
		}
		
		if (battleHUD == null)
		{
			// 备用方案：通过场景树查找
			battleHUD = GetTree().GetFirstNodeInGroup("ui") as BattleHUD;
		}
		
		if (battleHUD != null)
		{
			// 视觉高亮槽位：预输入优先（攻击中显示玩家想切换的目标），否则显示实际槽位
			int visualSlot = _pendingQuickBarSlot >= 0 ? _pendingQuickBarSlot : LeftHandSlotIndex;
			battleHUD.CallDeferred(BattleHUD.MethodName.UpdateHandSlotHighlight, visualSlot, -1);
		}
	}
	
	/// <summary>
	/// 初始化左手选择：不自动选中任何槽位，避免未按键时出现默认高亮
	/// 仅当已有有效槽位时做同步
	/// </summary>
	public void InitializeLeftHandSelection()
	{
		if (LeftHandSlotIndex >= 0 && LeftHandSlotIndex <= 4)
		{
			// 即使已经选中，也要确保同步
			if (InventoryComponent != null)
			{
				InventoryComponent.SelectedQuickBarSlot = LeftHandSlotIndex;
			}
			SyncLeftHandItemFromSlot();
			UpdateHandItemVisual();
			UpdateBattleHUDHandHighlight();
		}
		else
		{
			// 无有效选择时保持未选中状态，不触发默认高亮。
			LeftHandItem = null;
		}
	}
	
	/// <summary>
	/// 连接快捷栏变化信号，确保左手物品与选中槽位严格对应
	/// 可以在快捷栏被设置后调用此方法来确保信号连接
	/// </summary>
	public void ConnectQuickBarSignals()
	{
		if (InventoryComponent?.QuickBar != null)
		{
			// 断开之前的连接（如果存在），避免重复连接
			InventoryComponent.QuickBar.SlotChanged -= OnQuickBarSlotChanged;
			InventoryComponent.QuickBar.InventoryChanged -= OnQuickBarInventoryChanged;
			
			// 连接信号
			InventoryComponent.QuickBar.SlotChanged += OnQuickBarSlotChanged;
			InventoryComponent.QuickBar.InventoryChanged += OnQuickBarInventoryChanged;
			
			// 通知 PlayerItemAttachment 订阅 QuickBar 事件
			var itemAttachment = GetNodeOrNull<PlayerItemAttachment>("ItemAttachment");
			itemAttachment?.SubscribeToQuickBar();
			
			// 如果当前有选中的槽位，同步一次左手物品（可能是在 QuickBar 可用之前设置的）
			if (LeftHandSlotIndex >= 0 && LeftHandSlotIndex <= 4)
			{
				// 同步 SelectedQuickBarSlot
				InventoryComponent.SelectedQuickBarSlot = LeftHandSlotIndex;
				
				SyncLeftHandItemFromSlot();
				UpdateHandItemVisual();
				UpdateBattleHUDHandHighlight();
			}
		}

		// 订阅家具槽变化事件
		if (InventoryComponent != null)
		{
			InventoryComponent.FurnitureSlotChanged -= OnFurnitureSlotChanged;
			InventoryComponent.FurnitureSlotChanged += OnFurnitureSlotChanged;
		}
	}
	
	/// <summary>
	/// 清除左手物品选择（用于放下物品时）
	/// </summary>
	public void ClearLeftHandItem()
	{
		LeftHandItem = null;
		LeftHandSlotIndex = -1;
		UpdateHandItemVisual();
		UpdateBattleHUDHandHighlight();
	}
	
	public void RequestAttackFromState(string stateName)
	{
		_pendingAttackSourceState = stateName;
	}

	public string ConsumeAttackRequestSource()
	{
		string source = _pendingAttackSourceState;
		_pendingAttackSourceState = string.Empty;
		return source;
	}

	public void NotifyMovementState(string stateName)
	{
		LastMovementStateName = stateName;
	}

	
	// Override FlipFacing to handle AttackArea flipping correctly when turning
	public override void FlipFacing(bool faceRight)
	{
		base.FlipFacing(faceRight);
		
		// When the player turns around, we need to ensure the AttackArea flips accordingly.
		// This is better than doing it in PerformAttackCheck because physics has time to update.
		if (AttackArea != null)
		{
			 // We assume the AttackArea is centered or offset. If offset, we flip the offset.
			 // Check if AttackArea parent is NOT the flipped visual (to avoid double flipping)
			 if (AttackArea.GetParent() != _spineCharacter && AttackArea.GetParent() != _sprite)
			 {
				RefreshAttackAnchorTracking(resetOffset: true);
				ApplyAttackAreaFacingTransform(faceRight);
			 }
		}
	}
	
	public TargetableFactions CurrentAttackTargetableFactions { get; set; } =
		TargetableFactions.Enemy | TargetableFactions.WorldItem;

	/// <summary>基础伤害倍率（构筑效果如"空载增幅"写入,条件达成时 ≠1;两处 PerformAttackCheck 共用）。</summary>
	public float BasicAttackMultiplier { get; set; } = 1f;

	public void PerformAttackCheck()
	{
		AttackTimer = AttackCooldown;
		// GameLogger.Info(nameof(SamplePlayer), "=== Player attacking frame! ===");

		var activeAttackArea = ResolveAttackAreaForHitDetection(out string areaSource);
		if (activeAttackArea == null)
		{
			GameLogger.Error(nameof(SamplePlayer), "AttackArea is missing! Assign it in Inspector.");
			return;
		}

		// GameLogger.Info(nameof(SamplePlayer), $"AttackArea Source: {areaSource}, Node: {activeAttackArea.GetPath()}");
		// GameLogger.Info(nameof(SamplePlayer), $"AttackArea Detail: {DescribeAttackArea(activeAttackArea)}");

		int hitCount = ApplyDamageWithArea(AttackDamage * BasicAttackMultiplier, (target, isFallback) =>
		{
			// string suffix = isFallback ? " (fallback)" : string.Empty;
			// GameLogger.Info(nameof(SamplePlayer), $"Hit enemy{suffix}: {target.Name}");
		});

		if (hitCount == 0)
		{
			// GameLogger.Info(nameof(SamplePlayer), "No enemies hit!");
		}
	}

	protected int ApplyDamageWithArea(float damageAmount, Action<GameActor, bool>? onHit)
	{
		var activeAttackArea = ResolveAttackAreaForHitDetection();
		if (activeAttackArea == null)
		{
			return 0;
		}

		int hitCount = ApplyDamageWithSpecificArea(activeAttackArea, damageAmount, onHit);
		// GameLogger.Info(nameof(SamplePlayer), $"AttackArea hit test: {activeAttackArea.GetPath()} -> {hitCount} hit(s)");
		if (hitCount == 0 && AttackArea != null && activeAttackArea != AttackArea)
		{
			// GameLogger.Info(nameof(SamplePlayer), $"WeaponArea produced 0 hit(s), fallback to PlayerArea: {AttackArea.GetPath()}");
			hitCount = ApplyDamageWithSpecificArea(AttackArea, damageAmount, onHit);
			// GameLogger.Info(nameof(SamplePlayer), $"PlayerArea fallback hit test: {AttackArea.GetPath()} -> {hitCount} hit(s)");
		}

		return hitCount;
	}

	public Area2D? ResolveAttackAreaForHitDetection()
	{
		return ResolveAttackAreaForHitDetection(out _);
	}

	private Area2D? ResolveAttackAreaForHitDetection(out string areaSource)
	{
		if (AttackArea == null)
		{
			areaSource = "PlayerArea";
			return null;
		}

		// When sync mode is enabled, the player's own AttackArea is the single
		// authoritative hitbox. Do not fall back to the attached weapon Area2D,
		// because that node follows icon/bone transforms and can inherit unwanted
		// rotation even though the synced player hitbox should only react to facing.
		if (SyncMainAttackAreaWithEquippedWeaponArea)
		{
			areaSource = "PlayerAreaSynced";
			return AttackArea;
		}

		var itemAttachment = GetNodeOrNull<PlayerItemAttachment>("ItemAttachment");
		var attachedWeaponArea = itemAttachment?.GetEquippedAttackArea();
		if (IsAttackAreaUsable(attachedWeaponArea))
		{
			areaSource = "WeaponAreaAttached";
			return attachedWeaponArea;
		}

		var leftHandAttachment = GetLeftHandAttachment();
		if (leftHandAttachment == null)
		{
			GameLogger.Info(nameof(SamplePlayer), "AttackArea fallback -> PlayerArea (left hand attachment not found)");
			areaSource = "PlayerArea";
			return AttackArea;
		}

		var weaponArea = FindUsableWeaponAttackArea(leftHandAttachment);
		if (weaponArea != null)
		{
			areaSource = "WeaponArea";
			return weaponArea;
		}

		GameLogger.Info(nameof(SamplePlayer), $"AttackArea fallback -> PlayerArea (no usable weapon area under {leftHandAttachment.GetPath()})");

		areaSource = "PlayerArea";
		return AttackArea;
	}

	private static string DescribeAttackArea(Area2D area)
	{
		var shapeNode = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shapeNode == null)
		{
			foreach (Node child in area.GetChildren())
			{
				if (child is CollisionShape2D shape)
				{
					shapeNode = shape;
					break;
				}
			}
		}

		string shapeName = shapeNode?.Shape?.GetType().Name ?? "None";
		Vector2 pos = shapeNode?.GlobalPosition ?? area.GlobalPosition;
		float rot = shapeNode?.GlobalRotationDegrees ?? area.GlobalRotationDegrees;
		Vector2 scale = shapeNode?.GlobalScale ?? area.GlobalScale;
		int overlapAreas = area.GetOverlappingAreas().Count;
		int overlapBodies = area.GetOverlappingBodies().Count;
		return $"shape={shapeName}, globalPos={pos}, rot={rot:F2}, scale={scale}, layer={area.CollisionLayer}, mask={area.CollisionMask}, overlaps(area={overlapAreas}, body={overlapBodies})";
	}

	private Area2D? FindUsableWeaponAttackArea(Node subtreeRoot)
	{
		if (subtreeRoot is Area2D rootArea && rootArea != AttackArea && IsAttackAreaUsable(rootArea))
		{
			return rootArea;
		}

		foreach (Node node in subtreeRoot.FindChildren("*", "Area2D", recursive: true, owned: false))
		{
			if (node is not Area2D area || area == AttackArea)
			{
				continue;
			}

			if (!string.Equals(area.Name.ToString(), "AttackArea", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (IsAttackAreaUsable(area))
			{
				return area;
			}
		}

		return null;
	}

	private int ApplyDamageWithSpecificArea(Area2D attackArea, float damageAmount, Action<GameActor, bool>? onHit)
	{
		CacheAttackCollisionShape(attackArea);
		int hitCount = DealDamageFromHitAreas(attackArea, damageAmount, onHit);
		if (hitCount == 0)
		{
			hitCount = DealDamageViaShapeQuery(attackArea, damageAmount, onHit);
		}
		if (hitCount == 0)
		{
			hitCount = DealDamageFromBodies(attackArea, damageAmount, onHit);
		}

		if (hitCount == 0)
		{
			// LogNoHitDiagnostics(attackArea);
		}

		DealDamageToDestructiblesViaShape(attackArea, damageAmount);

		return hitCount;
	}

	private static bool IsAttackAreaUsable(Area2D? area)
	{
		if (area == null || !GodotObject.IsInstanceValid(area) || !area.IsInsideTree())
		{
			return false;
		}

		if (!area.Monitoring)
		{
			return false;
		}

		var shape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shape != null)
		{
			return !shape.Disabled && shape.Shape != null;
		}

		foreach (Node child in area.GetChildren())
		{
			if (child is CollisionShape2D collisionShape && !collisionShape.Disabled && collisionShape.Shape != null)
			{
				return true;
			}
		}

		return false;
	}

	private void CacheAttackCollisionShape(Area2D attackArea)
	{
		if (_cachedAttackAreaOwner == attackArea && _attackCollisionShape != null)
		{
			return;
		}

		_cachedAttackAreaOwner = attackArea;
		_attackCollisionShape = null;

		_attackCollisionShape = attackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (_attackCollisionShape != null)
		{
			return;
		}

		foreach (Node child in attackArea.GetChildren())
		{
			if (child is CollisionShape2D shape)
			{
				_attackCollisionShape = shape;
				break;
			}
		}

		if (_attackCollisionShape == null)
		{
			GD.PushWarning($"{Name}: AttackArea has no CollisionShape2D; fallback queries disabled.");
		}
	}

	private int DealDamageFromHitAreas(Area2D attackArea, float damageAmount, Action<GameActor, bool>? onHit)
	{
		var overlappingAreas = attackArea.GetOverlappingAreas();
		int hitCount = 0;
		var uniqueTargets = new HashSet<GameActor>();

		foreach (Node areaNode in overlappingAreas)
		{
			if (areaNode is not Area2D hitArea)
			{
				continue;
			}

			if (!TryResolveActorFromHitArea(hitArea, out GameActor actor))
			{
				continue;
			}

			if (!IsValidAttackTarget(actor))
			{
				continue;
			}

			if (!IsConfirmedActorHit(attackArea, actor, hitArea))
			{
				continue;
			}

			if (!uniqueTargets.Add(actor))
			{
				continue;
			}

			DealDamageToTarget(actor, damageAmount);
			hitCount++;
			onHit?.Invoke(actor, false);
		}

		return hitCount;
	}

	private int DealDamageFromBodies(Area2D attackArea, float damageAmount, Action<GameActor, bool>? onHit)
	{
		var bodies = attackArea.GetOverlappingBodies();
		int hitCount = 0;
		var uniqueTargets = new HashSet<GameActor>();
		foreach (Node body in bodies)
		{
			if (body is GameActor actor &&
				IsValidAttackTarget(actor) &&
				uniqueTargets.Add(actor) &&
				IsConfirmedActorHit(attackArea, actor, null))
			{
				DealDamageToTarget(actor, damageAmount);
				hitCount++;
				onHit?.Invoke(actor, false);
			}
		}
		return hitCount;
	}

	private int DealDamageViaShapeQuery(Area2D attackArea, float damageAmount, Action<GameActor, bool>? onHit)
	{
		if (_attackCollisionShape == null || _attackCollisionShape.Shape == null)
		{
			return 0;
		}

		var world = GetWorld2D();
		if (world == null)
		{
			return 0;
		}

		var spaceState = world.DirectSpaceState;
		if (spaceState == null)
		{
			return 0;
		}

		var query = new PhysicsShapeQueryParameters2D
		{
			Shape = _attackCollisionShape.Shape,
			Transform = _attackCollisionShape.GlobalTransform,
			CollisionMask = attackArea.CollisionMask == 0 ? uint.MaxValue : attackArea.CollisionMask,
			CollideWithAreas = true,
			CollideWithBodies = true
		};

		_attackQueryExclude.Clear();
		_attackQueryExclude.Add(GetRid());
		query.Exclude = _attackQueryExclude;

		var results = spaceState.IntersectShape(query, 16);
		int hitCount = 0;
		var uniqueTargets = new HashSet<GameActor>();
		foreach (Godot.Collections.Dictionary hit in results)
		{
			if (!hit.TryGetValue("collider", out Variant colliderVariant))
			{
				continue;
			}

			if (colliderVariant.VariantType != Variant.Type.Object)
			{
				continue;
			}

			var colliderObject = colliderVariant.As<GodotObject>();
			if (TryResolveActorFromCollider(colliderObject, out GameActor actor, out Area2D? hitArea) &&
				IsValidAttackTarget(actor) &&
				IsConfirmedActorHit(attackArea, actor, hitArea))
			{
				if (!uniqueTargets.Add(actor))
				{
					continue;
				}

				DealDamageToTarget(actor, damageAmount);
				hitCount++;
				onHit?.Invoke(actor, true);
			}
		}

		return hitCount;
	}

	private static bool TryResolveActorFromHitArea(Area2D hitArea, out GameActor actor)
	{
		Node? current = hitArea;
		while (current != null)
		{
			if (current is GameActor gameActor)
			{
				actor = gameActor;
				return true;
			}

			current = current.GetParent();
		}

		actor = null!;
		return false;
	}
	
	private static bool TryResolveActorFromCollider(GodotObject colliderObject, out GameActor actor, out Area2D? hitArea)
	{
		if (colliderObject is Area2D area)
		{
			hitArea = area;
			return TryResolveActorFromHitArea(area, out actor);
		}

		hitArea = null;

		if (colliderObject is GameActor gameActor)
		{
			actor = gameActor;
			return true;
		}

		if (colliderObject is Node node)
		{
			Node? current = node;
			while (current != null)
			{
				if (current is GameActor parentActor)
				{
					actor = parentActor;
					return true;
				}

				current = current.GetParent();
			}
		}

		actor = null!;
		return false;
	}

	private static bool IsConfirmedActorHit(Area2D attackArea, GameActor actor, Area2D? overlappedArea)
	{
		if (overlappedArea != null && string.Equals(overlappedArea.Name.ToString(), "HitArea", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (actor.IsHitByArea(attackArea))
		{
			return true;
		}

		return attackArea.OverlapsBody(actor);
	}

	private static Vector2 GetGlobalScaleFromTransform(Transform2D transform)
	{
		return new Vector2(transform.X.Length(), transform.Y.Length());
	}

	private Vector2 ComputeForwardAnchoredAttackShapePosition(Shape2D shape, bool horizontalCapsule)
	{
		float defaultRearEdge = _defaultAttackShapePosition.X - GetShapeHalfWidth(_defaultAttackShape);
		float newHalfWidth = GetShapeHalfWidth(shape, horizontalCapsule);
		return new Vector2(defaultRearEdge + newHalfWidth, _defaultAttackShapePosition.Y);
	}

	private static float GetShapeHalfWidth(Shape2D? shape, bool horizontalCapsule = false)
	{
		if (shape is RectangleShape2D rect)
		{
			return rect.Size.X * 0.5f;
		}

		if (shape is CircleShape2D circle)
		{
			return circle.Radius;
		}

		if (shape is CapsuleShape2D capsule)
		{
			// 横向胶囊（旋转 ±90°）：前向长度 = Height；垂直胶囊：前向宽度 = Radius
			return horizontalCapsule ? capsule.Height * 0.5f : capsule.Radius;
		}

		return 0f;
	}

	/// <summary>武器胶囊是否横向摆放（collisionShape 旋转 ±90°）：CapsuleShape2D 本身垂直，横向需节点旋转表达。</summary>
	private static bool IsCapsuleHorizontal(Transform2D transform)
	{
		float rotation = transform.Rotation;
		return Mathf.Abs(Mathf.Abs(rotation) - Mathf.Pi * 0.5f) < 0.1f;
	}

	private static Shape2D DuplicateShapeWithBakedScale(Shape2D originalShape, Vector2 scale)
	{
		Shape2D? duplicated = originalShape.Duplicate() as Shape2D;
		if (duplicated == null)
		{
			return originalShape;
		}

		scale = new Vector2(Mathf.Abs(scale.X), Mathf.Abs(scale.Y));

		if (duplicated is RectangleShape2D rect)
		{
			rect.Size = new Vector2(rect.Size.X * scale.X, rect.Size.Y * scale.Y);
			return rect;
		}

		if (duplicated is CircleShape2D circle)
		{
			float uniform = Mathf.Max(scale.X, scale.Y);
			circle.Radius *= uniform;
			return circle;
		}

		if (duplicated is CapsuleShape2D capsule)
		{
			capsule.Radius *= scale.X;
			capsule.Height *= scale.Y;
			return capsule;
		}

		return duplicated;
	}

	// 调试诊断方法（当前已注释调用，保留待排查命中问题时使用）
	// private void LogNoHitDiagnostics(Area2D attackArea)
	// {
	// 	var overlapAreas = attackArea.GetOverlappingAreas();
	// 	foreach (Node node in overlapAreas)
	// 	{
	// 		if (node is not Area2D area)
	// 		{
	// 			continue;
	// 		}

	// 		bool actorResolved = TryResolveActorFromHitArea(area, out GameActor resolvedActor);
	// 		string actorName = actorResolved ? resolvedActor.Name : "None";
	// 		bool isEnemy = actorResolved && IsValidAttackTarget(resolvedActor);
	// 		bool areaHit = actorResolved && resolvedActor.IsHitByArea(attackArea);
	// 		GameLogger.Info(nameof(SamplePlayer), $"NoHit Diagnose Area: {area.GetPath()}, actor={actorName}, validEnemy={isEnemy}, actorHitCheck={areaHit}");
	// 	}

	// 	var overlapBodies = attackArea.GetOverlappingBodies();
	// 	foreach (Node body in overlapBodies)
	// 	{
	// 		string name = body.Name;
	// 		bool isActor = body is GameActor;
	// 		bool isEnemy = isActor && IsValidAttackTarget((GameActor)body);
	// 		GameLogger.Info(nameof(SamplePlayer), $"NoHit Diagnose Body: {name}, isGameActor={isActor}, validEnemy={isEnemy}");
	// 	}
	// }

	/// <summary>近战"合法目标"：按**攻击配置的阵营列表**（<see cref="CurrentAttackTargetableFactions"/>，
	/// 默认 Enemy|WorldItem）走 dispatcher 同一套判据（含 damage_receivable 豁免）——不再写死 enemies 组：
	/// GameActor 化的世界物（如 DestructibleStageController 的 gate，world_items 组）否则近战永远打不到。</summary>
	protected virtual bool IsValidAttackTarget(GameActor candidate)
	{
		if (candidate == this) return false;
		return DamageDispatcher.ResolveDamageReceiver(candidate, CurrentAttackTargetableFactions) != null;
	}

	private void DealDamageToTarget(GameActor target, float damageAmount)
	{
		int finalDamage = Mathf.Max(0, Mathf.RoundToInt(damageAmount));
		if (finalDamage <= 0)
		{
			return;
		}

		var stack = InventoryComponent?.GetSelectedQuickBarStack();
		bool isThrowable = stack?.Item.IsThrowable == true && stack?.IsThrowOnCooldown != true;
		var source = isThrowable
			? Kuros.Core.Events.DamageSource.ThrowableDirectAttack
			: Kuros.Core.Events.DamageSource.DirectAttack;
	target.TakeDamage(finalDamage, GlobalPosition, this, source);
	}

	private void DealDamageToDestructiblesViaShape(Area2D attackArea, float damageAmount)
	{
		var shapeNode = attackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shapeNode?.Shape == null) return;

		var spaceState = attackArea.GetWorld2D()?.DirectSpaceState;
		if (spaceState == null) return;

		var query = new PhysicsShapeQueryParameters2D
		{
			Shape = shapeNode.Shape,
			Transform = shapeNode.GlobalTransform,
			CollisionMask = attackArea.CollisionMask == 0 ? uint.MaxValue : attackArea.CollisionMask,
			CollideWithAreas = true,
			CollideWithBodies = true
		};
		var results = spaceState.IntersectShape(query, 32);

		var attacker = attackArea.GetParent() as GameActor;

		var damaged = new System.Collections.Generic.HashSet<ulong>();
		foreach (var result in results)
		{
			var collider = result["collider"].As<Node>();
			if (collider == null) continue;

			var factions = CurrentAttackTargetableFactions;
			var root = DamageDispatcher.ResolveDamageReceiver(collider, factions);
			if (root == null || root is GameActor || !damaged.Add(root.GetInstanceId())) continue;

			DamageDispatcher.DealDamage(collider, damageAmount,
				attackArea.GlobalPosition, attacker, DamageSource.DirectAttack, factions);
		}
	}

	public override bool IsHitByArea(Area2D? attackerArea)
	{
		if (attackerArea == null)
		{
			return false;
		}

		if (HitArea != null && HitArea.IsInsideTree())
		{
			return attackerArea.OverlapsArea(HitArea);
		}

		return attackerArea.OverlapsBody(this);
	}
	
	public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null, Kuros.Core.Events.DamageSource damageSource = Kuros.Core.Events.DamageSource.DirectAttack, bool bypassMergeWindow = false)
	{
		_pendingAttackSourceState = string.Empty;
		bool dealt = base.TakeDamage(damage, attackOrigin, attacker, damageSource, bypassMergeWindow);
		UpdateStatsUI();
		return dealt;
	}
	
	public void AddScore(int points)
	{
		_score += points;
		UpdateStatsUI();
	}
	
	/// <summary>
	/// 获取当前金币数量
	/// </summary>
	public int GetGold()
	{
		return _gold;
	}
	
	/// <summary>
	/// 添加金币到玩家的金币总量。
	/// </summary>
	/// <param name="amount">要添加的金币数量，必须为非负数。</param>
	/// <exception cref="ArgumentOutOfRangeException">当 <paramref name="amount"/> 为负数时抛出。</exception>
	/// <remarks>
	/// 若需要扣除金币，请使用 <see cref="TrySpendGold"/> 方法，
	/// 该方法会检查金币是否足够并安全地扣除。
	/// </remarks>
	public void AddGold(int amount)
	{
		if (amount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(amount), amount,
				"金币数量不能为负数。若需扣除金币，请使用 TrySpendGold 方法。");
		}
		
		_gold += amount;
		EmitSignal(SignalName.GoldChanged, _gold);
	}
	
	/// <summary>
	/// 设置金币数量
	/// </summary>
	public void SetGold(int amount)
	{
		_gold = Mathf.Max(0, amount);
		EmitSignal(SignalName.GoldChanged, _gold);
	}
	
	/// <summary>
	/// 尝试消费金币（如果金币足够）
	/// </summary>
	/// <param name="amount">要消费的金币数量，必须为非负数。</param>
	/// <exception cref="ArgumentOutOfRangeException">当 <paramref name="amount"/> 为负数时抛出。</exception>
	/// <remarks>
	/// 若需要添加金币，请使用 <see cref="AddGold"/> 方法。
	/// </remarks>
	public bool TrySpendGold(int amount)
	{
		if (amount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(amount), amount,
				"金币数量不能为负数。若需添加金币，请使用 AddGold 方法。");
		}
		
		if (_gold >= amount)
		{
			_gold -= amount;
			EmitSignal(SignalName.GoldChanged, _gold);
			return true;
		}
		return false;
	}
	
	private void UpdateDebugOverlayText()
	{
		string stateName = StateMachine?.CurrentState?.Name ?? "None";
		_debugOverlayText = $"{Name} | State: {stateName} | HP: {CurrentHealth}/{MaxHealth} | Vel: {Velocity.Length():F0} | Speed: {CurrentMoveSpeed:F0}";
	}

	protected void UpdateStatsUI()
	{
		NotifyStatsListeners();

		if (StatsLabel != null)
		{
			StatsLabel.Text = $"Player HP: {CurrentHealth}\nScore: {_score}";
		}
	}

	private void NotifyStatsListeners()
	{
		StatsUpdated?.Invoke(CurrentHealth, MaxHealth, _score);
	}
	
	protected override void OnDeathFinalized()
	{
		EffectController?.ClearAll();
		GameLogger.Warn(nameof(SamplePlayer), "Player died! Game Over!");
		GetTree().ReloadCurrentScene();
	}
}
