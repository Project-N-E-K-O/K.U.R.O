using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家背包组件，封装背包容器并提供基础接口。
    /// </summary>
    public partial class PlayerInventoryComponent : Node
    {
        [Export(PropertyHint.Range, "1,200,1")]
        public int BackpackSlots { get; set; } = 4;

        public InventoryContainer Backpack { get; private set; } = null!;
        public InventoryContainer? QuickBar { get; set; }

        // 跟踪已获得的物品ID（用于判断是否是第一次获得）
        private HashSet<string> _obtainedItemIds = new HashSet<string>();

        [ExportGroup("Special Slots")]
        [Export] public Godot.Collections.Array<SpecialInventorySlotConfig> SpecialSlotConfigs
        {
            get => _specialSlotConfigs;
            set => _specialSlotConfigs = value ?? new();
        }

        private Godot.Collections.Array<SpecialInventorySlotConfig> _specialSlotConfigs = new();
        private readonly Dictionary<string, SpecialInventorySlot> _specialSlots = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, SpecialInventorySlot> SpecialSlots => _specialSlots;
        public SpecialInventorySlot? WeaponSlot => GetSpecialSlot(SpecialInventorySlotIds.PrimaryWeapon);
        public int SelectedBackpackSlot { get; private set; }
        public bool HasSelectedItem => GetSelectedBackpackStack() != null;

        // 事件
        public event Action<ItemDefinition>? ItemPicked;
        public event Action<string>? ItemRemoved;
        public event Action<ItemDefinition>? WeaponEquipped;
        public event Action? WeaponUnequipped;
        public event Action<int>? ActiveBackpackSlotChanged;

        public override void _Ready()
        {
            base._Ready();

            Backpack = GetNodeOrNull<InventoryContainer>("Backpack") ?? CreateBackpack();
            Backpack.SlotCount = BackpackSlots;
            Backpack.InventoryChanged += OnBackpackInventoryChanged;

            // 初始化特殊槽位
            InitializeSpecialSlots();
            InitializeSelection();
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
            // 参数验证：检查 item 是否为 null
            if (item == null)
            {
                GameLogger.Error(nameof(PlayerInventoryComponent), "AddItemSmart: item is null, cannot add null item to inventory.");
                return 0;
            }

            // 参数验证：检查 amount 是否为正数
            if (amount <= 0)
            {
                GameLogger.Warn(nameof(PlayerInventoryComponent), $"AddItemSmart: amount ({amount}) is not positive for item '{item.DisplayName}' (ID: {item.ItemId}), nothing to add.");
                return 0;
            }

            // 确保 remaining 从已验证的正数 amount 初始化
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

        public bool TryAssignSpecialSlotFromBackpack(string specialSlotId, int backpackSlotIndex, int requestedQuantity = 0)
        {
            if (!TryResolveSpecialSlot(specialSlotId, out var slot) || Backpack == null) return false;
            if (!slot.IsEmpty) return false;

            var sourceStack = Backpack.GetStack(backpackSlotIndex);
            if (sourceStack == null) return false;
            if (!slot.CanAccept(sourceStack.Item)) return false;

            int transferAmount = requestedQuantity > 0 ? Math.Min(requestedQuantity, sourceStack.Quantity) : sourceStack.Quantity;
            transferAmount = slot.ClampQuantity(transferAmount);
            if (transferAmount <= 0) return false;

            if (!Backpack.TryExtractFromSlot(backpackSlotIndex, transferAmount, out var extracted) || extracted == null)
            {
                return false;
            }

            if (slot.TryAssign(extracted))
            {
                if (specialSlotId == SpecialInventorySlotIds.PrimaryWeapon)
                {
                    WeaponEquipped?.Invoke(extracted.Item);
                }
                return true;
            }

            Backpack.AddItem(extracted.Item, extracted.Quantity);
            return false;
        }

        public bool TryEquipWeaponFromBackpack(int backpackSlotIndex)
        {
            return TryAssignSpecialSlotFromBackpack(SpecialInventorySlotIds.PrimaryWeapon, backpackSlotIndex);
        }

        public bool TryUnequipSpecialSlotToBackpack(string specialSlotId)
        {
            if (!TryResolveSpecialSlot(specialSlotId, out var slot) || Backpack == null) return false;
            if (slot.IsEmpty) return false;

            var stack = slot.TakeStack();
            if (stack == null || stack.IsEmpty) return false;

            int inserted = Backpack.AddItem(stack.Item, stack.Quantity);
            if (inserted == stack.Quantity)
            {
                NotifyItemRemoved(stack.Item.ItemId);
                if (specialSlotId == SpecialInventorySlotIds.PrimaryWeapon)
                {
                    WeaponUnequipped?.Invoke();
                }
                return true;
            }

            int remaining = stack.Quantity - inserted;
            if (remaining > 0)
            {
                var restoreStack = new InventoryItemStack(stack.Item, remaining);
                slot.TryAssign(restoreStack, replaceExisting: true);
            }

            NotifyItemRemoved(stack.Item.ItemId);
            if (specialSlotId == SpecialInventorySlotIds.PrimaryWeapon)
            {
                WeaponUnequipped?.Invoke();
            }
            return false;
        }

        public bool RemoveFirstItem(string itemId)
        {
            if (Backpack == null) return false;

            for (int i = 0; i < Backpack.Slots.Count; i++)
            {
                var stack = Backpack.Slots[i];
                if (stack == null || stack.Item.ItemId != itemId) continue;

                Backpack.RemoveItem(itemId, stack.Quantity);
                NotifyItemRemoved(itemId);
                return true;
            }

            return false;
        }

        public bool TryUnequipWeaponToBackpack()
        {
            return TryUnequipSpecialSlotToBackpack(SpecialInventorySlotIds.PrimaryWeapon);
        }

        public bool TryExtractFromSelectedSlot(int amount, out InventoryItemStack? extracted)
        {
            extracted = null;
            if (Backpack == null) return false;
            return Backpack.TryExtractFromSlot(SelectedBackpackSlot, amount, out extracted);
        }

        public bool TryReturnStackToSelectedSlot(InventoryItemStack? stack, out int acceptedQuantity)
        {
            acceptedQuantity = 0;
            if (Backpack == null || stack == null || stack.IsEmpty) return false;

            int accepted = Backpack.TryAddItemToSlot(stack.Item, stack.Quantity, SelectedBackpackSlot);
            if (accepted <= 0)
            {
                return false;
            }

            acceptedQuantity = Math.Min(accepted, stack.Quantity);
            if (acceptedQuantity > 0)
            {
                stack.Remove(acceptedQuantity);
            }

            return true;
        }

        public int TryAddItemToSelectedSlot(ItemDefinition item, int quantity)
        {
            if (Backpack == null || item == null || quantity <= 0) return 0;

            int accepted = Backpack.TryAddItemToSlot(item, quantity, SelectedBackpackSlot);
            if (accepted > 0)
            {
                NotifyItemPicked(item);
                return accepted;
            }

            return 0;
        }

        public void SelectNextBackpackSlot()
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            SetSelectedBackpackSlot(SelectedBackpackSlot + 1);
        }

        public void SelectPreviousBackpackSlot()
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            SetSelectedBackpackSlot(SelectedBackpackSlot - 1);
        }

        public InventoryItemStack? GetSelectedBackpackStack()
        {
            return Backpack?.GetStack(SelectedBackpackSlot);
        }

        public float GetBackpackAttributeValue(string attributeId, float baseValue = 0f)
        {
            return Backpack?.GetAttributeValue(attributeId, baseValue) ?? baseValue;
        }

        public Dictionary<string, float> GetBackpackAttributeSnapshot()
        {
            return Backpack?.GetAttributeSnapshot() ?? new Dictionary<string, float>();
        }

        public SpecialInventorySlot? GetSpecialSlot(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId)) return null;
            return _specialSlots.TryGetValue(slotId, out var slot) ? slot : null;
        }

        internal void NotifyItemPicked(ItemDefinition item)
        {
            ItemPicked?.Invoke(item);
            OnItemPicked(item);
        }

        protected virtual void OnItemPicked(ItemDefinition item)
        {
        }

        internal void NotifyItemRemoved(string itemId)
        {
            ItemRemoved?.Invoke(itemId);
            OnItemRemoved(itemId);
        }

        protected virtual void OnItemRemoved(string itemId)
        {
        }

        private bool TryResolveSpecialSlot(string slotId, out SpecialInventorySlot slot)
        {
            slot = null!;
            var resolved = GetSpecialSlot(slotId);
            if (resolved == null) return false;
            slot = resolved;
            return true;
        }

        private void InitializeSpecialSlots()
        {
            _specialSlots.Clear();
            bool hasWeaponSlot = false;

            foreach (var config in _specialSlotConfigs)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.SlotId)) continue;
                var slot = new SpecialInventorySlot(config);
                _specialSlots[slot.SlotId] = slot;
                if (slot.SlotId == SpecialInventorySlotIds.PrimaryWeapon)
                {
                    hasWeaponSlot = true;
                }
            }

            if (!hasWeaponSlot)
            {
                var defaultWeapon = new SpecialInventorySlot(SpecialInventorySlotConfig.CreateDefaultWeapon());
                _specialSlots[defaultWeapon.SlotId] = defaultWeapon;
            }
        }

        private void InitializeSelection()
        {
            if (Backpack == null || Backpack.Slots.Count == 0)
            {
                SelectedBackpackSlot = 0;
                ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
                return;
            }

            SelectedBackpackSlot = 0;
            ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
        }

        private void SetSelectedBackpackSlot(int index)
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            int count = Backpack.Slots.Count;
            int normalized = ((index % count) + count) % count;
            if (normalized == SelectedBackpackSlot) return;

            SelectedBackpackSlot = normalized;
            ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
        }

        private void OnBackpackInventoryChanged()
        {
            if (Backpack == null || Backpack.Slots.Count == 0)
            {
                SelectedBackpackSlot = 0;
                ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
                return;
            }

            if (SelectedBackpackSlot >= Backpack.Slots.Count)
            {
                SelectedBackpackSlot = Backpack.Slots.Count - 1;
                ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
            }
        }
    }
}
