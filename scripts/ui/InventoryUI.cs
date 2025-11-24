using Godot;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 物品栏UI - 显示和管理物品
    /// </summary>
    public partial class InventoryUI : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button ToggleButton { get; private set; } = null!;
        [Export] public Control InventoryPanel { get; private set; } = null!;
        [Export] public GridContainer ItemGrid { get; private set; } = null!;
        [Export] public Label TitleLabel { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public int SlotsPerRow = 5;
        [Export] public int TotalSlots = 20;

        // 信号
        [Signal] public delegate void ItemSelectedEventHandler(int slotIndex);
        [Signal] public delegate void InventoryToggledEventHandler(bool isOpen);

        private bool _isOpen = false;
        private List<InventorySlot> _slots = new List<InventorySlot>();

        public override void _Ready()
        {
            if (ToggleButton == null)
            {
                ToggleButton = GetNodeOrNull<Button>("ToggleButton");
            }

            if (InventoryPanel == null)
            {
                InventoryPanel = GetNodeOrNull<Control>("InventoryPanel");
            }

            if (ItemGrid == null)
            {
                ItemGrid = GetNodeOrNull<GridContainer>("InventoryPanel/ItemGrid");
            }

            if (TitleLabel == null)
            {
                TitleLabel = GetNodeOrNull<Label>("InventoryPanel/TitleLabel");
            }

            if (ItemGrid != null)
            {
                ItemGrid.Columns = SlotsPerRow;
            }

            if (ToggleButton != null)
            {
                ToggleButton.Pressed += ToggleInventory;
            }

            // 初始状态：隐藏
            SetInventoryVisible(false);
            CreateSlots();
        }

        private void CreateSlots()
        {
            if (ItemGrid == null) return;

            // 清除现有槽位
            foreach (var slot in _slots)
            {
                if (slot != null && IsInstanceValid(slot))
                {
                    slot.QueueFree();
                }
            }
            _slots.Clear();

            // 创建新的槽位
            for (int i = 0; i < TotalSlots; i++)
            {
                var slot = new InventorySlot();
                slot.Initialize(i);
                slot.SlotSelected += OnSlotSelected;
                ItemGrid.AddChild(slot);
                _slots.Add(slot);
            }
        }

        private void OnSlotSelected(int slotIndex)
        {
            EmitSignal(SignalName.ItemSelected, slotIndex);
        }

        /// <summary>
        /// 切换物品栏显示/隐藏
        /// </summary>
        public void ToggleInventory()
        {
            SetInventoryVisible(!_isOpen);
        }

        /// <summary>
        /// 设置物品栏可见性
        /// </summary>
        public void SetInventoryVisible(bool visible)
        {
            _isOpen = visible;
            if (InventoryPanel != null)
            {
                InventoryPanel.Visible = visible;
            }
            EmitSignal(SignalName.InventoryToggled, _isOpen);
        }

        public bool IsOpen => _isOpen;
    }

    /// <summary>
    /// 物品槽位
    /// </summary>
    public partial class InventorySlot : Control
    {
        [Signal] public delegate void SlotSelectedEventHandler(int slotIndex);

        private int _slotIndex;
        private Button _slotButton = null!;
        private TextureRect _itemIcon = null!;
        private Label _itemCountLabel = null!;

        public void Initialize(int slotIndex)
        {
            _slotIndex = slotIndex;
            SetupUI();
        }

        private void SetupUI()
        {
            CustomMinimumSize = new Vector2(64, 64);

            _slotButton = new Button();
            _slotButton.Name = "SlotButton";
            _slotButton.Flat = true;
            AddChild(_slotButton);
            _slotButton.AnchorLeft = 0.0f;
            _slotButton.AnchorTop = 0.0f;
            _slotButton.AnchorRight = 1.0f;
            _slotButton.AnchorBottom = 1.0f;
            _slotButton.OffsetLeft = 0;
            _slotButton.OffsetTop = 0;
            _slotButton.OffsetRight = 0;
            _slotButton.OffsetBottom = 0;

            _itemIcon = new TextureRect();
            _itemIcon.Name = "ItemIcon";
            _itemIcon.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            _slotButton.AddChild(_itemIcon);
            _itemIcon.AnchorLeft = 0.0f;
            _itemIcon.AnchorTop = 0.0f;
            _itemIcon.AnchorRight = 1.0f;
            _itemIcon.AnchorBottom = 1.0f;
            _itemIcon.OffsetLeft = 4;
            _itemIcon.OffsetTop = 4;
            _itemIcon.OffsetRight = -4;
            _itemIcon.OffsetBottom = -20;
            _itemIcon.Visible = false;

            _itemCountLabel = new Label();
            _itemCountLabel.Name = "ItemCount";
            _itemCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _itemCountLabel.VerticalAlignment = VerticalAlignment.Bottom;
            _slotButton.AddChild(_itemCountLabel);
            _itemCountLabel.AnchorLeft = 0.0f;
            _itemCountLabel.AnchorTop = 0.0f;
            _itemCountLabel.AnchorRight = 1.0f;
            _itemCountLabel.AnchorBottom = 1.0f;
            _itemCountLabel.OffsetLeft = 4;
            _itemCountLabel.OffsetTop = -20;
            _itemCountLabel.OffsetRight = -4;
            _itemCountLabel.OffsetBottom = -4;
            _itemCountLabel.Visible = false;

            _slotButton.Pressed += () => EmitSignal(SignalName.SlotSelected, _slotIndex);
        }

        public void SetItem(Texture2D? icon, int count = 1)
        {
            if (icon != null)
            {
                _itemIcon.Texture = icon;
                _itemIcon.Visible = true;
                if (count > 1)
                {
                    _itemCountLabel.Text = count.ToString();
                    _itemCountLabel.Visible = true;
                }
                else
                {
                    _itemCountLabel.Visible = false;
                }
            }
            else
            {
                _itemIcon.Visible = false;
                _itemCountLabel.Visible = false;
            }
        }
    }
}

