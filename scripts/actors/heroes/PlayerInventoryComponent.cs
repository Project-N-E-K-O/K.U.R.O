using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.Durability;
using Kuros.Items.Effects;
using Kuros.Items.Tags;
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
        public int BackpackSlots { get; set; } = 5;
        [Export(PropertyHint.Range, "1,20,1")]
        public int MaxCarriedWeaponCount { get; set; } = 5;

        public InventoryContainer Backpack { get; private set; } = null!;
        public InventoryContainer? QuickBar { get; set; }
        [Export] public ItemDefinition? UnarmedWeaponDefinition { get; set; }
        [Export] public bool ReserveQuickBarSlot0ForDefaultWeapon { get; set; } = false;
        private const string DefaultUnarmedWeaponPath = "res://resources/items/Weapon_Unarmed_Default.tres";

        // 跟踪已获得的物品ID（用于判断是否是第一次获得）
private readonly HashSet<string> _obtainedItemIds = new HashSet<string>();

		/// <summary>
		/// 飞行中的投掷武器所占用的快捷栏槽位索引集合。
		/// AddItemSmart 不将新物品放入这些槽位，防止占用投掷武器的归还位置。
		/// </summary>
		public HashSet<int> ReservedQuickBarSlots { get; } = new();

		/// <summary>
		/// 家具槽位（隐藏第6格）：只允许 IsFurniture=true 的物品放置，最多1个。
		/// 当此槽有物品时，优先使用此槽的物品（禁止切换到快捷栏的其他槽位）。
		/// </summary>
		public InventoryItemStack? FurnitureSlotStack { get; private set; }

		/// <summary>
		/// 家具槽是否有物品
		/// </summary>
		public bool HasFurnitureItem => FurnitureSlotStack != null && !FurnitureSlotStack.IsEmpty
			&& FurnitureSlotStack.Item.ItemId != "empty_item";

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
        public bool HasSelectedItem
        {
            get
            {
                var stack = GetSelectedBackpackStack();
                return stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item";
            }
        }
        
        /// <summary>
        /// 當前選中的快捷欄槽位索引（0-4，對應快捷欄1-5）
        /// -1 表示未選中任何槽位
        /// </summary>
        public int SelectedQuickBarSlot
        {
            get => _selectedQuickBarSlot;
            set
            {
                if (_selectedQuickBarSlot == value)
                {
                    return;
                }

                _selectedQuickBarSlot = value;
                QuickBarSlotChanged?.Invoke(_selectedQuickBarSlot);
            }
        }
        // 默认选择快捷栏第1格（索引0），确保首次拾取优先进入第1格。
        private int _selectedQuickBarSlot = 0;
        
        /// <summary>
        /// 檢查左手選中的快捷欄槽位是否有物品
        /// </summary>
        public bool HasSelectedQuickBarItem
        {
            get
            {
                var stack = GetSelectedQuickBarStack();
                return stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item";
            }
        }

        // 事件
        public event Action<ItemDefinition>? ItemPicked;
        public event Action<string>? ItemRemoved;
        public event Action<ItemDefinition>? WeaponEquipped;
        public event Action? WeaponUnequipped;
        public event Action<int>? ActiveBackpackSlotChanged;
        public event Action? QuickBarAssigned;
        public event Action<int>? QuickBarSlotChanged;
        /// <summary>
        /// 家具槽变化事件（放入或清除时触发）
        /// </summary>
        public event Action? FurnitureSlotChanged;

        public override void _Ready()
        {
            base._Ready();

            Backpack = GetNodeOrNull<InventoryContainer>("Backpack") ?? CreateBackpack();
            Backpack.SlotCount = BackpackSlots;
            Backpack.InventoryChanged += OnBackpackInventoryChanged;

            if (UnarmedWeaponDefinition == null)
            {
                UnarmedWeaponDefinition = ResourceLoader.Load<ItemDefinition>(DefaultUnarmedWeaponPath);
            }

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
            if (quickBar == null)
            {
                return;
            }

            if (QuickBar == quickBar)
            {
                return;
            }

            QuickBar = quickBar;
            QuickBarAssigned?.Invoke();
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
        /// 智能添加物品：
        /// 1. 優先放入當前選中的快捷欄槽位（左手選中的槽位）
        /// 2. 如果選中槽位已有物品，依次查看快捷欄的各個索引（1-4），優先放置在最左側的空槽位
        /// 3. 快捷欄1（索引0）是小木劍，永遠不會被更改
        /// 4. 快捷欄滿時，溢出的物品放置到物品欄中；總武器攜帶上限由 MaxCarriedWeaponCount 控制
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

            // 家具物品：路由到家具槽位（隐藏第6格）
            if (item.IsFurniture)
            {
                return AddFurnitureItem(item, amount, showPopupIfFirstTime);
            }

            int requestedAmount = amount;
            if (IsWeaponItem(item))
            {
                int currentWeaponCount = GetCarriedWeaponCount();
                int remainingWeaponCapacity = Math.Max(0, MaxCarriedWeaponCount - currentWeaponCount);
                if (remainingWeaponCapacity <= 0)
                {
                    GameLogger.Info(nameof(PlayerInventoryComponent), $"AddItemSmart: 武器栏已满（{currentWeaponCount}/{MaxCarriedWeaponCount}），无法拾取 '{item.DisplayName}'。");
                    return 0;
                }

                requestedAmount = Math.Min(requestedAmount, remainingWeaponCapacity);
            }

            // 确保 remaining 从已验证的正数 amount 初始化
            int remaining = requestedAmount;
            bool isFirstTime = IsFirstTimeObtaining(item);

            // 优先放入快捷栏（默认包含索引0；若保留默认武器槽则从索引1开始）
            if (QuickBar != null && remaining > 0)
            {
                int quickBarStart = ReserveQuickBarSlot0ForDefaultWeapon ? 1 : 0;
                const int quickBarEndExclusive = 5;

                // 步驟1：優先嘗試放入當前選中的快捷欄槽位
                if (SelectedQuickBarSlot >= quickBarStart && SelectedQuickBarSlot < quickBarEndExclusive)
                {
                    var selectedStack = QuickBar.GetStack(SelectedQuickBarSlot);
                    // 檢查選中槽位是否為空、空白道具或可合併的相同物品，且未被投掷武器預占
                    if (!ReservedQuickBarSlots.Contains(SelectedQuickBarSlot) &&
                        (selectedStack == null || selectedStack.IsEmpty || 
                        selectedStack.Item.ItemId == "empty_item" ||
                        (selectedStack.Item.ItemId == item.ItemId && !selectedStack.IsFull)))
                    {
                        int added = QuickBar.TryAddItemToSlot(item, remaining, SelectedQuickBarSlot);
                        if (added > 0)
                        {
                            remaining -= added;
                        }
                    }
                }
                
                // 步驟2：如果還有剩餘，嘗試合併到已有相同物品的槽位
                for (int i = quickBarStart; i < quickBarEndExclusive && remaining > 0; i++)
                {
                    if (i == SelectedQuickBarSlot) continue; // 跳過已處理的選中槽位
                    
                    var existingStack = QuickBar.GetStack(i);
                    if (existingStack != null && !existingStack.IsEmpty && 
                        existingStack.Item.ItemId == item.ItemId && !existingStack.IsFull)
                    {
                        int added = QuickBar.TryAddItemToSlot(item, remaining, i);
                        if (added > 0)
                        {
                            remaining -= added;
                        }
                    }
                }
                
                // 步驟3：如果还有剩余，找到最左側的空槽位或空白道具槽位添加
                if (remaining > 0)
                {
                    for (int i = quickBarStart; i < quickBarEndExclusive && remaining > 0; i++)
                    {
                        if (i == SelectedQuickBarSlot) continue; // 跳過已處理的選中槽位
                        if (ReservedQuickBarSlots.Contains(i)) continue; // 跳過投掷武器預占槽位
                        
                        var existingStack = QuickBar.GetStack(i);
                        // 检查槽位是否为空或包含空白道具
                        if (existingStack == null || existingStack.IsEmpty || 
                            (existingStack.Item.ItemId == "empty_item"))
                        {
                            int added = QuickBar.TryAddItemToSlot(item, remaining, i);
                            if (added > 0)
                            {
                                remaining -= added;
                                break;
                            }
                        }
                    }
                }
            }

            // 步驟4：剩余物品放入物品栏（会自动替换空白道具）
            if (remaining > 0)
            {
                int addedToBackpack = Backpack.AddItem(item, remaining);
                remaining -= addedToBackpack;
            }

            int totalAdded = requestedAmount - remaining;

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
        /// 将家具物品放入家具槽位（隐藏第6格）。
        /// 家具槽只能容纳1件 IsFurniture=true 的物品。
        /// </summary>
        private int AddFurnitureItem(ItemDefinition item, int amount, bool showPopupIfFirstTime)
        {
            if (HasFurnitureItem)
            {
                GameLogger.Info(nameof(PlayerInventoryComponent), $"AddFurnitureItem: 家具槽已被占用（{FurnitureSlotStack!.Item.DisplayName}），无法拾取 '{item.DisplayName}'。");
                return 0;
            }

            bool isFirstTime = IsFirstTimeObtaining(item);
            FurnitureSlotStack = new InventoryItemStack(item, 1);
            int totalAdded = 1;

            FurnitureSlotChanged?.Invoke();

            if (totalAdded > 0 && isFirstTime)
            {
                MarkItemAsObtained(item);
                if (showPopupIfFirstTime)
                {
                    ShowItemObtainedPopup(item);
                }
            }

            return totalAdded;
        }

        /// <summary>
        /// 从家具槽提取物品
        /// </summary>
        public bool TryExtractFromFurnitureSlot(int amount, out InventoryItemStack? extracted)
        {
            extracted = null;
            if (!HasFurnitureItem)
            {
                return false;
            }

            extracted = new InventoryItemStack(FurnitureSlotStack!.Item, 1);
            FurnitureSlotStack = null;
            FurnitureSlotChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 清除家具槽（用于丢弃/投掷后清除）
        /// </summary>
        public void ClearFurnitureSlot()
        {
            FurnitureSlotStack = null;
        }
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

        /// <summary>
        /// 消耗当前选中槽位中的可食用物品（需要 Food 标签），触发 OnConsume 效果并根据耐久/数量扣除。
        /// </summary>
        public bool TryConsumeSelectedItem(GameActor? consumer)
        {
            if (Backpack == null) return false;
            var stack = GetSelectedBackpackStack();
            if (stack == null || stack.IsEmpty) return false;
            if (!stack.HasTag(ItemTagIds.Food))
            {
                return false;
            }

            if (stack.DurabilityState != null && stack.DurabilityState.IsBroken)
            {
                return false;
            }

            var item = stack.Item;
            bool usesDurability = item.DurabilityConfig != null && stack.HasDurability;
            bool removedStack = false;

            if (usesDurability && item.DurabilityConfig != null)
            {
                int damage = item.DurabilityConfig.DamagePerUse;
                if (damage <= 0)
                {
                    damage = 1;
                }

                bool broke = stack.ApplyDurabilityDamage(damage, consumer, triggerEffects: true);
                if (broke && item.DurabilityConfig.BreakBehavior == DurabilityBreakBehavior.Disappear)
                {
                    removedStack = true;
                }
            }
            else
            {
                int removed = Backpack.RemoveItemFromSlot(SelectedBackpackSlot, 1);
                if (removed <= 0)
                {
                    return false;
                }

                removedStack = Backpack.GetStack(SelectedBackpackSlot) == null;
            }

            if (consumer != null)
            {
                item.ApplyEffects(consumer, ItemEffectTrigger.OnConsume);
            }

            if (removedStack)
            {
                if (usesDurability)
                {
                    Backpack.SetStack(SelectedBackpackSlot, null);
                }
                NotifyItemRemoved(item.ItemId);
            }
            else if (usesDurability)
            {
                Backpack.EmitSignal(InventoryContainer.SignalName.SlotChanged, SelectedBackpackSlot, item.ItemId, stack.Quantity);
                Backpack.EmitSignal(InventoryContainer.SignalName.InventoryChanged);
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

        public float GetSelectedAttributeValue(string attributeId, float defaultValue = 0f)
        {
            if (string.IsNullOrWhiteSpace(attributeId)) return defaultValue;

            var quickBarStack = GetSelectedQuickBarStack();
            if (quickBarStack != null && !quickBarStack.IsEmpty && quickBarStack.Item.ItemId != "empty_item")
            {
                return quickBarStack.GetAttributeValue(attributeId, defaultValue);
            }

            var stack = GetSelectedBackpackStack();
            if (stack != null)
            {
                return stack.GetAttributeValue(attributeId, defaultValue);
            }

            if (UnarmedWeaponDefinition != null &&
                UnarmedWeaponDefinition.TryResolveAttribute(attributeId, 1, out var attribute) &&
                attribute.IsValid)
            {
                return attribute.Value;
            }
            return defaultValue;
        }

        public ItemDefinition? GetCurrentWeaponDefinition()
        {
            return GetActiveCombatWeaponDefinition() ?? UnarmedWeaponDefinition;
        }

        public InventoryItemStack? GetEquippedWeaponStack()
        {
            var slot = GetSpecialSlot(SpecialInventorySlotIds.PrimaryWeapon);
            if (slot == null || slot.IsEmpty)
            {
                return null;
            }

            var stack = slot.Stack;
            if (stack == null || stack.IsEmpty)
            {
                return null;
            }

            return stack;
        }

        public ItemDefinition? GetActiveCombatWeaponDefinition()
        {
            var equippedStack = GetEquippedWeaponStack();
            if (equippedStack != null && equippedStack.Item != null)
            {
                return equippedStack.Item;
            }

            var quickBarStack = GetSelectedQuickBarStack();
            if (quickBarStack != null && !quickBarStack.IsEmpty && quickBarStack.Item.ItemId != "empty_item")
            {
                return quickBarStack.Item;
            }

            var backpackStack = GetSelectedBackpackStack();
            if (backpackStack != null && !backpackStack.IsEmpty && backpackStack.Item.ItemId != "empty_item")
            {
                return backpackStack.Item;
            }

            return null;
        }

        /// <summary>
        /// 獲取當前選中的快捷欄槽位的物品堆疊。
        /// 如果家具槽有物品，优先返回家具槽的物品。
        /// </summary>
        public InventoryItemStack? GetSelectedQuickBarStack()
        {
            // 家具槽优先
            if (HasFurnitureItem)
            {
                return FurnitureSlotStack;
            }
            if (QuickBar == null || SelectedQuickBarSlot < 0 || SelectedQuickBarSlot > 4)
            {
                return null;
            }
            return QuickBar.GetStack(SelectedQuickBarSlot);
        }

        /// <summary>
        /// 嘗試從選中的快捷欄槽位提取物品。
        /// 如果家具槽有物品，优先从家具槽提取。
        /// </summary>
        public bool TryExtractFromSelectedQuickBarSlot(int amount, out InventoryItemStack? extracted)
        {
            extracted = null;

            // 家具槽优先
            if (HasFurnitureItem)
            {
                return TryExtractFromFurnitureSlot(amount, out extracted);
            }

            if (QuickBar == null || SelectedQuickBarSlot < 0 || SelectedQuickBarSlot > 4)
            {
                return false;
            }
            return QuickBar.TryExtractFromSlot(SelectedQuickBarSlot, amount, out extracted);
        }

        /// <summary>
        /// 嘗試將物品堆疊返回到選中的快捷欄槽位
        /// </summary>
        public bool TryReturnStackToSelectedQuickBarSlot(InventoryItemStack? stack, out int acceptedQuantity)
        {
            acceptedQuantity = 0;
            if (QuickBar == null || stack == null || stack.IsEmpty)
            {
                return false;
            }
            if (SelectedQuickBarSlot < 0 || SelectedQuickBarSlot > 4)
            {
                return false;
            }

            int accepted = QuickBar.TryAddItemToSlot(stack.Item, stack.Quantity, SelectedQuickBarSlot);
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

        public int GetCarriedWeaponCount()
        {
            int total = 0;
            total += CountWeaponStacksInContainer(Backpack);
            total += CountWeaponStacksInContainer(QuickBar);
            // 飞行中的投掷武器已从快捷栏提取（槽位变为 empty_item），但它们仍属于玩家。
            // 将 ReservedQuickBarSlots 数量视为虚拟武器数，防止在武器归还期间多拾取一件武器。
            total += ReservedQuickBarSlots.Count;

            foreach (var slot in _specialSlots.Values)
            {
                var stack = slot?.Stack;
                if (stack == null || stack.IsEmpty || stack.Item == null)
                {
                    continue;
                }

                if (IsWeaponItem(stack.Item))
                {
                    total += Math.Max(1, stack.Quantity);
                }
            }

            return total;
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

        private static bool IsWeaponItem(ItemDefinition? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || item.ItemId == "empty_item")
            {
                return false;
            }

            return item.HasTag(ItemTagIds.Weapon) ||
                   string.Equals(item.Category, "Weapon", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountWeaponStacksInContainer(InventoryContainer? container)
        {
            if (container == null)
            {
                return 0;
            }

            int total = 0;
            foreach (var stack in container.Slots)
            {
                if (stack == null || stack.IsEmpty || stack.Item == null)
                {
                    continue;
                }

                if (IsWeaponItem(stack.Item))
                {
                    total += Math.Max(1, stack.Quantity);
                }
            }

            return total;
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
