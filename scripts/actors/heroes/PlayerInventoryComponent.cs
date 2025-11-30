using Godot;
using Kuros.Items;
using Kuros.Systems.Inventory;
using System.Collections.Generic;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家背包组件，封装背包容器并提供基础接口。
    /// </summary>
    public partial class PlayerInventoryComponent : Node
    {
        [Export(PropertyHint.Range, "1,200,1")]
        public int BackpackSlots { get; set; } = 24;

        public InventoryContainer Backpack { get; private set; } = null!;
        public InventoryContainer? QuickBar { get; set; }

        // 记录下一个要填充的快捷栏槽位索引（从1开始，因为0是默认小木剑）
        private int _nextQuickBarSlot = 1;
        
        // 空白道具资源缓存
        private ItemDefinition? _emptyItem;

        // 跟踪已获得的物品ID（用于判断是否是第一次获得）
        private HashSet<string> _obtainedItemIds = new HashSet<string>();

        public override void _Ready()
        {
            base._Ready();

            Backpack = GetNodeOrNull<InventoryContainer>("Backpack") ?? CreateBackpack();
            Backpack.SlotCount = BackpackSlots;
            
            // 加载空白道具资源
            _emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
            if (_emptyItem == null)
            {
                GD.PrintErr("PlayerInventoryComponent: Failed to load EmptyItem.tres");
            }
        }
        
        /// <summary>
        /// 获取空白道具实例
        /// </summary>
        private ItemDefinition? GetEmptyItem()
        {
            if (_emptyItem == null)
            {
                _emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
            }
            return _emptyItem;
        }
        
        /// <summary>
        /// 在指定槽位添加空白道具（如果槽位为空）
        /// </summary>
        private void AddEmptyItemToSlot(InventoryContainer container, int slotIndex)
        {
            if (container == null || slotIndex < 0 || slotIndex >= container.SlotCount) return;
            
            var emptyItem = GetEmptyItem();
            if (emptyItem == null) return;
            
            var stack = container.GetStack(slotIndex);
            if (stack == null || stack.IsEmpty)
            {
                container.TryAddItemToSlot(emptyItem, 1, slotIndex);
                GD.Print($"PlayerInventoryComponent: Added empty item to slot {slotIndex}");
            }
        }

        private InventoryContainer CreateBackpack()
        {
            var container = new InventoryContainer
            {
                Name = "Backpack",
                SlotCount = BackpackSlots
            };
            AddChild(container);
            return container;
        }

        /// <summary>
        /// 设置快捷栏容器引用
        /// </summary>
        public void SetQuickBar(InventoryContainer quickBar)
        {
            QuickBar = quickBar;
            GD.Print($"PlayerInventoryComponent: QuickBar has been set. QuickBar is {(quickBar != null ? "valid" : "null")}");
        }

        /// <summary>
        /// 检查是否是第一次获得该物品
        /// </summary>
        public bool IsFirstTimeObtaining(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.ItemId))
            {
                return false;
            }
            return !_obtainedItemIds.Contains(item.ItemId);
        }

        /// <summary>
        /// 标记物品为已获得
        /// </summary>
        private void MarkItemAsObtained(ItemDefinition item)
        {
            if (item != null && !string.IsNullOrEmpty(item.ItemId))
            {
                _obtainedItemIds.Add(item.ItemId);
            }
        }

        /// <summary>
        /// 智能添加物品：优先放入快捷栏2345的第一个空槽位或可合并的槽位，剩余放入物品栏
        /// 注意：快捷栏1（索引0）被小木剑占位，不会被填充
        /// </summary>
        public int AddItemSmart(ItemDefinition item, int amount, bool showPopupIfFirstTime = true)
        {
            int remaining = amount;
            bool isFirstTime = IsFirstTimeObtaining(item);

            // 优先放入快捷栏2345（索引1-4，因为索引0是默认小木剑，需要跳过）
            if (QuickBar != null && remaining > 0)
            {
                GD.Print($"AddItemSmart: Attempting to add {amount} x {item.DisplayName} to quickbar");
                
                // 首先尝试合并到已有相同物品的槽位
                for (int i = 1; i < 5 && remaining > 0; i++)
                {
                    var existingStack = QuickBar.GetStack(i);
                    if (existingStack != null && !existingStack.IsEmpty && 
                        existingStack.Item.ItemId == item.ItemId && !existingStack.IsFull)
                    {
                        int added = QuickBar.TryAddItemToSlot(item, remaining, i);
                        if (added > 0)
                        {
                            GD.Print($"AddItemSmart: Merged {added} x {item.DisplayName} into existing stack at slot {i}");
                            remaining -= added;
                        }
                    }
                }
                
                // 如果还有剩余，找到第一个空槽位或空白道具槽位添加
                if (remaining > 0)
                {
                    for (int i = 1; i < 5 && remaining > 0; i++)
                    {
                        var existingStack = QuickBar.GetStack(i);
                        // 检查槽位是否为空或包含空白道具
                        if (existingStack == null || existingStack.IsEmpty || 
                            (existingStack.Item.ItemId == "empty_item"))
                        {
                            int added = QuickBar.TryAddItemToSlot(item, remaining, i);
                            if (added > 0)
                            {
                                GD.Print($"AddItemSmart: Added {added} x {item.DisplayName} to slot {i} (replaced empty item if present)");
                                remaining -= added;
                                // 更新下一个要填充的槽位
                                _nextQuickBarSlot = ((i - 1) % 4) + 1;
                                if (_nextQuickBarSlot == 0) _nextQuickBarSlot = 1;
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                GD.PrintErr($"AddItemSmart: QuickBar is null! Item will only be added to backpack.");
            }

            // 剩余物品放入物品栏（会自动替换空白道具）
            if (remaining > 0)
            {
                GD.Print($"AddItemSmart: Adding {remaining} remaining items to backpack");
                int addedToBackpack = Backpack.AddItem(item, remaining);
                remaining -= addedToBackpack;
            }

            int totalAdded = amount - remaining;
            GD.Print($"AddItemSmart: Total added: {totalAdded} out of {amount}");

            // 如果成功添加了物品且是第一次获得，标记为已获得
            if (totalAdded > 0 && isFirstTime)
            {
                MarkItemAsObtained(item);
                
                // 如果是第一次获得且需要显示弹窗，触发弹窗显示
                if (showPopupIfFirstTime)
                {
                    ShowItemObtainedPopup(item);
                }
            }

            return totalAdded;
        }

        /// <summary>
        /// 显示获得物品弹窗
        /// </summary>
        private void ShowItemObtainedPopup(ItemDefinition item)
        {
            if (item == null)
            {
                return;
            }

            // 通过UIManager加载并显示弹窗
            if (Kuros.Managers.UIManager.Instance != null)
            {
                var popup = Kuros.Managers.UIManager.Instance.LoadItemObtainedPopup();
                if (popup != null)
                {
                    popup.ShowItem(item);
                    GD.Print($"PlayerInventoryComponent: 显示获得物品弹窗: {item.DisplayName}");
                }
            }
            else
            {
                GD.PrintErr("PlayerInventoryComponent: UIManager未初始化，无法显示获得物品弹窗");
            }
        }

        public bool TryAddItem(ItemDefinition item, int amount)
        {
            return Backpack.TryAddItem(item, amount);
        }

        public int RemoveItem(string itemId, int amount)
        {
            return Backpack.RemoveItem(itemId, amount);
        }
    }
}

