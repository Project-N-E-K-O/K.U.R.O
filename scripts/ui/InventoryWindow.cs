using Godot;
using Kuros.Systems.Inventory;
using Kuros.Core;

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
        [Export] public Control TrashBin { get; private set; } = null!;
        [Export] public Label GoldLabel { get; private set; } = null!;

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
        private Control? _dragPreview; // 拖拽时的预览控件
        private InventoryItemStack? _draggingStack; // 正在拖拽的物品堆叠

        // 精确换位状态
        private int _selectedSlotIndex = -1;
        private bool _isSelectedFromInventory = true;

        // 窗口状态
        private bool _isOpen = false;
        private bool _shouldBePaused = false; // 标记是否应该处于暂停状态
        
        // 玩家引用（用于监听金币变化）
        private SamplePlayer? _player;

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

        public override void _Process(double delta)
        {
            // 如果物品栏应该打开，确保游戏处于暂停状态
            if (_isOpen && _shouldBePaused)
            {
                var tree = GetTree();
                if (tree != null && !tree.Paused)
                {
                    GD.PrintErr("InventoryWindow._Process: 检测到暂停状态被意外取消，重新设置暂停");
                    tree.Paused = true;
                }
            }
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            InventoryGrid ??= GetNodeOrNull<GridContainer>("MainPanel/Body/InventorySection/InventoryGrid");
            QuickBarContainer ??= GetNodeOrNull<HBoxContainer>("MainPanel/Body/QuickBarSection/QuickBarContainer");
            TrashBin ??= GetNodeOrNull<Control>("MainPanel/Body/TrashBin");
            GoldLabel ??= GetNodeOrNull<Label>("MainPanel/Header/GoldLabel");

            if (CloseButton != null)
            {
                CloseButton.Pressed += HideWindow;
            }

            if (TrashBin != null)
            {
                TrashBin.GuiInput += _OnTrashBinGuiInput;
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
            slot.SlotDragUpdate += (slotIdx, pos) => OnSlotDragUpdate(slotIdx, pos);

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

        private void _OnTrashBinGuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseEvent && 
                mouseEvent.ButtonIndex == MouseButton.Left && 
                mouseEvent.Pressed)
            {
                // 如果处于精确换位模式，销毁选中的物品
                if (_selectedSlotIndex >= 0)
                {
                    var container = _isSelectedFromInventory ? _inventoryContainer : _quickBarContainer;
                    if (container != null)
                    {
                        var stack = container.GetStack(_selectedSlotIndex);
                        if (stack != null && !stack.IsEmpty)
                        {
                            container.RemoveItemFromSlot(_selectedSlotIndex, stack.Quantity);
                        }
                    }
                    
                    ClearAllSelections();
                    _selectedSlotIndex = -1;
                    GetViewport().SetInputAsHandled();
                }
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
                _inventorySlots[slotIndex]?.SetSelected(true);
            }
            else if (!isInventory && slotIndex >= 0 && slotIndex < _quickBarSlots.Length)
            {
                _quickBarSlots[slotIndex]?.SetSelected(true);
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

            // 获取正在拖拽的物品
            var container = isInventory ? _inventoryContainer : _quickBarContainer;
            if (container != null)
            {
                _draggingStack = container.GetStack(slotIndex);
                if (_draggingStack != null && !_draggingStack.IsEmpty)
                {
                    CreateDragPreview(_draggingStack, position);
                }
            }
        }

        private void OnSlotDragUpdate(int slotIndex, Vector2 position)
        {
            if (_dragPreview != null)
            {
                _dragPreview.GlobalPosition = position - new Vector2(40, 40); // 居中显示
            }
        }

        private void CreateDragPreview(InventoryItemStack stack, Vector2 position)
        {
            // 创建拖拽预览控件
            _dragPreview = new Panel
            {
                Size = new Vector2(80, 80),
                GlobalPosition = position - new Vector2(40, 40),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };

            var label = new Label
            {
                Text = stack.Item.DisplayName + (stack.Quantity > 1 ? $" x{stack.Quantity}" : ""),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(80, 80)
            };
            _dragPreview.AddChild(label);

            // 添加到场景树
            AddChild(_dragPreview);
            _dragPreview.SetAsTopLevel(true);
        }

        private void DestroyDragPreview()
        {
            if (_dragPreview != null)
            {
                _dragPreview.QueueFree();
                _dragPreview = null;
            }
        }

        private void OnSlotDragEnded(int slotIndex, Vector2 position, bool isInventory)
        {
            if (_draggingSlotIndex < 0)
            {
                DestroyDragPreview();
                return;
            }

            // 检查是否拖拽到垃圾桶
            if (TrashBin != null && IsPointInControl(TrashBin, position))
            {
                // 销毁物品
                var container = _isDraggingFromInventory ? _inventoryContainer : _quickBarContainer;
                if (container != null && _draggingStack != null)
                {
                    container.RemoveItemFromSlot(_draggingSlotIndex, _draggingStack.Quantity);
                }
                DestroyDragPreview();
                _draggingSlotIndex = -1;
                _draggingStack = null;
                return;
            }

            // 检查是否拖拽到界面外（丢弃物品）
            if (!IsPointInMainPanel(position))
            {
                // 丢弃物品（这里可以扩展为在世界中生成掉落物）
                var container = _isDraggingFromInventory ? _inventoryContainer : _quickBarContainer;
                if (container != null && _draggingStack != null)
                {
                    // TODO: 在世界中生成掉落物
                    container.RemoveItemFromSlot(_draggingSlotIndex, _draggingStack.Quantity);
                    GD.Print($"丢弃物品: {_draggingStack.Item.DisplayName} x{_draggingStack.Quantity}");
                }
                DestroyDragPreview();
                _draggingSlotIndex = -1;
                _draggingStack = null;
                return;
            }

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

            DestroyDragPreview();
            _draggingSlotIndex = -1;
            _draggingStack = null;
        }

        private bool IsPointInControl(Control control, Vector2 globalPosition)
        {
            var rect = new Rect2(control.GlobalPosition, control.Size);
            return rect.HasPoint(globalPosition);
        }

        private bool IsPointInMainPanel(Vector2 globalPosition)
        {
            var mainPanel = GetNodeOrNull<Control>("MainPanel");
            if (mainPanel == null) return false;
            var rect = new Rect2(mainPanel.GlobalPosition, mainPanel.Size);
            return rect.HasPoint(globalPosition);
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
                // 创建新的堆叠副本
                var newStack = new InventoryItemStack(fromStack.Item, fromStack.Quantity);
                
                // 清空源槽位
                fromContainer.SetStack(fromIndex, null);
                
                // 设置目标槽位
                toContainer.SetStack(toIndex, newStack);
            }
            else
            {
                // 交换两个槽位的内容
                var tempStack = new InventoryItemStack(fromStack.Item, fromStack.Quantity);
                var toStackCopy = new InventoryItemStack(toStack.Item, toStack.Quantity);

                // 交换设置
                fromContainer.SetStack(fromIndex, toStackCopy);
                toContainer.SetStack(toIndex, tempStack);
            }
        }

        private void SwapSlotsInContainer(InventoryContainer? container, int index1, int index2)
        {
            if (container == null || index1 == index2) return;

            var stack1 = container.GetStack(index1);
            var stack2 = container.GetStack(index2);

            // 如果两个槽位都为空或相同，直接返回
            if ((stack1 == null || stack1.IsEmpty) && (stack2 == null || stack2.IsEmpty)) return;

            // 创建副本进行交换
            InventoryItemStack? stack1Copy = null;
            InventoryItemStack? stack2Copy = null;

            if (stack1 != null && !stack1.IsEmpty)
            {
                stack1Copy = new InventoryItemStack(stack1.Item, stack1.Quantity);
            }
            if (stack2 != null && !stack2.IsEmpty)
            {
                stack2Copy = new InventoryItemStack(stack2.Item, stack2.Quantity);
            }

            // 交换设置
            container.SetStack(index1, stack2Copy);
            container.SetStack(index2, stack1Copy);
        }

        public void ShowWindow()
        {
            if (_isOpen) return;

            Visible = true;
            ProcessMode = ProcessModeEnum.Always; // 确保暂停时也能接收输入
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            _isOpen = true;
            
            // 连接玩家金币变化信号
            ConnectPlayerGoldSignal();
            
            // 更新金币显示
            UpdateGoldDisplay();
            
            // 设置暂停，确保游戏时间停止
            var tree = GetTree();
            if (tree != null)
            {
                _shouldBePaused = true;
                tree.Paused = true;
                
                // 延迟验证暂停状态，确保暂停生效
                CallDeferred(MethodName.VerifyPauseState);
            }
            
            // 尝试将窗口移到父节点的最后，确保输入处理优先级（后调用的 _Input 会先处理）
            var parent = GetParent();
            if (parent != null)
            {
                parent.MoveChild(this, parent.GetChildCount() - 1);
            }
        }
        
        /// <summary>
        /// 连接玩家金币变化信号
        /// </summary>
        private void ConnectPlayerGoldSignal()
        {
            // 断开之前的连接
            if (_player != null && _player.IsConnected(SamplePlayer.SignalName.GoldChanged, new Callable(this, MethodName.OnPlayerGoldChanged)))
            {
                _player.GoldChanged -= OnPlayerGoldChanged;
            }
            
            // 获取玩家引用
            _player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
            
            // 连接信号
            if (_player != null)
            {
                _player.GoldChanged += OnPlayerGoldChanged;
            }
        }
        
        /// <summary>
        /// 玩家金币变化回调
        /// </summary>
        private void OnPlayerGoldChanged(int gold)
        {
            UpdateGoldDisplay();
        }
        
        /// <summary>
        /// 更新金币显示
        /// </summary>
        private void UpdateGoldDisplay()
        {
            // 尝试从场景中获取玩家
            var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
            if (player != null && GoldLabel != null)
            {
                int gold = player.GetGold();
                GoldLabel.Text = $"金币: {gold}";
            }
        }

        private void VerifyPauseState()
        {
            var tree = GetTree();
            if (tree != null && _isOpen)
            {
                if (!tree.Paused)
                {
                    GD.PrintErr("InventoryWindow.VerifyPauseState: 警告 - 暂停状态被意外取消，重新设置暂停");
                    tree.Paused = true;
                }
            }
        }

        public void HideWindow()
        {
            if (!_isOpen) return;

            // 清理拖拽状态
            DestroyDragPreview();
            _draggingSlotIndex = -1;
            _draggingStack = null;
            ClearAllSelections();
            _selectedSlotIndex = -1;
            
            // 断开玩家金币变化信号
            if (_player != null && _player.IsConnected(SamplePlayer.SignalName.GoldChanged, new Callable(this, MethodName.OnPlayerGoldChanged)))
            {
                _player.GoldChanged -= OnPlayerGoldChanged;
            }
            _player = null;

            Visible = false;
            SetProcessInput(false);
            _isOpen = false;
            
            // 取消暂停，恢复游戏时间
            _shouldBePaused = false;
            var tree = GetTree();
            if (tree != null)
            {
                tree.Paused = false;
            }
            
            EmitSignal(SignalName.InventoryClosed);
        }

        public override void _Input(InputEvent @event)
        {
            // 检查物品栏是否打开
            if (!Visible || !_isOpen) return;

            // 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                // 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
                // 直接返回，让弹窗处理（禁用）
                return;
            }

            // 在物品栏打开时，ESC键关闭物品栏
            // 同时检查action和keycode，确保能捕获ESC键
            bool isEscKey = false;
            
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                // 直接检查ESC键的keycode（备用方法）
                if (keyEvent.Keycode == Key.Escape)
                {
                    isEscKey = true;
                }
            }

            if (isEscKey)
            {
                HideWindow();
                GetViewport().SetInputAsHandled();
                AcceptEvent(); // 确保事件被接受，防止其他系统处理
                return;
            }

            // 处理 M 键（open_inventory）关闭物品栏
            if (@event.IsActionPressed("open_inventory"))
            {
                HideWindow();
                GetViewport().SetInputAsHandled();
                AcceptEvent(); // 确保事件被接受，防止其他系统处理
                return;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            // 检查物品栏是否打开
            if (!Visible || !_isOpen) return;

            // 在物品栏打开时，ESC键关闭物品栏（GUI输入备用处理）
            bool isEscKey = false;
            
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                // 直接检查ESC键的keycode（备用方法）
                if (keyEvent.Keycode == Key.Escape)
                {
                    isEscKey = true;
                }
            }

            if (isEscKey)
            {
                HideWindow();
                AcceptEvent();
                return;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // 检查物品栏是否打开
            if (!Visible || !_isOpen) return;
            
            // 在物品栏打开时，ESC键关闭物品栏（未处理输入的备用处理）
            bool isEscKey = false;
            
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                // 直接检查ESC键的keycode（备用方法）
                if (keyEvent.Keycode == Key.Escape)
                {
                    isEscKey = true;
                }
            }

            if (isEscKey)
            {
                HideWindow();
                GetViewport().SetInputAsHandled();
                return;
            }
            
            // 如果处于精确换位模式，点击界面外取消选择
            if (_selectedSlotIndex >= 0 && @event is InputEventMouseButton mouseEvent && 
                mouseEvent.ButtonIndex == MouseButton.Left && mouseEvent.Pressed)
            {
                var globalPos = GetGlobalMousePosition();
                if (!IsPointInMainPanel(globalPos))
                {
                    // 点击界面外，丢弃选中的物品
                    var container = _isSelectedFromInventory ? _inventoryContainer : _quickBarContainer;
                    if (container != null)
                    {
                        var stack = container.GetStack(_selectedSlotIndex);
                        if (stack != null && !stack.IsEmpty)
                        {
                            // TODO: 在世界中生成掉落物
                            container.RemoveItemFromSlot(_selectedSlotIndex, stack.Quantity);
                        }
                    }
                    
                    ClearAllSelections();
                    _selectedSlotIndex = -1;
                    GetViewport().SetInputAsHandled();
                }
            }
        }
    }
}

