using Godot;
using System;
using Kuros.Core;
using Kuros.Systems.FSM;
using Kuros.Actors.Heroes.States;
using Kuros.Actors.Heroes;
using Kuros.Systems.Inventory;
using Kuros.Items;
using Kuros.Managers;
using Kuros.UI;
using Kuros.Utils;

public partial class SamplePlayer : GameActor, IPlayerStatsSource
{
	[ExportCategory("Combat")]
	[Export] public Area2D AttackArea { get; private set; } = null!;
	public PlayerFrozenState? FrozenState { get; private set; }
	public PlayerInventoryComponent? InventoryComponent { get; private set; }
	public InventoryContainer? Backpack => InventoryComponent?.Backpack;
	
	[ExportCategory("UI")]
	[Export] public Label StatsLabel { get; private set; } = null!; // Drag & Drop in Editor
	
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
	/// 当前左手物品对应的快捷栏槽位索引（1-4，对应数字键2-5）
	/// -1 表示未装备任何物品
	/// </summary>
	public int LeftHandSlotIndex { get; private set; } = -1;
	
	private int _score = 0;
	private int _gold = 0; // 金币数量
	private string _pendingAttackSourceState = string.Empty;
	public string LastMovementStateName { get; private set; } = "Idle";
	
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

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("player");
		
		// Fallback: Try to find nodes if not assigned in editor (Backward compatibility)
		if (AttackArea == null) AttackArea = GetNodeOrNull<Area2D>("AttackArea");
		if (FrozenState == null) FrozenState = StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
		if (StatsLabel == null) StatsLabel = GetNodeOrNull<Label>("../UI/PlayerStats");
		if (InventoryComponent == null) InventoryComponent = GetNodeOrNull<PlayerInventoryComponent>("Inventory");
		
		// 连接快捷栏变化信号，确保左手物品与选中槽位严格对应
		ConnectQuickBarSignals();
		
		// 设置左手默认选中快捷栏2（索引1）
		// 使用 CallDeferred 确保在快捷栏初始化完成后再设置
		CallDeferred(MethodName.InitializeLeftHandSelection);
		
		UpdateStatsUI();
	}
	
	public override void _UnhandledInput(InputEvent @event)
	{
		// 处理数字键 2、3、4、5 切换快捷栏物品（对应快捷栏槽位 1、2、3、4）
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			int? slotIndex = null;
			
			// 数字键 2-5 对应快捷栏槽位 1-4（索引从0开始，但槽位0是小木剑）
			if (keyEvent.Keycode == Key.Key2)
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
				SwitchToQuickBarSlot(slotIndex.Value);
				GetViewport().SetInputAsHandled();
			}
		}
		
		base._UnhandledInput(@event);
	}
	
	/// <summary>
	/// 切换到指定快捷栏槽位的物品
	/// 严格绑定：LeftHandSlotIndex 和 LeftHandItem 必须严格对应
	/// </summary>
	/// <param name="slotIndex">快捷栏槽位索引（1-4，对应数字键2-5）</param>
	private void SwitchToQuickBarSlot(int slotIndex)
	{
		// 验证槽位索引范围（1-4，跳过索引0的小木剑）
		if (slotIndex < 1 || slotIndex > 4)
		{
			GD.PrintErr($"SwitchToQuickBarSlot: Invalid slot index {slotIndex}, must be 1-4");
			return;
		}
		
		// 如果 QuickBar 还未初始化，先记录选中的槽位索引，稍后在 QuickBar 设置后会同步
		if (InventoryComponent?.QuickBar == null)
		{
			// 仅记录槽位索引，等待 QuickBar 初始化后再同步物品
			LeftHandSlotIndex = slotIndex;
			GD.Print($"SwitchToQuickBarSlot: QuickBar not yet available, recorded slot index {slotIndex} for later sync");
			return;
		}
		
		GD.Print($"SwitchToQuickBarSlot: Switching from slot {LeftHandSlotIndex} to slot {slotIndex}");
		
		// 严格绑定：设置 LeftHandSlotIndex，然后同步 LeftHandItem
		LeftHandSlotIndex = slotIndex;
		SyncLeftHandItemFromSlot();
		
		GD.Print($"SwitchToQuickBarSlot: LeftHandSlotIndex is now {LeftHandSlotIndex}, LeftHandItem is {(LeftHandItem != null ? LeftHandItem.DisplayName : "null")}");
		
		// 更新视觉反馈：显示/隐藏手上的物品
		UpdateHandItemVisual();
		
		// 通知 BattleHUD 更新边框颜色
		UpdateBattleHUDHandHighlight();
	}
	
	/// <summary>
	/// 同步左手物品：从当前选中的快捷栏槽位获取物品，确保严格对应
	/// </summary>
	public void SyncLeftHandItemFromSlot()
	{
		if (LeftHandSlotIndex < 1 || LeftHandSlotIndex > 4)
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
			GD.Print($"SyncLeftHandItemFromSlot: Left hand item synced to {LeftHandItem.DisplayName} from quickbar slot {LeftHandSlotIndex + 1}");
		}
		else
		{
			// 槽位为空或只有空白道具，清除左手物品
			LeftHandItem = null;
			GD.Print($"SyncLeftHandItemFromSlot: Quickbar slot {LeftHandSlotIndex + 1} is empty or has empty item, cleared left hand item");
		}
	}
	
	/// <summary>
	/// 快捷栏槽位变化时的回调：如果变化的是当前选中的槽位，同步更新左手物品
	/// </summary>
	private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
	{
		GD.Print($"OnQuickBarSlotChanged: Slot {slotIndex} changed (ItemId: {itemId}, Quantity: {quantity}), current LeftHandSlotIndex: {LeftHandSlotIndex}");
		
		// 如果变化的是当前选中的槽位，同步更新左手物品
		if (slotIndex == LeftHandSlotIndex)
		{
			GD.Print($"OnQuickBarSlotChanged: Selected slot {slotIndex} changed, syncing left hand item");
			SyncLeftHandItemFromSlot();
			UpdateHandItemVisual();
		}
	}
	
	/// <summary>
	/// 快捷栏整体变化时的回调：同步更新左手物品
	/// </summary>
	private void OnQuickBarInventoryChanged()
	{
		GD.Print($"OnQuickBarInventoryChanged: QuickBar inventory changed, current LeftHandSlotIndex: {LeftHandSlotIndex}");
		
		// 如果当前有选中的槽位，同步更新左手物品
		if (LeftHandSlotIndex >= 1 && LeftHandSlotIndex <= 4)
		{
			GD.Print("OnQuickBarInventoryChanged: Syncing left hand item from selected slot");
			SyncLeftHandItemFromSlot();
			UpdateHandItemVisual();
		}
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
					GD.Print($"UpdateHandItemVisual: Left hand item visibility set to {node2d.Visible} (Item: {LeftHandItem?.DisplayName ?? "null"})");
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
		
		// 如果已经搜索过但未找到，不再重复搜索（使用标记避免重复日志）
		if (_leftHandAttachmentSearched)
		{
			return null;
		}
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
			battleHUD.CallDeferred(BattleHUD.MethodName.UpdateHandSlotHighlight, LeftHandSlotIndex, 0);
		}
	}
	
	/// <summary>
	/// 初始化左手选择：默认选中快捷栏2（索引1）
	/// 只在还没有选中任何槽位时才初始化，避免覆盖用户的选择
	/// </summary>
	public void InitializeLeftHandSelection()
	{
		// 如果还没有选中任何槽位，默认选中快捷栏2（索引1）
		// 重要：只在 LeftHandSlotIndex 无效时才初始化，避免覆盖用户已选择的其他快捷栏
		if (LeftHandSlotIndex < 1 || LeftHandSlotIndex > 4)
		{
			int previousIndex = LeftHandSlotIndex;
			SwitchToQuickBarSlot(1);
			GD.Print($"SamplePlayer: Initialized left hand selection to quickbar slot 2 (index 1). Previous LeftHandSlotIndex: {previousIndex}");
		}
		else
		{
			GD.Print($"SamplePlayer: Left hand selection already set to slot {LeftHandSlotIndex}, skipping initialization");
			// 即使已经选中，也要确保同步
			SyncLeftHandItemFromSlot();
			UpdateHandItemVisual();
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
			GD.Print("SamplePlayer: Connected QuickBar signals for left hand item synchronization");
			
			// 如果当前有选中的槽位，同步一次左手物品（可能是在 QuickBar 可用之前设置的）
			if (LeftHandSlotIndex >= 1 && LeftHandSlotIndex <= 4)
			{
				GD.Print($"SamplePlayer: Syncing left hand item for previously selected slot {LeftHandSlotIndex}");
				SyncLeftHandItemFromSlot();
				UpdateHandItemVisual();
				UpdateBattleHUDHandHighlight();
			}
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
		
		// If AttackArea is NOT a child of the flipped sprite/spine, we must flip it manually here.
		// This is better than doing it in PerformAttackCheck because physics has time to update.
		if (AttackArea != null)
		{
			 // We assume the AttackArea is centered or offset. If offset, we flip the offset.
			 // Check if AttackArea parent is NOT the flipped visual (to avoid double flipping)
			 if (AttackArea.GetParent() != _spineCharacter && AttackArea.GetParent() != _sprite)
			 {
				 var areaPos = AttackArea.Position;
				 float absX = Mathf.Abs(areaPos.X);
				 AttackArea.Position = new Vector2(faceRight ? absX : -absX, areaPos.Y);
				 
				 // Optional: Flip scale too if the shape is asymmetric
				 var areaScale = AttackArea.Scale;
				 float absScaleX = Mathf.Abs(areaScale.X);
				 AttackArea.Scale = new Vector2(faceRight ? absScaleX : -absScaleX, areaScale.Y);
			 }
		}
	}
	
	public void PerformAttackCheck()
	{
		// Reset timer just in case, though State usually manages cooldown entry
		AttackTimer = AttackCooldown;
		
		GameLogger.Info(nameof(SamplePlayer), "=== Player attacking frame! ===");
		
		int hitCount = 0;
		
		if (AttackArea != null)
		{
			// REMOVED: Manual Position flipping here. It's now handled in FlipFacing or via Scene Hierarchy.
			
			var bodies = AttackArea.GetOverlappingBodies();
			foreach (var body in bodies)
			{
				if (body is SampleEnemy enemy)
				{
					enemy.TakeDamage((int)AttackDamage);
					hitCount++;
					GameLogger.Info(nameof(SamplePlayer), $"Hit enemy: {enemy.Name}");
				}
			}
		}
		else
		{
			GameLogger.Error(nameof(SamplePlayer), "AttackArea is missing! Assign it in Inspector.");
		}
		
		if (hitCount == 0)
		{
			GameLogger.Info(nameof(SamplePlayer), "No enemies hit!");
		}
	}
	
	public override void TakeDamage(int damage)
	{
		_pendingAttackSourceState = string.Empty;
		base.TakeDamage(damage);
		UpdateStatsUI();
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
	
	private void UpdateStatsUI()
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
