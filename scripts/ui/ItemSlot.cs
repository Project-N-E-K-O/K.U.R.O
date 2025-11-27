using Godot;
using Kuros.Systems.Inventory;

namespace Kuros.UI
{
    /// <summary>
    /// 物品槽位控件，支持显示物品、拖拽和双击选择
    /// </summary>
    public partial class ItemSlot : Panel
    {
        [Export] public TextureRect ItemIcon { get; private set; } = null!;
        [Export] public Label QuantityLabel { get; private set; } = null!;
        [Export] public Label SlotLabel { get; private set; } = null!;

        public int SlotIndex { get; set; } = -1;
        public InventoryItemStack? ItemStack { get; private set; }

        private bool _isDragging = false;
        private bool _isSelected = false; // 用于双击选择
        private double _lastClickTime = 0;
        private const double DoubleClickTime = 0.3; // 双击时间间隔（秒）

        public void ClearSelection()
        {
            _isSelected = false;
            QueueRedraw();
        }

        [Signal] public delegate void SlotClickedEventHandler(int slotIndex);
        [Signal] public delegate void SlotDragStartedEventHandler(int slotIndex, Vector2 position);
        [Signal] public delegate void SlotDragEndedEventHandler(int slotIndex, Vector2 position);
        [Signal] public delegate void SlotDoubleClickedEventHandler(int slotIndex);

        public override void _Ready()
        {
            base._Ready();
            CacheNodeReferences();
            UpdateDisplay();
        }

        private void CacheNodeReferences()
        {
            ItemIcon ??= GetNodeOrNull<TextureRect>("ItemIcon");
            QuantityLabel ??= GetNodeOrNull<Label>("QuantityLabel");
            SlotLabel ??= GetNodeOrNull<Label>("SlotLabel");
        }

        public void SetItemStack(InventoryItemStack? stack)
        {
            ItemStack = stack;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (ItemStack == null || ItemStack.IsEmpty)
            {
                if (ItemIcon != null)
                {
                    ItemIcon.Texture = null;
                    ItemIcon.Visible = false;
                }
                if (QuantityLabel != null)
                {
                    QuantityLabel.Text = string.Empty;
                    QuantityLabel.Visible = false;
                }
                if (SlotLabel != null)
                {
                    SlotLabel.Visible = true;
                }
            }
            else
            {
                if (ItemIcon != null)
                {
                    ItemIcon.Texture = ItemStack.Item.Icon;
                    ItemIcon.Visible = ItemStack.Item.Icon != null;
                }
                if (QuantityLabel != null)
                {
                    QuantityLabel.Text = ItemStack.Quantity > 1 ? ItemStack.Quantity.ToString() : string.Empty;
                    QuantityLabel.Visible = ItemStack.Quantity > 1;
                }
                if (SlotLabel != null)
                {
                    SlotLabel.Visible = false;
                }
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left)
            {
                if (mouseEvent.Pressed)
                {
                    double currentTime = Time.GetTicksMsec() / 1000.0;
                    
                    // 检测双击
                    if (currentTime - _lastClickTime < DoubleClickTime && _lastClickTime > 0)
                    {
                        _isSelected = true;
                        EmitSignal(SignalName.SlotDoubleClicked, SlotIndex);
                        QueueRedraw();
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                    else
                    {
                        _isSelected = false;
                        _lastClickTime = currentTime;
                        QueueRedraw();
                    }

                    // 开始拖拽
                    if (ItemStack != null && !ItemStack.IsEmpty)
                    {
                        _isDragging = true;
                        EmitSignal(SignalName.SlotDragStarted, SlotIndex, GetGlobalMousePosition());
                        GetViewport().SetInputAsHandled();
                    }
                    else
                    {
                        // 如果槽位为空，但处于选择状态，则触发点击事件（用于精确换位）
                        // 注意：这里的 _isSelected 是之前双击设置的，不是当前点击
                        EmitSignal(SignalName.SlotClicked, SlotIndex);
                        GetViewport().SetInputAsHandled();
                    }
                }
                else if (_isDragging)
                {
                    // 结束拖拽
                    _isDragging = false;
                    EmitSignal(SignalName.SlotDragEnded, SlotIndex, GetGlobalMousePosition());
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        public override void _Draw()
        {
            base._Draw();
            
            // 绘制选中状态
            if (_isSelected)
            {
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 0, 0.3f));
            }
        }
    }
}

