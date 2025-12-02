using Godot;
using System.Collections.Generic;
using Kuros.Managers;

namespace Kuros.UI
{
    /// <summary>
    /// 存档模式枚举
    /// </summary>
    public enum SaveLoadMode
    {
        Save,  // 存档模式
        Load   // 读档模式
    }

    /// <summary>
    /// 存档选择界面 - 软排样式（卡片式布局）
    /// </summary>
    public partial class SaveSlotSelection : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public Button SwitchModeButton { get; private set; } = null!;
        [Export] public ScrollContainer ScrollContainer { get; private set; } = null!;
        [Export] public GridContainer SlotGrid { get; private set; } = null!;
        [Export] public PackedScene SaveSlotCardScene { get; private set; } = null!;
        [Export] public Label TitleLabel { get; private set; } = null!;
        [Export] public TextureRect LocationImage { get; private set; } = null!;
        [Export] public Label HealthLabel { get; private set; } = null!;
        [Export] public Label WeaponLabel { get; private set; } = null!;
        [Export] public Label LevelLabel { get; private set; } = null!;
        [Export] public Label PlayTimeLabel { get; private set; } = null!;
        [Export] public Label SaveTimeLabel { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public int SlotsPerRow = 4;
        [Export] public int TotalSlots = 12;
        [Export] public SaveLoadMode Mode { get; set; } = SaveLoadMode.Load;
        [Export] public bool AllowSave { get; set; } = true; // 是否允许存档（从主界面进入时禁用）
        public bool FromBattleMenu { get; set; } = false; // 是否从战斗菜单进入

        // 信号
        [Signal] public delegate void SlotSelectedEventHandler(int slotIndex);
        [Signal] public delegate void SlotHighlightedEventHandler(int slotIndex);
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void ModeSwitchRequestedEventHandler(int newMode); // 使用int而不是枚举

        private List<SaveSlotCard> _slotCards = new List<SaveSlotCard>();
        private int _selectedSlotIndex = -1;

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

        public override void _Ready()
        {
            // 确保在游戏暂停时也能接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点 - 修复路径
            if (BackButton == null)
            {
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            }

            if (ScrollContainer == null)
            {
                ScrollContainer = GetNodeOrNull<ScrollContainer>("MenuPanel/VBoxContainer/ContentContainer/LeftPanel/ScrollContainer");
            }

            if (SlotGrid == null)
            {
                SlotGrid = GetNodeOrNull<GridContainer>("MenuPanel/VBoxContainer/ContentContainer/LeftPanel/ScrollContainer/SlotGrid");
            }

            if (TitleLabel == null)
            {
                TitleLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/Title");
            }

            if (SwitchModeButton == null)
            {
                SwitchModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SwitchModeButton");
            }

            if (LocationImage == null)
            {
                LocationImage = GetNodeOrNull<TextureRect>("MenuPanel/VBoxContainer/ContentContainer/RightPanel/LocationImage");
            }

            if (HealthLabel == null)
            {
                HealthLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/ContentContainer/RightPanel/InfoContainer/HealthLabel");
            }

            if (WeaponLabel == null)
            {
                WeaponLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/ContentContainer/RightPanel/InfoContainer/WeaponLabel");
            }

            if (LevelLabel == null)
            {
                LevelLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/ContentContainer/RightPanel/InfoContainer/LevelLabel");
            }

            if (PlayTimeLabel == null)
            {
                PlayTimeLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/ContentContainer/RightPanel/InfoContainer/PlayTimeLabel");
            }

            if (SaveTimeLabel == null)
            {
                SaveTimeLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/ContentContainer/RightPanel/InfoContainer/SaveTimeLabel");
            }

            // 驗證關鍵節點
            if (SlotGrid == null)
            {
                GD.PrintErr("SaveSlotSelection: 關鍵節點 SlotGrid 未找到，UI 將無法正常運作。");
                return;
            }
            if (BackButton == null)
            {
                GD.PrintErr("SaveSlotSelection: 關鍵節點 BackButton 未找到。");
            }
            if (SwitchModeButton == null)
            {
                GD.PrintErr("SaveSlotSelection: 關鍵節點 SwitchModeButton 未找到。");
            }
            if (TitleLabel == null)
            {
                GD.PrintErr("SaveSlotSelection: 關鍵節點 TitleLabel 未找到。");
            }

            // 设置网格列数
            if (SlotGrid != null)
            {
                SlotGrid.Columns = SlotsPerRow;
            }

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(BackButton, nameof(OnBackPressed));
            ConnectButtonSignal(SwitchModeButton, nameof(OnSwitchModePressed));

            // 更新标题和按钮
            UpdateTitle();
            UpdateSwitchModeButton();

            // 创建存档槽位
            CreateSaveSlots();
            
            // 初始化详情面板（显示空状态）
            UpdateDetailPanel(-1);
        }

        /// <summary>
        /// 设置模式并更新界面
        /// </summary>
        public void SetMode(SaveLoadMode mode)
        {
            Mode = mode;
            UpdateTitle();
            UpdateSwitchModeButton();
            RefreshSlots();
        }

        /// <summary>
        /// 设置是否允许存档
        /// </summary>
        public void SetAllowSave(bool allowSave)
        {
            AllowSave = allowSave;
            UpdateSwitchModeButton();
        }

        private void UpdateTitle()
        {
            if (TitleLabel != null)
            {
                TitleLabel.Text = Mode == SaveLoadMode.Save ? "选择存档位置" : "选择存档";
            }
        }

        private void UpdateSwitchModeButton()
        {
            if (SwitchModeButton != null)
            {
                // 如果当前是存档模式，切换到读档；如果是读档模式，切换到存档
                if (Mode == SaveLoadMode.Save)
                {
                    SwitchModeButton.Text = "切换到读档";
                    SwitchModeButton.Visible = true;
                }
                else
                {
                    SwitchModeButton.Text = "切换到存档";
                    SwitchModeButton.Visible = AllowSave; // 只有在允许存档时才显示
                }
            }
        }

        private void OnSwitchModePressed()
        {
            var newMode = Mode == SaveLoadMode.Save ? SaveLoadMode.Load : SaveLoadMode.Save;
            EmitSignal(SignalName.ModeSwitchRequested, (int)newMode); // 转换为int
        }

        private void CreateSaveSlots()
        {
            if (SlotGrid == null)
            {
                GD.PrintErr("SaveSlotSelection: SlotGrid is null, cannot create slots");
                return;
            }

            GD.Print($"SaveSlotSelection: 开始创建 {TotalSlots} 个存档槽位");

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
                    GD.Print($"SaveSlotSelection: 成功创建存档槽位 {i}");
                }
                else
                {
                    GD.PrintErr($"SaveSlotSelection: 创建存档槽位 {i} 失败");
                }
            }
            
            GD.Print($"SaveSlotSelection: 共创建了 {_slotCards.Count} 个存档槽位");
        }

        private SaveSlotCard CreateSaveSlotCard(int slotIndex)
        {
            SaveSlotCard? card = null;
            
            // 如果提供了场景，使用场景实例化
            if (SaveSlotCardScene != null)
            {
                card = SaveSlotCardScene.Instantiate<SaveSlotCard>();
            }
            
            // 如果场景实例化失败，创建默认卡片
            if (card == null)
            {
                card = new SaveSlotCard();
            }
            
            // 此时 card 一定不为 null
            var data = GetSaveSlotData(slotIndex);
            card.Initialize(slotIndex, data, Mode);
            card.SlotSelected += OnSlotCardSelected;
            card.SlotHighlighted += OnSlotCardHighlighted;
            
            return card;
        }

        private SaveSlotData GetSaveSlotData(int slotIndex)
        {
            // 从 SaveManager 获取实际的存档数据
            if (SaveManager.Instance == null)
            {
                GD.PrintErr("SaveSlotSelection: SaveManager.Instance is null");
                return new SaveSlotData
                {
                    SlotIndex = slotIndex,
                    HasSave = false
                };
            }

            var displayData = SaveManager.Instance.GetSaveSlotData(slotIndex);
            
            return new SaveSlotData
            {
                SlotIndex = displayData.SlotIndex,
                HasSave = displayData.HasSave,
                SaveName = displayData.SaveName,
                SaveTime = displayData.SaveTime,
                PlayTime = displayData.PlayTime,
                Level = displayData.Level,
                Thumbnail = displayData.Thumbnail,
                LocationImage = displayData.LocationImage,
                CurrentHealth = displayData.CurrentHealth,
                MaxHealth = displayData.MaxHealth,
                WeaponName = displayData.WeaponName,
                LevelProgress = displayData.LevelProgress
            };
        }

        private void OnSlotCardSelected(int slotIndex)
        {
            _selectedSlotIndex = slotIndex;
            EmitSignal(SignalName.SlotSelected, slotIndex);
        }

        private void OnSlotCardHighlighted(int slotIndex)
        {
            UpdateDetailPanel(slotIndex);
        }

        private void UpdateDetailPanel(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slotCards.Count)
            {
                // 显示空状态
                if (LocationImage != null) LocationImage.Texture = null;
                if (HealthLabel != null) HealthLabel.Text = "生命值: - / -";
                if (WeaponLabel != null) WeaponLabel.Text = "武器: -";
                if (LevelLabel != null) LevelLabel.Text = "等级: -";
                if (PlayTimeLabel != null) PlayTimeLabel.Text = "游戏时间: -";
                if (SaveTimeLabel != null) SaveTimeLabel.Text = "保存时间: -";
                return;
            }

            var data = GetSaveSlotData(slotIndex);
            if (data.HasSave)
            {
                if (LocationImage != null)
                {
                    LocationImage.Texture = data.LocationImage;
                }
                if (HealthLabel != null)
                {
                    HealthLabel.Text = $"生命值: {data.CurrentHealth} / {data.MaxHealth}";
                }
                if (WeaponLabel != null)
                {
                    WeaponLabel.Text = $"武器: {data.WeaponName}";
                }
                if (LevelLabel != null)
                {
                    LevelLabel.Text = $"等级: {data.Level} - {data.LevelProgress}";
                }
                if (PlayTimeLabel != null)
                {
                    PlayTimeLabel.Text = $"游戏时间: {data.PlayTime}";
                }
                if (SaveTimeLabel != null)
                {
                    SaveTimeLabel.Text = $"保存时间: {data.SaveTime}";
                }
            }
            else
            {
                // 空存档
                if (LocationImage != null) LocationImage.Texture = null;
                if (HealthLabel != null) HealthLabel.Text = "生命值: - / -";
                if (WeaponLabel != null) WeaponLabel.Text = "武器: -";
                if (LevelLabel != null) LevelLabel.Text = "等级: -";
                if (PlayTimeLabel != null) PlayTimeLabel.Text = "游戏时间: -";
                if (SaveTimeLabel != null) SaveTimeLabel.Text = "保存时间: -";
            }
        }

        /// <summary>
        /// 刷新存档列表
        /// </summary>
        public void RefreshSlots()
        {
            // 如果卡片列表为空，重新创建
            if (_slotCards.Count == 0)
            {
                CreateSaveSlots();
                return;
            }
            
            // 更新现有卡片
            for (int i = 0; i < _slotCards.Count && i < TotalSlots; i++)
            {
                if (_slotCards[i] != null && IsInstanceValid(_slotCards[i]))
                {
                    var data = GetSaveSlotData(i);
                    _slotCards[i].UpdateData(data, Mode);
                }
                else
                {
                    // 如果卡片无效，重新创建
                    GD.PushWarning($"SaveSlotSelection: 槽位 {i} 的卡片无效，重新创建");
                    if (SlotGrid != null)
                    {
                        // 清理先前無效的 _slotCards[i] 引用及 SlotGrid 中的無效子節點
                        if (_slotCards[i] != null && !IsInstanceValid(_slotCards[i]))
                        {
                            var invalidRef = _slotCards[i];
                            var children = SlotGrid.GetChildren();
                            foreach (var child in children)
                            {
                                // 移除無效子節點或與無效引用相同的子節點
                                if (!IsInstanceValid(child) || child == invalidRef)
                                {
                                    SlotGrid.RemoveChild(child);
                                    if (IsInstanceValid(child))
                                    {
                                        child.QueueFree();
                                    }
                                }
                            }
                            _slotCards[i] = null!;
                        }
                        
                        var newCard = CreateSaveSlotCard(i);
                        if (newCard != null)
                        {
                            SlotGrid.AddChild(newCard);
                            // 計算安全的目標索引（使用 i 限制在有效範圍內）
                            int validIndex = Mathf.Clamp(i, 0, Mathf.Max(0, SlotGrid.GetChildCount() - 1));
                            SlotGrid.MoveChild(newCard, validIndex);
                            _slotCards[i] = newCard;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 设置来源（是否从战斗菜单进入）
        /// </summary>
        public void SetSource(bool fromBattleMenu)
        {
            FromBattleMenu = fromBattleMenu;
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }

        public override void _Input(InputEvent @event)
        {
            // 只有在控件可见时才处理输入，避免隐藏的实例拦截 ESC 键
            if (!IsVisibleInTree())
            {
                return;
            }
            
            if (@event.IsActionPressed("ui_cancel"))
            {
                OnBackPressed();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// 存档槽位卡片 - 完全重构版本，修复所有显示和交互问题
    /// </summary>
    public partial class SaveSlotCard : Button
    {
        [Signal] public delegate void SlotSelectedEventHandler(int slotIndex);
        [Signal] public delegate void SlotHighlightedEventHandler(int slotIndex);

        private int _slotIndex;
        private SaveLoadMode _mode;
        private SaveSlotData? _currentData;
        
        private VBoxContainer? _contentContainer;
        private Label? _emptyLabel;
        private TextureRect? _thumbnailRect;
        private Label? _slotNameLabel;
        private Label? _saveTimeLabel;
        private Label? _playTimeLabel;
        private bool _uiInitialized = false;

        public SaveSlotCard()
        {
            Name = "SaveSlotCard";
            CustomMinimumSize = new Vector2(150, 200);
            
            // 设置按钮样式 - 带背景和边框
            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = new Color(0.15f, 0.15f, 0.2f, 1.0f); // 深色背景
            normalStyle.BorderWidthLeft = 3;
            normalStyle.BorderWidthTop = 3;
            normalStyle.BorderWidthRight = 3;
            normalStyle.BorderWidthBottom = 3;
            normalStyle.BorderColor = new Color(0.6f, 0.6f, 0.7f, 1.0f); // 浅色边框
            normalStyle.CornerRadiusTopLeft = 6;
            normalStyle.CornerRadiusTopRight = 6;
            normalStyle.CornerRadiusBottomLeft = 6;
            normalStyle.CornerRadiusBottomRight = 6;
            
            var hoverStyle = normalStyle.Duplicate() as StyleBoxFlat;
            if (hoverStyle != null)
            {
                hoverStyle.BgColor = new Color(0.25f, 0.25f, 0.3f, 1.0f); // 悬停时更亮
                hoverStyle.BorderColor = new Color(0.8f, 0.8f, 0.9f, 1.0f);
            }
            
            var pressedStyle = normalStyle.Duplicate() as StyleBoxFlat;
            if (pressedStyle != null)
            {
                pressedStyle.BgColor = new Color(0.35f, 0.35f, 0.4f, 1.0f); // 按下时更亮
            }
            
            AddThemeStyleboxOverride("normal", normalStyle);
            AddThemeStyleboxOverride("hover", hoverStyle ?? normalStyle);
            AddThemeStyleboxOverride("pressed", pressedStyle ?? normalStyle);
            AddThemeStyleboxOverride("disabled", normalStyle);
            
            // 确保按钮可以接收鼠标事件
            MouseFilter = Control.MouseFilterEnum.Stop;
            
            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            var pressedCallable = new Callable(this, nameof(OnPressed));
            if (!IsConnected(Button.SignalName.Pressed, pressedCallable))
            {
                Connect(Button.SignalName.Pressed, pressedCallable);
            }
            var mouseEnteredCallable = new Callable(this, nameof(OnMouseEntered));
            if (!IsConnected(Control.SignalName.MouseEntered, mouseEnteredCallable))
            {
                Connect(Control.SignalName.MouseEntered, mouseEnteredCallable);
            }
        }

        public override void _Ready()
        {
            base._Ready();
            // 如果UI还没有初始化，现在初始化
            if (!_uiInitialized && _currentData != null)
            {
                SetupUI();
                UpdateDisplay();
            }
        }

        public void Initialize(int slotIndex, SaveSlotData data, SaveLoadMode mode = SaveLoadMode.Load)
        {
            _slotIndex = slotIndex;
            _mode = mode;
            _currentData = data;
            
            if (!_uiInitialized)
            {
                SetupUI();
            }
            UpdateDisplay();
        }

        public void UpdateData(SaveSlotData data, SaveLoadMode mode)
        {
            _currentData = data;
            _mode = mode;
            UpdateDisplay();
        }

        private void SetupUI()
        {
            if (_uiInitialized) return;
            
            // 清除按钮的默认文本
            Text = "";
            
            // 创建内容容器
            _contentContainer = new VBoxContainer();
            _contentContainer.Name = "ContentContainer";
            _contentContainer.LayoutMode = 1; // 使用锚点布局
            // 设置锚点填充整个按钮 (FullRect)
            _contentContainer.AnchorLeft = 0.0f;
            _contentContainer.AnchorTop = 0.0f;
            _contentContainer.AnchorRight = 1.0f;
            _contentContainer.AnchorBottom = 1.0f;
            _contentContainer.OffsetLeft = 5;
            _contentContainer.OffsetTop = 5;
            _contentContainer.OffsetRight = -5;
            _contentContainer.OffsetBottom = -5;
            _contentContainer.MouseFilter = Control.MouseFilterEnum.Ignore; // 不拦截鼠标事件
            AddChild(_contentContainer);

            // 缩略图
            _thumbnailRect = new TextureRect();
            _thumbnailRect.Name = "Thumbnail";
            _thumbnailRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            _thumbnailRect.CustomMinimumSize = new Vector2(0, 100);
            _thumbnailRect.MouseFilter = Control.MouseFilterEnum.Ignore;
            _contentContainer.AddChild(_thumbnailRect);

            // 存档名称
            _slotNameLabel = new Label();
            _slotNameLabel.Name = "SlotName";
            _slotNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _slotNameLabel.AddThemeFontSizeOverride("font_size", 16);
            _slotNameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _contentContainer.AddChild(_slotNameLabel);

            // 保存时间
            _saveTimeLabel = new Label();
            _saveTimeLabel.Name = "SaveTime";
            _saveTimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _saveTimeLabel.AddThemeFontSizeOverride("font_size", 11);
            _saveTimeLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _contentContainer.AddChild(_saveTimeLabel);

            // 游戏时间
            _playTimeLabel = new Label();
            _playTimeLabel.Name = "PlayTime";
            _playTimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _playTimeLabel.AddThemeFontSizeOverride("font_size", 11);
            _playTimeLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _contentContainer.AddChild(_playTimeLabel);

            // 空存档标签
            _emptyLabel = new Label();
            _emptyLabel.Name = "EmptyLabel";
            _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyLabel.VerticalAlignment = VerticalAlignment.Center;
            _emptyLabel.AddThemeFontSizeOverride("font_size", 14);
            _emptyLabel.MouseFilter = Control.MouseFilterEnum.Ignore; // 不拦截鼠标事件
            _emptyLabel.LayoutMode = 1;
            // 设置锚点填充整个按钮 (FullRect)
            _emptyLabel.AnchorLeft = 0.0f;
            _emptyLabel.AnchorTop = 0.0f;
            _emptyLabel.AnchorRight = 1.0f;
            _emptyLabel.AnchorBottom = 1.0f;
            AddChild(_emptyLabel);
            
            _uiInitialized = true;
        }

        private void UpdateDisplay()
        {
            if (_currentData == null) return;
            if (!_uiInitialized) return;

            if (_currentData.HasSave)
            {
                // 显示存档信息
                if (_emptyLabel != null) _emptyLabel.Visible = false;
                if (_contentContainer != null) _contentContainer.Visible = true;
                
                if (_slotNameLabel != null)
                    _slotNameLabel.Text = string.IsNullOrEmpty(_currentData.SaveName) ? $"存档 {_slotIndex + 1}" : _currentData.SaveName;
                if (_saveTimeLabel != null)
                    _saveTimeLabel.Text = string.IsNullOrEmpty(_currentData.SaveTime) ? "未知时间" : _currentData.SaveTime;
                if (_playTimeLabel != null)
                    _playTimeLabel.Text = string.IsNullOrEmpty(_currentData.PlayTime) ? "时间: 00:00:00" : $"时间: {_currentData.PlayTime}";
                
                if (_thumbnailRect != null)
                {
                    if (_currentData.Thumbnail != null)
                    {
                        _thumbnailRect.Texture = _currentData.Thumbnail;
                        _thumbnailRect.Visible = true;
                    }
                    else
                    {
                        _thumbnailRect.Visible = false;
                    }
                }
            }
            else
            {
                // 显示空存档
                if (_contentContainer != null) _contentContainer.Visible = false;
                if (_emptyLabel != null)
                {
                    _emptyLabel.Visible = true;
                    if (_mode == SaveLoadMode.Load)
                    {
                        _emptyLabel.Text = $"存档槽位 {_slotIndex + 1}\n\n（空存档）";
                    }
                    else
                    {
                        _emptyLabel.Text = $"存档槽位 {_slotIndex + 1}\n\n（点击新建存档）";
                    }
                }
            }
        }

        private void OnPressed()
        {
            GD.Print($"SaveSlotCard: 槽位 {_slotIndex} 被点击");
            EmitSignal(SignalName.SlotSelected, _slotIndex);
        }

        private void OnMouseEntered()
        {
            EmitSignal(SignalName.SlotHighlighted, _slotIndex);
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
        public Texture2D? LocationImage { get; set; } // 玩家位置小图
        public int CurrentHealth { get; set; } = 0;
        public int MaxHealth { get; set; } = 0;
        public string WeaponName { get; set; } = "";
        public string LevelProgress { get; set; } = "";
    }
}
