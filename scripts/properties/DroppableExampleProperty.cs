using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Actors.Heroes;
using Kuros.UI;
using Kuros.Managers;

public partial class DroppableExampleProperty : DroppablePickupProperty
{
    [Export] public int HealValue = 25;
    [Export] public Color PickedColor = Colors.LimeGreen;
    [ExportGroup("Item")]
    [Export] public ItemDefinition? Item { get; set; } // 可选的物品定义
    [Export(PropertyHint.Range, "1,9999,1")]
    public int ItemQuantity { get; set; } = 1; // 物品数量

    private Color _initialColor = Colors.White;
    private Sprite2D? _sprite;
    private bool _healGranted;
    private GameActor? _healedActor;
    private int _actualHealAmount;

    public override void _Ready()
    {
        base._Ready();

        // 验证Item属性的类型，防止类型转换错误
        if (Item != null && !(Item is ItemDefinition))
        {
            GD.PrintErr($"DroppableExampleProperty._Ready: Item property is not ItemDefinition! Type: {Item.GetType().Name}. Please set Item to an ItemDefinition resource in the editor.");
            Item = null; // 清除错误的引用
        }

        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _initialColor = _sprite.Modulate;
            
            // 确保贴图已加载，如果没有则加载 icon.svg
            if (_sprite.Texture == null)
            {
                var iconTexture = GD.Load<Texture2D>("res://icon.svg");
                if (iconTexture != null)
                {
                    _sprite.Texture = iconTexture;
                    GD.Print("DroppableExampleProperty: Loaded icon.svg texture");
                }
                else
                {
                    GD.PrintErr("DroppableExampleProperty: Failed to load icon.svg");
                }
            }
        }
    }

    protected override void OnPicked(GameActor actor)
    {
        base.OnPicked(actor);

        if (_sprite != null)
        {
            _sprite.Modulate = PickedColor;
        }

        // 應用治療效果
        ApplyHealEffect(actor);

        // 如果设置了物品，添加到玩家物品栏
        if (actor is SamplePlayer player)
        {
            GD.Print($"DroppableExampleProperty.OnPicked: Actor is SamplePlayer, Item is {(Item != null ? Item.DisplayName : "null")}");
            
            if (Item != null)
            {
                GD.Print($"DroppableExampleProperty: Adding {ItemQuantity} x {Item.DisplayName} to inventory");
                
                if (player.InventoryComponent != null)
                {
                    GD.Print($"DroppableExampleProperty: InventoryComponent found, QuickBar is {(player.InventoryComponent.QuickBar != null ? "set" : "null")}");
                    
                    int added = player.InventoryComponent.AddItemSmart(Item, ItemQuantity);
                    if (added > 0)
                    {
                        GD.Print($"DroppableExampleProperty: Successfully added {added} x {Item.DisplayName} to inventory");
                        
                        // 强制刷新快捷栏显示（通过BattleHUD）
                        // 优先通过 UIManager 获取，如果失败则尝试通过场景树查找
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
                            GD.Print("DroppableExampleProperty: Found BattleHUD, requesting quickbar refresh");
                            battleHUD.CallDeferred("UpdateQuickBarDisplay");
                        }
                        else
                        {
                            GD.PrintErr("DroppableExampleProperty: Could not find BattleHUD to refresh quickbar");
                        }
                    }
                    else
                    {
                        GD.PrintErr($"DroppableExampleProperty: Failed to add {Item.DisplayName} to inventory - added: {added}");
                    }
                }
                else
                {
                    GD.PrintErr($"DroppableExampleProperty: Player {player.Name} has no InventoryComponent");
                }
            }
            else
            {
                GD.Print($"DroppableExampleProperty: Item is null, skipping inventory addition (this is normal if item is not set)");
                GD.Print($"DroppableExampleProperty: 提示：请在编辑器中为 {Name} 节点设置 Item 属性");
            }
        }
        else
        {
            GD.Print($"DroppableExampleProperty: Actor is not SamplePlayer: {actor?.GetType().Name}");
        }
    }

    protected override void OnPutDown(GameActor actor)
    {
        base.OnPutDown(actor);

        ResetSpriteColor();
        ClearHealEffect(actor);

        // 从玩家物品栏中移除当前选中槽位的物品
        // 关键修复：应该从当前选中的快捷栏槽位获取物品，而不是使用Item属性
        if (actor is SamplePlayer player)
        {
            HandleItemRemoval(player);
            RefreshBattleHUD(player);
        }
    }

    /// <summary>
    /// 重置精灵颜色到初始状态
    /// </summary>
    private void ResetSpriteColor()
    {
        if (_sprite != null)
        {
            _sprite.Modulate = _initialColor;
        }
    }

    /// <summary>
    /// 清除治療效果並記錄日誌
    /// </summary>
    private void ClearHealEffect(GameActor actor)
    {
        if (_healGranted)
        {
            GD.Print($"{Name} put down by {actor.Name}. Heal effect of {_actualHealAmount} HP was previously granted.");
            // 注意：治療效果通常不會在放下物品時撤銷
            // 如果需要撤銷效果，可以在這裡添加邏輯
            _healGranted = false;
            _healedActor = null;
            _actualHealAmount = 0;
        }
    }

    /// <summary>
    /// 應用治療效果到角色
    /// </summary>
    private void ApplyHealEffect(GameActor actor)
    {
        if (actor == null)
        {
            GD.PrintErr($"{Name}: Cannot apply heal effect - actor is null");
            return;
        }

        if (_healGranted)
        {
            GD.Print($"{Name}: Heal effect already granted, skipping");
            return;
        }

        // 計算實際治療量（不超過最大生命值）
        int healthBefore = actor.CurrentHealth;
        int maxHeal = actor.MaxHealth - actor.CurrentHealth;
        _actualHealAmount = Mathf.Min(HealValue, maxHeal);

        if (_actualHealAmount > 0)
        {
            // 使用 RestoreHealth 方法來恢復生命值
            int newHealth = Mathf.Min(actor.CurrentHealth + _actualHealAmount, actor.MaxHealth);
            actor.RestoreHealth(newHealth);
            
            _healGranted = true;
            _healedActor = actor;
            
            GD.Print($"{Name} healed {actor.Name} for {_actualHealAmount} HP ({healthBefore} -> {actor.CurrentHealth}/{actor.MaxHealth})");
        }
        else
        {
            GD.Print($"{Name}: {actor.Name} is at full health ({actor.CurrentHealth}/{actor.MaxHealth}), no healing applied");
            _healGranted = true; // 標記為已處理，即使沒有實際治療
            _healedActor = actor;
        }
    }

    /// <summary>
    /// 处理物品移除逻辑：从快捷栏和背包中移除物品
    /// </summary>
    private void HandleItemRemoval(SamplePlayer player)
    {
        int removed = 0;
        ItemDefinition? itemToRemove = null;
        int quantityToRemove = 1;
        
        // 记录所有快捷栏槽位的状态，用于调试
        if (player.InventoryComponent?.QuickBar != null)
        {
            GD.Print($"DroppableExampleProperty.OnPutDown: QuickBar status before removal:");
            for (int i = 0; i < 5; i++)
            {
                var debugStack = player.InventoryComponent.QuickBar.GetStack(i);
                GD.Print($"  Slot {i}: {(debugStack != null && !debugStack.IsEmpty ? $"{debugStack.Item.DisplayName} x{debugStack.Quantity}" : "empty")}");
            }
        }
        
        // 关键修复：从当前选中槽位获取物品，而不是使用Item属性
        if (player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex < 5 && player.InventoryComponent?.QuickBar != null)
        {
            var selectedStack = player.InventoryComponent.QuickBar.GetStack(player.LeftHandSlotIndex);
            if (selectedStack != null && !selectedStack.IsEmpty && selectedStack.Item.ItemId != "empty_item")
            {
                itemToRemove = selectedStack.Item;
                quantityToRemove = selectedStack.Quantity;
                GD.Print($"DroppableExampleProperty.OnPutDown: Selected slot {player.LeftHandSlotIndex} has {quantityToRemove} x {itemToRemove.DisplayName} (ItemId: {itemToRemove.ItemId})");
            }
        }
        
        // 如果没有选中槽位或选中槽位为空，直接返回
        if (itemToRemove == null)
        {
            GD.Print($"DroppableExampleProperty.OnPutDown: No valid item to remove from selected slot {player.LeftHandSlotIndex}");
            
            // 如果选中的是空白道具，创建不透明度为0的实例
            if (player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex < 5 && player.InventoryComponent?.QuickBar != null)
            {
                var stack = player.InventoryComponent.QuickBar.GetStack(player.LeftHandSlotIndex);
                if (stack != null && !stack.IsEmpty && stack.Item.ItemId == "empty_item")
                {
                    CreateEmptyItemInstance(player);
                }
            }
            return;
        }
        
        // 检查物品栏是否有对应物品（排除空白道具）
        bool backpackEmpty = true;
        if (player.InventoryComponent?.Backpack != null)
        {
            for (int i = 0; i < player.InventoryComponent.Backpack.Slots.Count; i++)
            {
                var backpackStack = player.InventoryComponent.Backpack.GetStack(i);
                if (backpackStack != null && !backpackStack.IsEmpty && 
                    backpackStack.Item.ItemId != "empty_item" && 
                    backpackStack.Item.ItemId == itemToRemove.ItemId)
                {
                    backpackEmpty = false;
                    break;
                }
            }
        }
        
        // 从选中槽位移除物品
        if (player.InventoryComponent?.QuickBar != null)
        {
            removed = player.InventoryComponent.QuickBar.RemoveItemFromSlot(player.LeftHandSlotIndex, quantityToRemove);
            GD.Print($"DroppableExampleProperty.OnPutDown: Removed {removed} x {itemToRemove.DisplayName} from selected quickbar slot {player.LeftHandSlotIndex}");
            
            // 立即同步左手物品（在添加空白道具之前，确保左手物品先清除）
            // 这样可以避免信号处理顺序问题
            GD.Print($"DroppableExampleProperty.OnPutDown: Syncing left hand item after removal");
            player.SyncLeftHandItemFromSlot();
            player.UpdateHandItemVisual();
            
            // 如果选中的槽位物品被完全移除，添加空白道具
            var updatedStack = player.InventoryComponent.QuickBar.GetStack(player.LeftHandSlotIndex);
            if (updatedStack == null || updatedStack.IsEmpty)
            {
                // 添加空白道具（这会触发SlotChanged信号，但我们已经同步过了）
                var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
                if (emptyItem != null && player.InventoryComponent?.QuickBar != null)
                {
                    player.InventoryComponent.QuickBar.TryAddItemToSlot(emptyItem, 1, player.LeftHandSlotIndex);
                    GD.Print($"DroppableExampleProperty.OnPutDown: Added empty item to slot {player.LeftHandSlotIndex}");
                    // 添加空白道具后再次同步，确保状态正确
                    // 虽然SlotChanged信号也会触发同步，但这里确保立即执行
                    player.SyncLeftHandItemFromSlot();
                    player.UpdateHandItemVisual();
                }
            }
        }
        
        // 如果从选中槽位没有移除足够的物品，尝试从物品栏移除
        if (removed < quantityToRemove && !backpackEmpty && player.InventoryComponent?.Backpack != null)
        {
            int remaining = quantityToRemove - removed;
            // 找到物品栏中对应物品的槽位并移除
            for (int i = 0; i < player.InventoryComponent.Backpack.Slots.Count && remaining > 0; i++)
            {
                var backpackStack = player.InventoryComponent.Backpack.GetStack(i);
                // 确保不是空白道具
                if (backpackStack != null && !backpackStack.IsEmpty && 
                    backpackStack.Item.ItemId != "empty_item" && 
                    backpackStack.Item.ItemId == itemToRemove.ItemId)
                {
                    int removedFromSlot = player.InventoryComponent.Backpack.RemoveItemFromSlot(i, remaining);
                    removed += removedFromSlot;
                    remaining -= removedFromSlot;
                    GD.Print($"DroppableExampleProperty.OnPutDown: Removed {removedFromSlot} x {itemToRemove.DisplayName} from backpack slot {i}");
                    
                    // 如果槽位被清空，添加空白道具
                    var updatedBackpackStack = player.InventoryComponent.Backpack.GetStack(i);
                    if (updatedBackpackStack == null || updatedBackpackStack.IsEmpty)
                    {
                        var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
                        if (emptyItem != null)
                        {
                            player.InventoryComponent.Backpack.TryAddItemToSlot(emptyItem, 1, i);
                            GD.Print($"DroppableExampleProperty.OnPutDown: Added empty item to backpack slot {i}");
                        }
                    }
                }
            }
        }
        
        // 注意：不再从其他快捷栏槽位移除，只从选中槽位和物品栏移除
        // 这样可以确保g键只放下当前选中的物品
        
        // 记录移除后的快捷栏状态，用于调试
        if (removed > 0 && player.InventoryComponent?.QuickBar != null)
        {
            GD.Print($"DroppableExampleProperty.OnPutDown: QuickBar status after removal:");
            for (int i = 0; i < 5; i++)
            {
                var debugStack = player.InventoryComponent.QuickBar.GetStack(i);
                GD.Print($"  Slot {i}: {(debugStack != null && !debugStack.IsEmpty ? $"{debugStack.Item.DisplayName} x{debugStack.Quantity}" : "empty")}");
            }
        }
        
        if (removed > 0 && itemToRemove != null)
        {
            GD.Print($"DroppableExampleProperty.OnPutDown: Successfully removed {removed} x {itemToRemove.DisplayName} from inventory");
        }
        else if (itemToRemove != null)
        {
            // 如果没有移除任何物品，打印错误（这种情况不应该发生，因为上面已经检查过了）
            GD.PrintErr($"DroppableExampleProperty.OnPutDown: ERROR - No items removed (Item: {itemToRemove.DisplayName}, Quantity: {quantityToRemove}). This should not happen.");
        }
    }

    /// <summary>
    /// 刷新 BattleHUD 显示：更新快捷栏和手部槽位高亮
    /// </summary>
    private void RefreshBattleHUD(SamplePlayer player)
    {
        // 无论是否移除物品，都要刷新快捷栏显示（因为添加空白道具也会改变显示）
        // 这样可以确保所有槽位的显示都是最新的
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
            GD.Print("DroppableExampleProperty.OnPutDown: Found BattleHUD, requesting quickbar refresh");
            // 更新所有快捷栏槽位的显示
            battleHUD.CallDeferred("UpdateQuickBarDisplay");
            // 保持当前的左手选择状态（如果还有的话）
            int leftHandSlot = player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex < 5 ? player.LeftHandSlotIndex : -1;
            battleHUD.CallDeferred("UpdateHandSlotHighlight", leftHandSlot, 0);
        }
        else
        {
            GD.PrintErr("DroppableExampleProperty.OnPutDown: Could not find BattleHUD to refresh quickbar");
        }
    }

    /// <summary>
    /// 创建空白道具实例：不透明度为0，无法被拾取
    /// </summary>
    private void CreateEmptyItemInstance(SamplePlayer player)
    {
        // 加载 DroppableExampleProperty 场景
        var itemScene = GD.Load<PackedScene>("res://scenes/properties/DroppableExampleProperty.tscn");
        if (itemScene == null)
        {
            GD.PrintErr("DroppableExampleProperty.CreateEmptyItemInstance: Failed to load DroppableExampleProperty scene");
            return;
        }

        // 创建实例
        var itemInstance = itemScene.Instantiate<DroppableExampleProperty>();
        if (itemInstance == null)
        {
            GD.PrintErr("DroppableExampleProperty.CreateEmptyItemInstance: Failed to instantiate DroppableExampleProperty");
            return;
        }

        // 设置物品为 EmptyItem
        var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
        if (emptyItem != null)
        {
            itemInstance.Item = emptyItem;
            itemInstance.ItemQuantity = 1;
        }

        // 设置不透明度为0（完全透明）
        var sprite = itemInstance.GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite != null)
        {
            sprite.Modulate = new Color(1, 1, 1, 0); // alpha = 0 表示完全透明
        }

        // 禁用触发区域，使其无法被拾取
        var triggerArea = itemInstance.GetNodeOrNull<Area2D>("TriggerArea");
        if (triggerArea != null)
        {
            triggerArea.Monitoring = false;
            triggerArea.Monitorable = false;
            triggerArea.CollisionLayer = 0;
            triggerArea.CollisionMask = 0;
        }

        // 设置位置并添加到场景
        var dropParent = GetDropParent(player);
        dropParent.AddChild(itemInstance);
        itemInstance.GlobalPosition = player.GlobalPosition + DropWorldOffset;

        GD.Print("DroppableExampleProperty.CreateEmptyItemInstance: Created invisible empty item instance at " + itemInstance.GlobalPosition);
    }
}


