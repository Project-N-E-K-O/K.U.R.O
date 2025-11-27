using Godot;
using System.Collections.Generic;
using Kuros.Utils;

namespace Kuros.UI
{
    /// <summary>
    /// 存档选择界面 - 软排样式（卡片式布局）
    /// </summary>
    public partial class SaveSlotSelection : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public ScrollContainer ScrollContainer { get; private set; } = null!;
        [Export] public GridContainer SlotGrid { get; private set; } = null!;
        [Export] public PackedScene SaveSlotCardScene { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public int SlotsPerRow = 3;
        [Export] public int TotalSlots = 9;

        // 信号
        [Signal] public delegate void SlotSelectedEventHandler(int slotIndex);
        [Signal] public delegate void BackRequestedEventHandler();

        private List<SaveSlotCard> _slotCards = new List<SaveSlotCard>();

        public override void _Ready()
        {
            // 自动查找节点
            if (BackButton == null)
            {
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            }

            if (ScrollContainer == null)
            {
                ScrollContainer = GetNodeOrNull<ScrollContainer>("MenuPanel/VBoxContainer/ScrollContainer");
            }

            if (SlotGrid == null)
            {
                SlotGrid = GetNodeOrNull<GridContainer>("MenuPanel/VBoxContainer/ScrollContainer/SlotGrid");
            }

            // 设置网格列数
            if (SlotGrid != null)
            {
                SlotGrid.Columns = SlotsPerRow;
            }

            // 连接返回按钮
            if (BackButton != null)
            {
                BackButton.Pressed += OnBackPressed;
            }

            // 创建存档槽位
            CreateSaveSlots();
        }

        private void CreateSaveSlots()
        {
            if (SlotGrid == null) return;

            // 清除现有槽位
            foreach (var card in _slotCards)
            {
                if (card != null && IsInstanceValid(card))
                {
                    card.QueueFree();
                }
            }
            _slotCards.Clear();

            // 创建新的存档槽位卡片
            for (int i = 0; i < TotalSlots; i++)
            {
                var slotCard = CreateSaveSlotCard(i);
                if (slotCard != null)
                {
                    SlotGrid.AddChild(slotCard);
                    _slotCards.Add(slotCard);
                }
            }
        }

        private SaveSlotCard CreateSaveSlotCard(int slotIndex)
        {
            // 如果提供了场景，使用场景实例化
            if (SaveSlotCardScene != null)
            {
                var card = SaveSlotCardScene.Instantiate<SaveSlotCard>();
                if (card != null)
                {
                    card.Initialize(slotIndex, GetSaveSlotData(slotIndex));
                    card.SlotSelected += OnSlotCardSelected;
                    return card;
                }
            }

            // 否则创建默认卡片
            var defaultCard = new SaveSlotCard();
            defaultCard.Initialize(slotIndex, GetSaveSlotData(slotIndex));
            defaultCard.SlotSelected += OnSlotCardSelected;
            return defaultCard;
        }

        private SaveSlotData GetSaveSlotData(int slotIndex)
        {
            // 这里应该从文件系统加载实际的存档数据
            // 现在返回示例数据
            return new SaveSlotData
            {
                SlotIndex = slotIndex,
                HasSave = false, // 实际应该检查文件是否存在
                SaveName = $"存档 {slotIndex + 1}",
                SaveTime = "",
                PlayTime = "00:00:00",
                Level = 1,
                Thumbnail = null
            };
        }

        private void OnSlotCardSelected(int slotIndex)
        {
            EmitSignal(SignalName.SlotSelected, slotIndex);
            GameLogger.Info(nameof(SaveSlotSelection), $"选择了存档槽位: {slotIndex}");
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }

        /// <summary>
        /// 刷新存档列表
        /// </summary>
        public void RefreshSlots()
        {
            CreateSaveSlots();
        }
    }

    /// <summary>
    /// 存档槽位卡片 - 单个存档槽位的UI
    /// </summary>
    public partial class SaveSlotCard : Control
    {
        public SaveSlotCard()
        {
            Name = "SaveSlotCard";
        }

        [Signal] public delegate void SlotSelectedEventHandler(int slotIndex);

        private int _slotIndex;
        private Button _cardButton = null!;
        private Label _slotNameLabel = null!;
        private Label _saveTimeLabel = null!;
        private Label _playTimeLabel = null!;
        private TextureRect _thumbnailRect = null!;
        private Label _emptyLabel = null!;

        public void Initialize(int slotIndex, SaveSlotData data)
        {
            _slotIndex = slotIndex;

            // 创建UI结构
            SetupUI();

            // 更新显示
            UpdateDisplay(data);
        }

        private void SetupUI()
        {
            // 设置大小
            CustomMinimumSize = new Vector2(200, 250);

            // 创建主按钮
            _cardButton = new Button();
            _cardButton.Name = "CardButton";
            _cardButton.Flat = true;
            _cardButton.ExpandIcon = true;
            AddChild(_cardButton);
            // 设置按钮填充整个父容器
            _cardButton.AnchorLeft = 0.0f;
            _cardButton.AnchorTop = 0.0f;
            _cardButton.AnchorRight = 1.0f;
            _cardButton.AnchorBottom = 1.0f;
            _cardButton.OffsetLeft = 0;
            _cardButton.OffsetTop = 0;
            _cardButton.OffsetRight = 0;
            _cardButton.OffsetBottom = 0;

            // 创建容器
            var vbox = new VBoxContainer();
            vbox.Name = "VBox";
            _cardButton.AddChild(vbox);
            // 设置容器填充整个按钮
            vbox.AnchorLeft = 0.0f;
            vbox.AnchorTop = 0.0f;
            vbox.AnchorRight = 1.0f;
            vbox.AnchorBottom = 1.0f;
            vbox.OffsetLeft = 0;
            vbox.OffsetTop = 0;
            vbox.OffsetRight = 0;
            vbox.OffsetBottom = 0;

            // 缩略图
            _thumbnailRect = new TextureRect();
            _thumbnailRect.Name = "Thumbnail";
            _thumbnailRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            _thumbnailRect.CustomMinimumSize = new Vector2(0, 120);
            vbox.AddChild(_thumbnailRect);

            // 存档名称
            _slotNameLabel = new Label();
            _slotNameLabel.Name = "SlotName";
            _slotNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _slotNameLabel.AddThemeFontSizeOverride("font_size", 18);
            vbox.AddChild(_slotNameLabel);

            // 保存时间
            _saveTimeLabel = new Label();
            _saveTimeLabel.Name = "SaveTime";
            _saveTimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _saveTimeLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(_saveTimeLabel);

            // 游戏时间
            _playTimeLabel = new Label();
            _playTimeLabel.Name = "PlayTime";
            _playTimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _playTimeLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(_playTimeLabel);

            // 空存档标签
            _emptyLabel = new Label();
            _emptyLabel.Name = "EmptyLabel";
            _emptyLabel.Text = "空存档";
            _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyLabel.VerticalAlignment = VerticalAlignment.Center;
            _emptyLabel.AddThemeFontSizeOverride("font_size", 24);
            AddChild(_emptyLabel);
            // 设置空标签填充整个父容器
            _emptyLabel.AnchorLeft = 0.0f;
            _emptyLabel.AnchorTop = 0.0f;
            _emptyLabel.AnchorRight = 1.0f;
            _emptyLabel.AnchorBottom = 1.0f;
            _emptyLabel.OffsetLeft = 0;
            _emptyLabel.OffsetTop = 0;
            _emptyLabel.OffsetRight = 0;
            _emptyLabel.OffsetBottom = 0;

            // 连接按钮信号
            _cardButton.Pressed += () => EmitSignal(SignalName.SlotSelected, _slotIndex);
        }

        private void UpdateDisplay(SaveSlotData data)
        {
            if (data.HasSave)
            {
                // 显示存档信息
                _emptyLabel.Visible = false;
                _cardButton.Visible = true;
                _slotNameLabel.Text = data.SaveName;
                _saveTimeLabel.Text = data.SaveTime;
                _playTimeLabel.Text = $"游戏时间: {data.PlayTime}";
                
                if (data.Thumbnail != null)
                {
                    _thumbnailRect.Texture = data.Thumbnail;
                }
            }
            else
            {
                // 显示空存档
                _emptyLabel.Visible = true;
                _cardButton.Visible = false;
            }
        }
    }

    /// <summary>
    /// 存档数据类
    /// </summary>
    public class SaveSlotData
    {
        public int SlotIndex { get; set; }
        public bool HasSave { get; set; }
        public string SaveName { get; set; } = "";
        public string SaveTime { get; set; } = "";
        public string PlayTime { get; set; } = "";
        public int Level { get; set; }
        public Texture2D? Thumbnail { get; set; }
    }
}

