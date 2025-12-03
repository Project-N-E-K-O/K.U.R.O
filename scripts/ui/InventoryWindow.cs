using Godot;
using Kuros.Systems.Inventory;
using Kuros.Core;
using Kuros.Managers;
using Kuros.Items.World;

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
        [Export] public ConfirmationDialog DeleteConfirmDialog { get; private set; } = null!;

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

        // 待删除物品状态（用于确认对话框）
        private int _pendingDeleteSlotIndex = -1;
        private bool _pendingDeleteFromInventory = true;
        private InventoryItemStack? _pendingDeleteStack;

        // 窗口状态
        private bool _isOpen = false;
        
        // 玩家引用（用于监听金币变化）
        private SamplePlayer? _player;

        [Signal] public delegate void InventoryClosedEventHandler();

        public override void _Ready()
        {
            base._Ready();
            // 暂停时也要接收输入
            ProcessMode = ProcessModeEnum.Always;
            
            // 添加到组以便其他组件可以通过组查找找到此窗口
            AddToGroup("inventory_window");
            
            CacheNodeReferences();
            InitializeSlots();
            
            // 使用 CallDeferred 确保在 UIManager 设置可见性之后执行
            // 这样可以确保窗口默认是隐藏的
            CallDeferred(MethodName.HideWindow);
        }

        public override void _ExitTree()
        {
            // 清理玩家金币变化信号连接，防止内存泄漏
            DisconnectPlayerGoldSignal();
            base._ExitTree();
        }


        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接按钮信号
        /// 这种方式在导出版本中比 C# 委托方式更可靠
        /// </summary>
        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            InventoryGrid ??= GetNodeOrNull<GridContainer>("MainPanel/Body/InventorySection/InventoryGrid");
            QuickBarContainer ??= GetNodeOrNull<HBoxContainer>("MainPanel/Body/QuickBarSection/QuickBarContainer");
            TrashBin ??= GetNodeOrNull<Control>("MainPanel/Body/TrashBin");
            GoldLabel ??= GetNodeOrNull<Label>("MainPanel/Header/GoldLabel");

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(CloseButton, nameof(HideWindow));

            if (TrashBin != null)
            {
                var guiInputCallable = new Callable(this, nameof(_OnTrashBinGuiInput));
                if (!TrashBin.IsConnected(Control.SignalName.GuiInput, guiInputCallable))
                {
                    TrashBin.Connect(Control.SignalName.GuiInput, guiInputCallable);
                }
            }

            DeleteConfirmDialog ??= GetNodeOrNull<ConfirmationDialog>("DeleteConfirmDialog");
            if (DeleteConfirmDialog != null)
            {
                var confirmedCallable = new Callable(this, nameof(OnDeleteConfirmed));
                if (!DeleteConfirmDialog.IsConnected(ConfirmationDialog.SignalName.Confirmed, confirmedCallable))
                {
                    DeleteConfirmDialog.Connect(ConfirmationDialog.SignalName.Confirmed, confirmedCallable);
                }
                var canceledCallable = new Callable(this, nameof(OnDeleteCanceled));
                if (!DeleteConfirmDialog.IsConnected(ConfirmationDialog.SignalName.Canceled, canceledCallable))
                {
                    DeleteConfirmDialog.Connect(ConfirmationDialog.SignalName.Canceled, canceledCallable);
                }
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
                // 檢查是否嘗試交換到快捷欄1（索引0），這是被鎖定的小木劍槽位
                if (!isInventory && slotIndex == 0)
                {
                    ClearAllSelections();
                    _selectedSlotIndex = -1;
                    return;
                }
                
                // 檢查源槽位是否是快捷欄1（索引0）
                if (!_isSelectedFromInventory && _selectedSlotIndex == 0)
                {
                    ClearAllSelections();
                    _selectedSlotIndex = -1;
                    return;
                }
                
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
                // 如果处于精确换位模式，显示确认对话框
                if (_selectedSlotIndex >= 0)
                {
                    var container = _isSelectedFromInventory ? _inventoryContainer : _quickBarContainer;
                    if (container != null)
                    {
                        var stack = container.GetStack(_selectedSlotIndex);
                        if (stack != null && !stack.IsEmpty)
                        {
                            // 保存待删除信息并显示确认对话框
                            ShowDeleteConfirmDialog(_selectedSlotIndex, _isSelectedFromInventory, stack);
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
            // 檢查是否雙擊快捷欄1（索引0），這是被鎖定的小木劍槽位
            if (!isInventory && slotIndex == 0)
            {
                return;
            }
            
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
            // 檢查是否嘗試拖拽快捷欄1（索引0），這是被鎖定的小木劍槽位
            if (!isInventory && slotIndex == 0)
            {
                return;
            }
            
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
                // 显示确认对话框
                var container = _isDraggingFromInventory ? _inventoryContainer : _quickBarContainer;
                if (container != null && _draggingStack != null && !_draggingStack.IsEmpty)
                {
                    ShowDeleteConfirmDialog(_draggingSlotIndex, _isDraggingFromInventory, _draggingStack);
                }
                DestroyDragPreview();
                _draggingSlotIndex = -1;
                _draggingStack = null;
                return;
            }

            // 检查是否拖拽到界面外（丢弃物品）
            if (!IsPointInMainPanel(position))
            {
                // 嘗試在世界中生成掉落物，如果失敗則顯示確認對話框
                if (_draggingStack != null && !_draggingStack.IsEmpty)
                {
                    TryDropItemToWorld(_draggingSlotIndex, _isDraggingFromInventory, _draggingStack);
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

                // 檢查是否嘗試拖拽到快捷欄1（索引0），這是被鎖定的小木劍槽位
                if (!targetIsInventory && targetIndex == 0)
                {
                    DestroyDragPreview();
                    _draggingSlotIndex = -1;
                    _draggingStack = null;
                    return;
                }

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
                        // 快捷欄內部移動，但跳過快捷欄1
                        if (_draggingSlotIndex == 0 || targetIndex == 0)
                        {
                            DestroyDragPreview();
                            _draggingSlotIndex = -1;
                            _draggingStack = null;
                            return;
                        }
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
            // 保護快捷欄1（索引0）不被更改
            if (!fromInventory && fromIndex == 0)
            {
                return;
            }
            if (!toInventory && toIndex == 0)
            {
                return;
            }
            
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

        /// <summary>
        /// 显示删除确认对话框
        /// </summary>
        private void ShowDeleteConfirmDialog(int slotIndex, bool isFromInventory, InventoryItemStack stack)
        {
            if (DeleteConfirmDialog == null) 
            {
                // 如果没有对话框，直接删除（向后兼容）
                PerformDelete(slotIndex, isFromInventory, stack.Quantity);
                return;
            }

            // 保存待删除信息
            _pendingDeleteSlotIndex = slotIndex;
            _pendingDeleteFromInventory = isFromInventory;
            _pendingDeleteStack = stack;

            // 更新对话框文本，显示物品名称和数量
            string itemInfo = stack.Quantity > 1 
                ? $"{stack.Item.DisplayName} x{stack.Quantity}" 
                : stack.Item.DisplayName;
            DeleteConfirmDialog.DialogText = $"确定要删除 [{itemInfo}] 吗？\n此操作无法撤销。";

            // 显示对话框
            DeleteConfirmDialog.PopupCentered();
        }

        /// <summary>
        /// 确认删除回调
        /// </summary>
        private void OnDeleteConfirmed()
        {
            if (_pendingDeleteSlotIndex >= 0 && _pendingDeleteStack != null)
            {
                PerformDelete(_pendingDeleteSlotIndex, _pendingDeleteFromInventory, _pendingDeleteStack.Quantity);
            }
            ClearPendingDelete();
        }

        /// <summary>
        /// 取消删除回调
        /// </summary>
        private void OnDeleteCanceled()
        {
            ClearPendingDelete();
        }

        /// <summary>
        /// 执行删除操作
        /// </summary>
        private void PerformDelete(int slotIndex, bool isFromInventory, int quantity)
        {
            var container = isFromInventory ? _inventoryContainer : _quickBarContainer;
            if (container != null)
            {
                container.RemoveItemFromSlot(slotIndex, quantity);
                GD.Print($"已删除物品，槽位: {slotIndex}, 数量: {quantity}");
            }
        }

        /// <summary>
        /// 清除待删除状态
        /// </summary>
        private void ClearPendingDelete()
        {
            _pendingDeleteSlotIndex = -1;
            _pendingDeleteStack = null;
        }

        /// <summary>
        /// 在玩家位置附近生成世界掉落物
        /// </summary>
        /// <param name="stack">要掉落的物品堆疊</param>
        /// <returns>如果生成成功返回 true，否則返回 false</returns>
        private bool SpawnWorldDropAtPlayer(InventoryItemStack stack)
        {
            if (stack == null || stack.IsEmpty)
            {
                return false;
            }

            // 獲取玩家位置
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (player == null)
            {
                GD.PrintErr("無法找到玩家，無法生成世界掉落物");
                return false;
            }

            // 在玩家前方稍微偏移的位置生成掉落物
            var dropPosition = player.GlobalPosition + new Vector2(50, 0);

            // 使用 WorldItemSpawner 生成掉落物
            var entity = WorldItemSpawner.SpawnFromStack(this, stack, dropPosition);
            if (entity != null)
            {
                // 給掉落物一個隨機的拋出速度
                var random = new RandomNumberGenerator();
                random.Randomize();
                var throwVelocity = new Vector2(
                    random.RandfRange(-100, 100),
                    random.RandfRange(-150, -50)
                );
                entity.ApplyThrowImpulse(throwVelocity);
                
                GD.Print($"已丟棄物品至世界: {stack.Item.DisplayName} x{stack.Quantity}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 嘗試丟棄物品到世界，如果失敗則顯示確認對話框
        /// </summary>
        private void TryDropItemToWorld(int slotIndex, bool isFromInventory, InventoryItemStack stack)
        {
            if (stack == null || stack.IsEmpty)
            {
                return;
            }

            var container = isFromInventory ? _inventoryContainer : _quickBarContainer;
            if (container == null)
            {
                return;
            }

            // 嘗試在世界中生成掉落物
            if (SpawnWorldDropAtPlayer(stack))
            {
                // 生成成功，從背包中移除物品
                container.RemoveItemFromSlot(slotIndex, stack.Quantity);
            }
            else
            {
                // 生成失敗（例如沒有定義世界場景），顯示確認對話框讓玩家決定是否永久刪除
                GD.Print($"無法生成世界掉落物，顯示刪除確認對話框");
                ShowDeleteConfirmDialog(slotIndex, isFromInventory, stack);
            }
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
            
            // 请求暂停游戏
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PushPause();
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
            DisconnectPlayerGoldSignal();
            
            // 获取玩家引用
            _player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
            
            // 连接信号
            if (_player != null)
            {
                _player.GoldChanged += OnPlayerGoldChanged;
            }
        }

        /// <summary>
        /// 断开玩家金币变化信号连接，防止内存泄漏
        /// </summary>
        private void DisconnectPlayerGoldSignal()
        {
            if (_player != null)
            {
                // 直接取消订阅（C# 中取消订阅不存在的处理器是安全的）
                _player.GoldChanged -= OnPlayerGoldChanged;
                _player = null;
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


        public void HideWindow()
        {
            if (!_isOpen && !Visible)
            {
                // 如果已经关闭且不可见，直接返回
                return;
            }

            // 清理拖拽状态
            DestroyDragPreview();
            _draggingSlotIndex = -1;
            _draggingStack = null;
            ClearAllSelections();
            _selectedSlotIndex = -1;
            
            // 清理待删除状态
            ClearPendingDelete();
            if (DeleteConfirmDialog != null && DeleteConfirmDialog.Visible)
            {
                DeleteConfirmDialog.Hide();
            }
            
            // 断开玩家金币变化信号
            DisconnectPlayerGoldSignal();

            Visible = false;
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            _isOpen = false;
            
            // 取消暂停请求
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PopPause();
            }
            
            EmitSignal(SignalName.InventoryClosed);
        }

        /// <summary>
        /// 检查输入事件是否为 ESC 键（通过 action "ui_cancel" 或 Key.Escape）
        /// </summary>
        private bool IsEscEvent(InputEvent @event)
        {
            if (@event.IsActionPressed("ui_cancel"))
            {
                return true;
            }
            
            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                // 直接检查ESC键的keycode（备用方法）
                if (keyEvent.Keycode == Key.Escape)
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 检查是否应该处理 ESC 键（物品栏打开且没有 ItemObtainedPopup 激活）
        /// </summary>
        private bool ShouldHandleEsc()
        {
            // 检查物品栏是否打开
            if (!Visible || !_isOpen)
            {
                return false;
            }

            // 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                // 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
                return false;
            }

            return true;
        }

        public override void _Input(InputEvent @event)
        {
            // 检查是否应该处理 ESC（包括弹窗检查）
            if (!ShouldHandleEsc())
            {
                return;
            }

            // 检查是否为 ESC 事件
            if (IsEscEvent(@event))
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
            // 检查是否应该处理 ESC（包括弹窗检查）
            if (!ShouldHandleEsc()) return;

            // 检查是否为 ESC 事件
            if (IsEscEvent(@event))
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
            
            // 检查是否为 ESC 事件，需要同时检查弹窗状态
            if (ShouldHandleEsc() && IsEscEvent(@event))
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
                    // 点击界面外，嘗試丟棄選中的物品到世界
                    var container = _isSelectedFromInventory ? _inventoryContainer : _quickBarContainer;
                    if (container != null)
                    {
                        var stack = container.GetStack(_selectedSlotIndex);
                        if (stack != null && !stack.IsEmpty)
                        {
                            TryDropItemToWorld(_selectedSlotIndex, _isSelectedFromInventory, stack);
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

