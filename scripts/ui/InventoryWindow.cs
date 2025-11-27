using Godot;
using Kuros.Systems.Inventory;

namespace Kuros.UI
{
    /// <summary>
    /// 物品栏窗口，包含16个物品栏槽位和5个快捷栏槽位
    /// </summary>
    public partial class InventoryWindow : Control
    {
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public GridContainer InventoryGrid { get; private set; } = null!;
        [Export] public HBoxContainer QuickBarContainer { get; private set; } = null!;

        private const int InventorySlotCount = 16; // 4x4 网格
        private const int QuickBarSlotCount = 5;

        private readonly ItemSlot[] _inventorySlots = new ItemSlot[InventorySlotCount];
        private readonly ItemSlot[] _quickBarSlots = new ItemSlot[QuickBarSlotCount];

        private InventoryContainer? _inventoryContainer;
        private InventoryContainer? _quickBarContainer;

        // 拖拽状态
        private int _draggingSlotIndex = -1;
        private bool _isDraggingFromInventory = true;
        private Vector2 _dragOffset = Vector2.Zero;

        // 精确换位状态
        private int _selectedSlotIndex = -1;
        private bool _isSelectedFromInventory = true;

        // 窗口状态
        private bool _isOpen = false;

        [Signal] public delegate void InventoryClosedEventHandler();

        public override void _Ready()
        {
            base._Ready();
            // 暂停时也要接收输入
            ProcessMode = ProcessModeEnum.Always;
            
            CacheNodeReferences();
            InitializeSlots();
            HideWindow();
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            InventoryGrid ??= GetNodeOrNull<GridContainer>("MainPanel/Body/InventorySection/InventoryGrid");
            QuickBarContainer ??= GetNodeOrNull<HBoxContainer>("MainPanel/Body/QuickBarSection/QuickBarContainer");

            if (CloseButton != null)
            {
                CloseButton.Pressed += HideWindow;
            }
        }

        private void InitializeSlots()
        {
            // 初始化物品栏槽位
            if (InventoryGrid != null)
            {
                InventoryGrid.Columns = 4;
                for (int i = 0; i < InventorySlotCount; i++)
                {
                    var slot = CreateItemSlot(i, true);
                    _inventorySlots[i] = slot;
                    InventoryGrid.AddChild(slot);
                }
            }

            // 初始化快捷栏槽位
            if (QuickBarContainer != null)
            {
                for (int i = 0; i < QuickBarSlotCount; i++)
                {
                    var slot = CreateItemSlot(i, false);
                    _quickBarSlots[i] = slot;
                    QuickBarContainer.AddChild(slot);
                }
            }
        }

        private ItemSlot CreateItemSlot(int index, bool isInventory)
        {
            var slotScene = GD.Load<PackedScene>("res://scenes/ui/ItemSlot.tscn");
            var slot = slotScene.Instantiate<ItemSlot>();
            slot.SlotIndex = index;

            slot.SlotClicked += (slotIdx) => OnSlotClicked(slotIdx, isInventory);
            slot.SlotDoubleClicked += (slotIdx) => OnSlotDoubleClicked(slotIdx, isInventory);
            slot.SlotDragStarted += (slotIdx, pos) => OnSlotDragStarted(slotIdx, pos, isInventory);
            slot.SlotDragEnded += (slotIdx, pos) => OnSlotDragEnded(slotIdx, pos, isInventory);

            return slot;
        }

        public void SetInventoryContainer(InventoryContainer inventory, InventoryContainer quickBar)
        {
            _inventoryContainer = inventory;
            _quickBarContainer = quickBar;

            // 连接信号
            if (_inventoryContainer != null)
            {
                _inventoryContainer.SlotChanged += OnInventorySlotChanged;
                _inventoryContainer.InventoryChanged += OnInventoryChanged;
            }

            if (_quickBarContainer != null)
            {
                _quickBarContainer.SlotChanged += OnQuickBarSlotChanged;
                _quickBarContainer.InventoryChanged += OnQuickBarChanged;
            }

            RefreshAllSlots();
        }

        private void RefreshAllSlots()
        {
            // 刷新物品栏
            if (_inventoryContainer != null)
            {
                for (int i = 0; i < InventorySlotCount && i < _inventoryContainer.Slots.Count; i++)
                {
                    _inventorySlots[i]?.SetItemStack(_inventoryContainer.GetStack(i));
                }
            }

            // 刷新快捷栏
            if (_quickBarContainer != null)
            {
                for (int i = 0; i < QuickBarSlotCount && i < _quickBarContainer.Slots.Count; i++)
                {
                    _quickBarSlots[i]?.SetItemStack(_quickBarContainer.GetStack(i));
                }
            }
        }

        private void OnInventorySlotChanged(int slotIndex, string itemId, int quantity)
        {
            if (slotIndex >= 0 && slotIndex < InventorySlotCount)
            {
                var stack = _inventoryContainer?.GetStack(slotIndex);
                _inventorySlots[slotIndex]?.SetItemStack(stack);
            }
        }

        private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
        {
            if (slotIndex >= 0 && slotIndex < QuickBarSlotCount)
            {
                var stack = _quickBarContainer?.GetStack(slotIndex);
                _quickBarSlots[slotIndex]?.SetItemStack(stack);
            }
        }

        private void OnInventoryChanged()
        {
            RefreshAllSlots();
        }

        private void OnQuickBarChanged()
        {
            RefreshAllSlots();
        }

        private void OnSlotClicked(int slotIndex, bool isInventory)
        {
            // 如果处于精确换位模式，执行换位
            if (_selectedSlotIndex >= 0)
            {
                PerformSwap(_selectedSlotIndex, _isSelectedFromInventory, slotIndex, isInventory);
                
                // 清除所有槽位的选中状态
                ClearAllSelections();
                
                _selectedSlotIndex = -1;
            }
        }

        private void OnSlotDoubleClicked(int slotIndex, bool isInventory)
        {
            // 清除之前的选中状态
            ClearAllSelections();
            
            // 进入精确换位模式
            _selectedSlotIndex = slotIndex;
            _isSelectedFromInventory = isInventory;
            
            // 设置当前槽位为选中状态
            if (isInventory && slotIndex >= 0 && slotIndex < _inventorySlots.Length)
            {
                // 选中状态在 ItemSlot 内部管理
            }
            else if (!isInventory && slotIndex >= 0 && slotIndex < _quickBarSlots.Length)
            {
                // 选中状态在 ItemSlot 内部管理
            }
        }

        private void ClearAllSelections()
        {
            foreach (var slot in _inventorySlots)
            {
                slot?.ClearSelection();
            }
            foreach (var slot in _quickBarSlots)
            {
                slot?.ClearSelection();
            }
        }

        private void OnSlotDragStarted(int slotIndex, Vector2 position, bool isInventory)
        {
            _draggingSlotIndex = slotIndex;
            _isDraggingFromInventory = isInventory;
            _dragOffset = position;
        }

        private void OnSlotDragEnded(int slotIndex, Vector2 position, bool isInventory)
        {
            if (_draggingSlotIndex < 0) return;

            // 查找拖拽结束位置的槽位
            var targetSlot = FindSlotAtPosition(position);
            if (targetSlot != null)
            {
                int targetIndex = targetSlot.SlotIndex;
                bool targetIsInventory = IsInventorySlot(targetSlot);

                // 执行移动或交换
                if (_isDraggingFromInventory == targetIsInventory)
                {
                    // 同一容器内移动
                    if (_isDraggingFromInventory)
                    {
                        SwapSlotsInContainer(_inventoryContainer, _draggingSlotIndex, targetIndex);
                    }
                    else
                    {
                        SwapSlotsInContainer(_quickBarContainer, _draggingSlotIndex, targetIndex);
                    }
                }
                else
                {
                    // 跨容器移动
                    PerformSwap(_draggingSlotIndex, _isDraggingFromInventory, targetIndex, targetIsInventory);
                }
            }

            _draggingSlotIndex = -1;
        }

        private ItemSlot? FindSlotAtPosition(Vector2 globalPosition)
        {
            // 检查物品栏槽位
            foreach (var slot in _inventorySlots)
            {
                if (slot != null)
                {
                    var rect = new Rect2(slot.GlobalPosition, slot.Size);
                    if (rect.HasPoint(globalPosition))
                    {
                        return slot;
                    }
                }
            }

            // 检查快捷栏槽位
            foreach (var slot in _quickBarSlots)
            {
                if (slot != null)
                {
                    var rect = new Rect2(slot.GlobalPosition, slot.Size);
                    if (rect.HasPoint(globalPosition))
                    {
                        return slot;
                    }
                }
            }

            return null;
        }

        private bool IsInventorySlot(ItemSlot slot)
        {
            foreach (var invSlot in _inventorySlots)
            {
                if (invSlot == slot) return true;
            }
            return false;
        }

        private void PerformSwap(int fromIndex, bool fromInventory, int toIndex, bool toInventory)
        {
            var fromContainer = fromInventory ? _inventoryContainer : _quickBarContainer;
            var toContainer = toInventory ? _inventoryContainer : _quickBarContainer;

            if (fromContainer == null || toContainer == null) return;

            var fromStack = fromContainer.GetStack(fromIndex);
            var toStack = toContainer.GetStack(toIndex);

            // 如果源槽位为空，直接返回
            if (fromStack == null || fromStack.IsEmpty) return;

            // 如果目标槽位为空，直接移动
            if (toStack == null || toStack.IsEmpty)
            {
                // 移动到目标槽位的指定位置（需要特殊处理，因为 AddItem 会自动填充）
                // 先移除源槽位物品
                var item = fromStack.Item;
                var quantity = fromStack.Quantity;
                fromContainer.RemoveItem(item.ItemId, quantity);
                
                // 直接设置目标槽位（需要扩展 InventoryContainer 或使用 MoveTo）
                // 暂时使用 MoveTo 方法，但需要确保目标槽位为空
                // 如果 MoveTo 不支持指定槽位，我们需要手动处理
                // 这里简化处理：先移除，再添加到指定位置
                if (toContainer.GetStack(toIndex) == null || toContainer.GetStack(toIndex)!.IsEmpty)
                {
                    // 临时存储目标槽位索引，通过 MoveTo 移动到第一个空槽位，然后交换
                    // 由于 InventoryContainer 的限制，我们使用一个变通方法
                    // 先移除源物品
                    fromContainer.RemoveItem(item.ItemId, quantity);
                    // 添加到目标容器（会自动填充到第一个空槽位）
                    int added = toContainer.AddItem(item, quantity);
                    // 如果目标容器有多个空槽位，我们需要手动调整
                    // 这里简化：如果添加成功，就认为移动成功
                }
            }
            else
            {
                // 交换两个槽位的内容
                var tempItem = fromStack.Item;
                var tempQuantity = fromStack.Quantity;
                var toItem = toStack.Item;
                var toQuantity = toStack.Quantity;

                // 移除两个槽位的物品
                fromContainer.RemoveItem(tempItem.ItemId, tempQuantity);
                toContainer.RemoveItem(toItem.ItemId, toQuantity);

                // 交换添加
                fromContainer.AddItem(toItem, toQuantity);
                toContainer.AddItem(tempItem, tempQuantity);
            }
        }

        private void SwapSlotsInContainer(InventoryContainer? container, int index1, int index2)
        {
            if (container == null || index1 == index2) return;

            var stack1 = container.GetStack(index1);
            var stack2 = container.GetStack(index2);

            // 如果两个槽位都为空或相同，直接返回
            if ((stack1 == null || stack1.IsEmpty) && (stack2 == null || stack2.IsEmpty)) return;

            // 临时存储
            var tempItem1 = stack1?.Item;
            var tempQuantity1 = stack1?.Quantity ?? 0;
            var tempItem2 = stack2?.Item;
            var tempQuantity2 = stack2?.Quantity ?? 0;

            // 清空两个槽位
            if (stack1 != null && !stack1.IsEmpty)
            {
                container.RemoveItem(stack1.Item.ItemId, stack1.Quantity);
            }
            if (stack2 != null && !stack2.IsEmpty)
            {
                container.RemoveItem(stack2.Item.ItemId, stack2.Quantity);
            }

            // 交换添加（由于 AddItem 会自动填充，我们需要确保先添加到目标槽位）
            // 这里简化处理：先添加 stack2 到 index1 的位置，再添加 stack1 到 index2 的位置
            // 但由于 AddItem 的限制，我们只能尽力而为
            if (tempItem2 != null && tempQuantity2 > 0)
            {
                container.AddItem(tempItem2, tempQuantity2);
            }
            if (tempItem1 != null && tempQuantity1 > 0)
            {
                container.AddItem(tempItem1, tempQuantity1);
            }
        }

        public void ShowWindow()
        {
            if (_isOpen) return;

            Visible = true;
            SetProcessInput(true);
            _isOpen = true;
            GetTree().Paused = true;
        }

        public void HideWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            _isOpen = false;
            GetTree().Paused = false;
            EmitSignal(SignalName.InventoryClosed);
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible) return;

            if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("open_inventory"))
            {
                HideWindow();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}

