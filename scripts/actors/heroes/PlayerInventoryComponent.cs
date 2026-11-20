using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.Durability;
using Kuros.Items.Effects;
using Kuros.Items.Tags;
using Kuros.Managers;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家背包组件，封装背包容器并提供基础接口。
    /// </summary>
    public partial class PlayerInventoryComponent : Node
    {
        /// <summary>背包容器格数（物品溢出快捷栏后放入背包的上限）。</summary>
        [Export(PropertyHint.Range, "1,200,1")] public int BackpackSlots { get; set; } = 5;

        /// <summary>可携带武器总数上限（= 已解锁武器槽数）：初始值，Build 升级通过 UnlockWeaponSlot 增长。</summary>
        [Export(PropertyHint.Range, "1,20,1")] public int MaxCarriedWeaponCount { get; set; } = 3;
        /// <summary>武器槽位解锁封顶（MaxCarriedWeaponCount 增长上限，对应快捷栏槽位数）。</summary>
        [Export(PropertyHint.Range, "1,20,1")] public int MaxCarriedWeaponSlots { get; set; } = 5;
        private int _initialMaxCarriedWeaponCount = 3; // _Ready 时保存初始值，供 ResetWeaponSlots 还原
        public InventoryContainer Backpack { get; private set; } = null!;
        public InventoryContainer? QuickBar { get; set; }

        /// <summary>空手默认武器定义（未装备任何武器时使用的武器/技能）。</summary>
        [Export] public ItemDefinition? UnarmedWeaponDefinition { get; set; }
        
        /// <summary>是否保留快捷栏第 1 格（索引 0）给默认武器，新拾取的物品从第 2 格开始放置。</summary>
        [Export] public bool ReserveQuickBarSlot0ForDefaultWeapon { get; set; } = false;
        /// <summary>是否显示"获得新物品"弹窗（首次获得武器/物品时的信息弹窗；关闭后不再弹出）。</summary>
        [Export] public bool ShowObtainedPopupEnabled { get; set; } = true;
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

        /// <summary>特殊装备槽配置列表（如主武器槽）；未配置时自动兜底创建默认主武器槽。</summary>
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
        /// <summary>AddItemSmart 首次获得某物品时触发（拾取路径统一入口，图鉴/记忆数据源）。</summary>
        public event Action<ItemDefinition>? ItemFirstAcquired;
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
        /// <summary>
        /// 战斗武器解析可能变化（投掷 CD 开始/结束等，槽位内容不变但"手中武器"语义变了）。
        /// 技能控制器监听此事件重新应用武器/空手技能回退——投掷 CD 期间按空手处理。
        /// </summary>
        public event Action? CombatWeaponResolutionChanged;

        /// <summary>广播战斗武器解析变化（供投掷 CD 开始/结束等场景主动通知）。</summary>
        public void NotifyCombatWeaponResolutionChanged()
        {
            CombatWeaponResolutionChanged?.Invoke();
        }

        public override void _Ready()
        {
            base._Ready();

            _initialMaxCarriedWeaponCount = MaxCarriedWeaponCount; // 保存初始武器槽数（ResetWeaponSlots 还原用）

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


	public override void _Process(double delta)
	{
		base._Process(delta);
		if (QuickBar == null) return;
		for (int i = 0; i < QuickBar.SlotCount; i++)
		{
			var stack = QuickBar.GetStack(i);
			if (stack == null || stack.ThrowCooldownRemaining <= 0f) continue;
			stack.ThrowCooldownRemaining -= (float)delta;
			// CD 到期：武器重新可用，战斗武器解析变化（技能控制器从空手回退切回武器技能）
			if (stack.ThrowCooldownRemaining <= 0f)
			{
				stack.ThrowCooldownRemaining = 0f;
				NotifyCombatWeaponResolutionChanged();
			}
		}
	}
        /// <summary>创建背包容器（场景中无 Backpack 节点时兜底创建）。</summary>
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
        /// 设置快捷栏容器引用（由 HUD 在连接玩家时传入其创建的 5 槽容器）。
        /// 绑定成功触发 QuickBarAssigned 事件。
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
        public int AddItemSmart(ItemDefinition item, int amount, GameActor? owner = null, bool showPopupIfFirstTime = true,
            bool recordAcquisition = true)
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
            // 家具类型不显示新获取介绍弹窗
            if (item.IsFurniture)
            {
                int added = AddFurnitureItem(item, amount, owner, showPopupIfFirstTime: false);
                if (recordAcquisition && added > 0) SaveManager.Instance?.RecordRunItem(item, added);
                return added;
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
                // 武器只放入已解锁的槽位（Build 升级解锁）；非武器物品可放入全部槽位
                int quickBarEndExclusive = IsWeaponItem(item) ? GetUnlockedWeaponSlots() : 5;

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

            // 步驟4：背包（Backpack）当前无 UI 界面，放入后玩家不可见——
            // 快捷栏放不下的剩余物品直接拒绝并提示（不再溢出到 Backpack）
            if (remaining > 0)
            {
                GameLogger.Warn(nameof(PlayerInventoryComponent), $"快捷栏已满，拒绝拾取 '{item.DisplayName}' x{remaining}（背包无 UI，避免物品不可见）");
                ShowInventoryFullMessage(item, remaining);
            }

            int totalAdded = requestedAmount - remaining;
            if (recordAcquisition && totalAdded > 0) SaveManager.Instance?.RecordRunItem(item, totalAdded);

            // 如果成功添加了物品且是第一次获得，标记为已获得
            if (totalAdded > 0 && isFirstTime)
            {
                ItemFirstAcquired?.Invoke(item);
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
        private int AddFurnitureItem(ItemDefinition item, int amount, GameActor? owner, bool showPopupIfFirstTime)
        {
            if (HasFurnitureItem)
            {
                GameLogger.Info(nameof(PlayerInventoryComponent), $"AddFurnitureItem: 家具槽已被占用（{FurnitureSlotStack!.Item.DisplayName}），无法拾取 '{item.DisplayName}'。");
                return 0;
            }

            bool isFirstTime = IsFirstTimeObtaining(item);
            FurnitureSlotStack = new InventoryItemStack(item, 1);
            int totalAdded = 1;

            if (owner != null)
            {
                item.ApplyEffects(owner, ItemEffectTrigger.OnEquip);
            }

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
        public bool TryExtractFromFurnitureSlot(int amount, out InventoryItemStack? extracted, GameActor? owner = null)
        {
            extracted = null;
            if (!HasFurnitureItem)
            {
                return false;
            }

            extracted = new InventoryItemStack(FurnitureSlotStack!.Item, 1);
            extracted.RuntimeSourceTag = FurnitureSlotStack.RuntimeSourceTag;   // 来源身份随栈副本转移(放置出口消费)
            extracted.RuntimeIsThrowCoreCopy = FurnitureSlotStack.RuntimeIsThrowCoreCopy;
            extracted.RuntimeThrowCountUsed = FurnitureSlotStack.RuntimeThrowCountUsed; // 投掷耐久已用次数(B_006)随件转移
            if (owner != null)
            {
                FurnitureSlotStack.Item.RemoveEffects(owner, ItemEffectTrigger.OnEquip);
            }
            FurnitureSlotStack = null;
            FurnitureSlotChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 清除家具槽（用于丢弃/投掷后清除）。
        /// 与 TryExtractFromFurnitureSlot 一致，清空后触发 FurnitureSlotChanged，
        /// 供 SamplePlayer 恢复左手持有、PlayerWeaponSkillController 重估武器技能。
        /// </summary>
        public void ClearFurnitureSlot(GameActor? owner = null)
        {
            if (owner != null && FurnitureSlotStack != null)
            {
                FurnitureSlotStack.Item.RemoveEffects(owner, ItemEffectTrigger.OnEquip);
            }
            FurnitureSlotStack = null;
            FurnitureSlotChanged?.Invoke();
        }
        /// <summary>
        /// 快捷栏/背包已满提示（轻量状态消息，避免物品静默丢失）。
        /// </summary>
        private void ShowInventoryFullMessage(ItemDefinition item, int amount)
        {
            var battleHUD = Kuros.Managers.UIManager.Instance?.GetUI<Kuros.UI.BattleHUD>("BattleHUD");
            battleHUD?.ShowStatusMessage($"背包已满，无法拾取 {item.DisplayName}" + (amount > 1 ? $" x{amount}" : ""));
        }

        private void ShowItemObtainedPopup(ItemDefinition item)
        {
            // 全局开关：关闭后跳过弹窗（不打断游戏流程）
            if (!ShowObtainedPopupEnabled)
            {
                return;
            }

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

        /// <summary>直接向背包容器添加物品（不走快捷栏优先逻辑）。</summary>
        public bool TryAddItem(ItemDefinition item, int amount)
        {
            int accepted = Backpack.AddItem(item, amount);
            if (accepted > 0) SaveManager.Instance?.RecordRunItem(item, accepted);
            return accepted == amount;
        }

        /// <summary>从背包容器移除指定数量的物品（按物品 ID 匹配）。</summary>
        public int RemoveItem(string itemId, int amount)
        {
            return Backpack.RemoveItem(itemId, amount);
        }

        /// <summary>
        /// 从背包指定槽位转移物品到特殊装备槽（如主武器槽）。
        /// 槽位为空且物品满足 CanAccept 时执行；主武器槽成功会触发 WeaponEquipped。
        /// </summary>
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

        /// <summary>将背包指定槽位的物品装备为主武器（等价于 TryAssignSpecialSlotFromBackpack 主武器槽）。</summary>
        public bool TryEquipWeaponFromBackpack(int backpackSlotIndex)
        {
            return TryAssignSpecialSlotFromBackpack(SpecialInventorySlotIds.PrimaryWeapon, backpackSlotIndex);
        }

        /// <summary>
        /// 将特殊装备槽（如主武器）卸下放回背包。
        /// 背包放满时部分放回失败会把剩余数量重新放回槽位。
        /// </summary>
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

        /// <summary>从背包中移除第一个匹配指定 ID 的整组物品，并发出 ItemRemoved 通知。</summary>
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

        /// <summary>卸下主武器放回背包。</summary>
        public bool TryUnequipWeaponToBackpack()
        {
            return TryUnequipSpecialSlotToBackpack(SpecialInventorySlotIds.PrimaryWeapon);
        }

        /// <summary>从当前选中的背包槽位提取指定数量物品。</summary>
        public bool TryExtractFromSelectedSlot(int amount, out InventoryItemStack? extracted)
        {
            extracted = null;
            if (Backpack == null) return false;
            return Backpack.TryExtractFromSlot(SelectedBackpackSlot, amount, out extracted);
        }

        /// <summary>将物品堆叠归还到当前选中的背包槽位（部分归还时从原堆叠扣除已接受数量）。</summary>
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

        /// <summary>
        /// 消耗背包中第一个带指定标签的可食用物品：
        /// 走耐久度扣除（DurabilityConfig）或数量扣除两种路径，
        /// 消耗成功时触发 OnConsume 效果并发送槽位/物品栏变更信号。
        /// </summary>
        public bool TryConsumeFirstTaggedItem(string requiredTag, GameActor? consumer)
        {
            if (Backpack == null || string.IsNullOrWhiteSpace(requiredTag))
            {
                return false;
            }

            for (int slotIndex = 0; slotIndex < Backpack.Slots.Count; slotIndex++)
            {
                var stack = Backpack.GetStack(slotIndex);
                if (stack == null || stack.IsEmpty || !stack.HasTag(requiredTag))
                {
                    continue;
                }

                if (stack.DurabilityState != null && stack.DurabilityState.IsBroken)
                {
                    continue;
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
                    int removed = Backpack.RemoveItemFromSlot(slotIndex, 1);
                    if (removed <= 0)
                    {
                        continue;
                    }

                    removedStack = Backpack.GetStack(slotIndex) == null;
                }

                if (consumer != null)
                {
                    item.ApplyEffects(consumer, ItemEffectTrigger.OnConsume);
                }

                if (removedStack)
                {
                    if (usesDurability)
                    {
                        Backpack.SetStack(slotIndex, null);
                    }

                    NotifyItemRemoved(item.ItemId);
                }
                else if (usesDurability)
                {
                    Backpack.EmitSignal(InventoryContainer.SignalName.SlotChanged, slotIndex, item.ItemId, stack.Quantity);
                    Backpack.EmitSignal(InventoryContainer.SignalName.InventoryChanged);
                }

                return true;
            }

            return false;
        }

        /// <summary>向当前选中的背包槽位添加物品，成功时发出 ItemPicked 通知。</summary>
        public int TryAddItemToSelectedSlot(ItemDefinition item, int quantity)
        {
            if (Backpack == null || item == null || quantity <= 0) return 0;

            int accepted = Backpack.TryAddItemToSlot(item, quantity, SelectedBackpackSlot);
            if (accepted > 0)
            {
                SaveManager.Instance?.RecordRunItem(item, accepted);
                NotifyItemPicked(item);
                return accepted;
            }

            return 0;
        }

        /// <summary>选中下一个背包槽位（循环）。</summary>
        public void SelectNextBackpackSlot()
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            SetSelectedBackpackSlot(SelectedBackpackSlot + 1);
        }

        /// <summary>选中上一个背包槽位（循环）。</summary>
        public void SelectPreviousBackpackSlot()
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            SetSelectedBackpackSlot(SelectedBackpackSlot - 1);
        }

        /// <summary>获取当前选中背包槽位的物品堆叠。</summary>
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

        /// <summary>获取当前武器定义：战斗武器（装备槽/快捷栏/背包依次）或空手默认武器。</summary>
        public ItemDefinition? GetCurrentWeaponDefinition()
        {
            return GetActiveCombatWeaponDefinition() ?? UnarmedWeaponDefinition;
        }

        /// <summary>获取主武器特殊槽中的物品堆叠（未装备返回 null）。</summary>
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

        /// <summary>
        /// 获取当前参与战斗的武器定义，优先级：主武器装备槽 → 快捷栏选中槽 → 背包选中槽。
        /// </summary>
        public ItemDefinition? GetActiveCombatWeaponDefinition()
        {
            // 投掷 CD 中的堆叠不参与战斗武器解析（视为空手）：武器在飞行/冷却中，手上没有它，
            // 属性/技能/武器需求匹配全部按空手处理，CD 结束后由 CombatWeaponResolutionChanged 通知重载
            var equippedStack = GetEquippedWeaponStack();
            if (equippedStack != null && equippedStack.Item != null && !equippedStack.IsThrowOnCooldown)
            {
                return equippedStack.Item;
            }

            var quickBarStack = GetSelectedQuickBarStack();
            if (quickBarStack != null && !quickBarStack.IsEmpty && !quickBarStack.IsThrowOnCooldown && quickBarStack.Item.ItemId != "empty_item")
            {
                return quickBarStack.Item;
            }

            var backpackStack = GetSelectedBackpackStack();
            if (backpackStack != null && !backpackStack.IsEmpty && !backpackStack.IsThrowOnCooldown && backpackStack.Item.ItemId != "empty_item")
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
        public bool TryExtractFromSelectedQuickBarSlot(int amount, out InventoryItemStack? extracted, GameActor? owner = null)
        {
            extracted = null;

            // 家具槽优先
            if (HasFurnitureItem)
            {
                return TryExtractFromFurnitureSlot(amount, out extracted, owner);
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

        /// <summary>
        /// 当前已解锁的武器快捷栏槽位数 = MaxCarriedWeaponCount（单一数据源）。
        /// 与 Build 等级解耦：仅由 Build 效果（解锁武器槽卡片）调用 UnlockWeaponSlot 增长；
        /// UI 锁图与 AddItemSmart 拾取范围共用。
        /// </summary>
        public int GetUnlockedWeaponSlots()
        {
            return Mathf.Clamp(MaxCarriedWeaponCount, 1, Mathf.Max(1, MaxCarriedWeaponSlots));
        }

        /// <summary>解锁一个武器槽位：MaxCarriedWeaponCount +1，封顶 MaxCarriedWeaponSlots（Build 解锁槽位效果调用）。</summary>
        public void UnlockWeaponSlot()
        {
            MaxCarriedWeaponCount = Mathf.Min(MaxCarriedWeaponCount + 1, Mathf.Max(1, MaxCarriedWeaponSlots));
        }

        /// <summary>重置武器槽位到初始值（新游戏开始时调用）。</summary>
        public void ResetWeaponSlots()
        {
            MaxCarriedWeaponCount = Mathf.Max(1, _initialMaxCarriedWeaponCount);
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

        /// <summary>按属性 ID 汇总背包中所有物品的属性值（带默认值兜底）。</summary>
        public float GetBackpackAttributeValue(string attributeId, float baseValue = 0f)
        {
            return Backpack?.GetAttributeValue(attributeId, baseValue) ?? baseValue;
        }

        /// <summary>获取背包全部物品属性的汇总快照。</summary>
        public Dictionary<string, float> GetBackpackAttributeSnapshot()
        {
            return Backpack?.GetAttributeSnapshot() ?? new Dictionary<string, float>();
        }

        /// <summary>按 ID 查找特殊装备槽（如主武器槽）。</summary>
        public SpecialInventorySlot? GetSpecialSlot(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId)) return null;
            return _specialSlots.TryGetValue(slotId, out var slot) ? slot : null;
        }

        /// <summary>物品拾取通知：触发 ItemPicked 事件并调用可重写钩子（子类可扩展）。</summary>
        internal void NotifyItemPicked(ItemDefinition item)
        {
            ItemPicked?.Invoke(item);
            OnItemPicked(item);
        }

        /// <summary>子类钩子：物品拾取后调用。</summary>
        protected virtual void OnItemPicked(ItemDefinition item)
        {
        }

        /// <summary>物品移除通知：触发 ItemRemoved 事件并调用可重写钩子。</summary>
        internal void NotifyItemRemoved(string itemId)
        {
            ItemRemoved?.Invoke(itemId);
            OnItemRemoved(itemId);
        }

        /// <summary>子类钩子：物品移除后调用。</summary>
        protected virtual void OnItemRemoved(string itemId)
        {
        }

        /// <summary>解析特殊装备槽（不存在返回 false）。</summary>
        private bool TryResolveSpecialSlot(string slotId, out SpecialInventorySlot slot)
        {
            slot = null!;
            var resolved = GetSpecialSlot(slotId);
            if (resolved == null) return false;
            slot = resolved;
            return true;
        }

        /// <summary>判断物品是否为武器（Weapon 标签或 Category 为 "Weapon"，排除空物品）。</summary>
        private static bool IsWeaponItem(ItemDefinition? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || item.ItemId == "empty_item")
            {
                return false;
            }

            return item.HasTag(ItemTagIds.Weapon) ||
                   string.Equals(item.Category, "Weapon", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>统计容器内武器数量（每格按 1 计，用于武器携带上限判断）。</summary>
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

        /// <summary>
        /// 初始化特殊装备槽：从配置创建（如主武器槽）；未配置主武器槽时
        /// 兜底创建默认武器槽（空手定义）。
        /// </summary>
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

        /// <summary>初始化选中槽位为 0，并通知订阅者。</summary>
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

        /// <summary>设置选中背包槽位（循环取模，变化时发出 ActiveBackpackSlotChanged）。</summary>
        private void SetSelectedBackpackSlot(int index)
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            int count = Backpack.Slots.Count;
            int normalized = ((index % count) + count) % count;
            if (normalized == SelectedBackpackSlot) return;

            SelectedBackpackSlot = normalized;
            ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
        }

        /// <summary>背包内容变化时校正选中槽位索引（越界则回退到最后一个槽位）。</summary>
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
