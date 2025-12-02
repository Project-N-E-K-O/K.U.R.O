using Godot;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 图鉴系统窗口 - 当前包含怪物与武器两个分类，均使用占位数据。
    /// </summary>
    public partial class CompendiumWindow : Control
    {
        private const int GridColumns = 4;
        private const string PlaceholderText = "我是测试文本";

        [Signal] public delegate void CompendiumClosedEventHandler();

        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public TabContainer Tabs { get; private set; } = null!;

        // 怪物图鉴引用
        [Export] public GridContainer MonsterGrid { get; private set; } = null!;
        [Export] public Label MonsterName { get; private set; } = null!;
        [Export] public TextureRect MonsterImage { get; private set; } = null!;
        [Export] public RichTextLabel MonsterDescription { get; private set; } = null!;
        [Export] public Label MonsterHpValue { get; private set; } = null!;
        [Export] public Label MonsterDefenseValue { get; private set; } = null!;
        [Export] public Label MonsterSpeedValue { get; private set; } = null!;
        [Export] public Label MonsterAttackValue { get; private set; } = null!;
        [Export] public Label MonsterRangeValue { get; private set; } = null!;
        [Export] public RichTextLabel MonsterSkillDescription { get; private set; } = null!;

        // NPC图鉴引用
        [Export] public GridContainer NpcGrid { get; private set; } = null!;
        [Export] public Label NpcName { get; private set; } = null!;
        [Export] public TextureRect NpcImage { get; private set; } = null!;
        [Export] public Label NpcHeartIcon { get; private set; } = null!;
        [Export] public RichTextLabel NpcDescriptionText { get; private set; } = null!;
        [Export] public Label NpcAffectionValue { get; private set; } = null!;
        [Export] public RichTextLabel NpcQuestDescription { get; private set; } = null!;

        // 武器图鉴引用
        [Export] public GridContainer WeaponGrid { get; private set; } = null!;
        [Export] public Label WeaponName { get; private set; } = null!;
        [Export] public TextureRect WeaponImage { get; private set; } = null!;
        [Export] public RichTextLabel WeaponDescriptionText { get; private set; } = null!;
        [Export] public Label WeaponAttackValue { get; private set; } = null!;
        [Export] public Label WeaponElementValue { get; private set; } = null!;
        [Export] public Label WeaponWeightValue { get; private set; } = null!;
        [Export] public Label WeaponRarityValue { get; private set; } = null!;
        [Export] public Label WeaponSpecialValue { get; private set; } = null!;
        [Export] public RichTextLabel WeaponEffectDescription { get; private set; } = null!;

        private readonly List<MonsterEntry> _monsterEntries = new();
        private readonly Dictionary<MonsterEntry, Button> _monsterButtons = new();
        private MonsterEntry? _currentMonsterSelection;

        private readonly List<NpcEntry> _npcEntries = new();
        private readonly Dictionary<NpcEntry, Button> _npcButtons = new();
        private NpcEntry? _currentNpcSelection;

        private readonly List<WeaponEntry> _weaponEntries = new();
        private readonly Dictionary<WeaponEntry, Button> _weaponButtons = new();
        private WeaponEntry? _currentWeaponSelection;

        public override void _Ready()
        {
            CacheNodeReferences();

            BuildMonsterEntries();
            PopulateMonsterGrid();

            BuildNpcEntries();
            PopulateNpcGrid();

            BuildWeaponEntries();
            PopulateWeaponGrid();

            // 默认不选择任何实例，直接清除所有面板
            ClearMonsterPanel();
            ClearNpcPanel();
            ClearWeaponPanel();

            HideWindow();
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/RootMargin/RootVBox/Header/CloseButton");
            Tabs ??= GetNodeOrNull<TabContainer>("MainPanel/RootMargin/RootVBox/Tabs");

            MonsterGrid ??= GetNodeOrNull<GridContainer>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterLeftPanel/MonsterLeftVBox/MonsterScroll/MonsterGrid");
            MonsterName ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/MonsterName");
            MonsterImage ??= GetNodeOrNull<TextureRect>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/MonsterImage");
            MonsterDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/DescriptionSection/DescriptionScroll/DescriptionText");
            MonsterHpValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/HpValue");
            MonsterDefenseValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/DefenseValue");
            MonsterSpeedValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/SpeedValue");
            MonsterAttackValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/AttackValue");
            MonsterRangeValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/RangeValue");
            MonsterSkillDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/SkillSection/SkillScroll/SkillDescription");

            NpcGrid ??= GetNodeOrNull<GridContainer>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCLeftPanel/NPCLeftVBox/NPCScroll/NPCGrid");
            NpcName ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCName");
            NpcImage ??= GetNodeOrNull<TextureRect>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCImageWrapper/NPCImage");
            NpcHeartIcon ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCImageWrapper/NPCHearthIcon");
            NpcDescriptionText ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCDescriptionSection/NPCDescriptionScroll/NPCDescriptionText");
            NpcAffectionValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCAffectionSection/NPCAffectionValue");
            NpcQuestDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCQuestSection/NPCQuestScroll/NPCQuestDescription");

            WeaponGrid ??= GetNodeOrNull<GridContainer>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponLeftPanel/WeaponLeftVBox/WeaponScroll/WeaponGrid");
            WeaponName ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponName");
            WeaponImage ??= GetNodeOrNull<TextureRect>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponImage");
            WeaponDescriptionText ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponDescriptionSection/WeaponDescriptionScroll/WeaponDescriptionText");
            WeaponAttackValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponAttackValue");
            WeaponElementValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponElementValue");
            WeaponWeightValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponWeightValue");
            WeaponRarityValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponRarityValue");
            WeaponSpecialValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponSpecialValue");
            WeaponEffectDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponEffectSection/WeaponEffectScroll/WeaponEffectDescription");

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            if (CloseButton != null)
            {
                var callable = new Callable(this, nameof(HideWindow));
                if (!CloseButton.IsConnected(Button.SignalName.Pressed, callable))
                {
                    CloseButton.Connect(Button.SignalName.Pressed, callable);
                }
            }
        }

        public void ShowWindow()
        {
            Visible = true;
            ProcessMode = ProcessModeEnum.Always; // 确保暂停时也能接收输入
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            
            // 尝试将窗口移到父节点的最后，确保输入处理优先级
            var parent = GetParent();
            if (parent != null)
            {
                parent.MoveChild(this, parent.GetChildCount() - 1);
                GD.Print($"CompendiumWindow.ShowWindow: 已将图鉴窗口移到父节点最后");
            }
            
            GD.Print("CompendiumWindow.ShowWindow: 图鉴窗口已打开，输入处理已启用");
        }

        public void HideWindow()
        {
            if (!Visible)
            {
                SetProcessInput(false);
                SetProcessUnhandledInput(false);
                return;
            }

            Visible = false;
            ProcessMode = ProcessModeEnum.Inherit;
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            EmitSignal(SignalName.CompendiumClosed);
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible)
            {
                return;
            }

            // 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                // 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
                // 直接返回，让弹窗处理（禁用）
                return;
            }

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
                GD.Print($"CompendiumWindow._UnhandledInput: 检测到ESC键，当前标签页={Tabs?.CurrentTab ?? -1}，是否有选中条目={HasSelectedEntry()}");
                
                // 首先检查是否有选中的条目，如果有则取消选择（返回列表状态）
                if (HasSelectedEntry())
                {
                    GD.Print("CompendiumWindow._UnhandledInput: 清除选中条目");
                    ClearSelectedEntry();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                // 如果没有选中的条目，检查是否在非第一个标签页
                int currentTab = Tabs?.CurrentTab ?? -1;
                if (Tabs != null && currentTab > 0)
                {
                    // 切换到上一个标签页
                    int newTab = currentTab - 1;
                    GD.Print($"CompendiumWindow._UnhandledInput: 切换到上一个标签页，从{currentTab}到{newTab}");
                    Tabs.CurrentTab = newTab;
                    GetViewport().SetInputAsHandled();
                }
                else
                {
                    // 如果已经在第一个标签页且没有选中条目，关闭窗口
                    GD.Print("CompendiumWindow._UnhandledInput: 关闭图鉴窗口");
                    HideWindow();
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (!Visible)
            {
                return;
            }

            // 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                // 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
                // 直接返回，让弹窗处理（禁用）
                return;
            }

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
                GD.Print($"CompendiumWindow._GuiInput: 检测到ESC键，当前标签页={Tabs?.CurrentTab ?? -1}，是否有选中条目={HasSelectedEntry()}");
                
                // 首先检查是否有选中的条目，如果有则取消选择（返回列表状态）
                if (HasSelectedEntry())
                {
                    GD.Print("CompendiumWindow._GuiInput: 清除选中条目");
                    ClearSelectedEntry();
                    AcceptEvent();
                    return;
                }

                // 如果没有选中的条目，检查是否在非第一个标签页
                int currentTab = Tabs?.CurrentTab ?? -1;
                if (Tabs != null && currentTab > 0)
                {
                    // 切换到上一个标签页
                    int newTab = currentTab - 1;
                    GD.Print($"CompendiumWindow._GuiInput: 切换到上一个标签页，从{currentTab}到{newTab}");
                    Tabs.CurrentTab = newTab;
                    AcceptEvent();
                }
                else
                {
                    // 如果已经在第一个标签页且没有选中条目，关闭窗口
                    GD.Print("CompendiumWindow._GuiInput: 关闭图鉴窗口");
                    HideWindow();
                    AcceptEvent();
                }
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (!Visible)
            {
                return;
            }

            // 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                // 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
                // 直接返回，让弹窗处理（禁用）
                return;
            }

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
                GD.Print($"CompendiumWindow._Input: 检测到ESC键，当前标签页={Tabs?.CurrentTab ?? -1}，是否有选中条目={HasSelectedEntry()}");
                
                // 首先检查是否有选中的条目，如果有则取消选择（返回列表状态）
                if (HasSelectedEntry())
                {
                    GD.Print("CompendiumWindow._Input: 清除选中条目");
                    ClearSelectedEntry();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                // 如果没有选中的条目，检查是否在非第一个标签页
                int currentTab = Tabs?.CurrentTab ?? -1;
                if (Tabs != null && currentTab > 0)
                {
                    // 切换到上一个标签页
                    int newTab = currentTab - 1;
                    GD.Print($"CompendiumWindow._Input: 切换到上一个标签页，从{currentTab}到{newTab}");
                    Tabs.CurrentTab = newTab;
                    GetViewport().SetInputAsHandled();
                }
                else
                {
                    // 如果已经在第一个标签页且没有选中条目，关闭窗口
                    GD.Print("CompendiumWindow._Input: 关闭图鉴窗口");
                    HideWindow();
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        /// <summary>
        /// 检查是否有选中的条目
        /// </summary>
        private bool HasSelectedEntry()
        {
            return _currentMonsterSelection != null || 
                   _currentNpcSelection != null || 
                   _currentWeaponSelection != null;
        }

        /// <summary>
        /// 清除选中的条目，返回到列表状态
        /// </summary>
        private void ClearSelectedEntry()
        {
            // 清除怪物选择
            if (_currentMonsterSelection != null)
            {
                _currentMonsterSelection = null;
                foreach (var pair in _monsterButtons)
                {
                    pair.Value.ButtonPressed = false;
                }
                ClearMonsterPanel();
                GD.Print("CompendiumWindow: 已清除怪物选择，返回列表状态");
                return;
            }

            // 清除NPC选择
            if (_currentNpcSelection != null)
            {
                _currentNpcSelection = null;
                foreach (var pair in _npcButtons)
                {
                    pair.Value.ButtonPressed = false;
                }
                ClearNpcPanel();
                GD.Print("CompendiumWindow: 已清除NPC选择，返回列表状态");
                return;
            }

            // 清除武器选择
            if (_currentWeaponSelection != null)
            {
                _currentWeaponSelection = null;
                foreach (var pair in _weaponButtons)
                {
                    pair.Value.ButtonPressed = false;
                }
                ClearWeaponPanel();
                GD.Print("CompendiumWindow: 已清除武器选择，返回列表状态");
                return;
            }
        }

        private void BuildMonsterEntries()
        {
            _monsterEntries.Clear();
            for (int i = 0; i < 16; i++)
            {
                _monsterEntries.Add(new MonsterEntry(
                    $"monster_placeholder_{i}",
                    PlaceholderText,
                    null,
                    PlaceholderText,
                    new MonsterStats(PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText),
                    PlaceholderText));
            }
        }

        private void PopulateMonsterGrid()
        {
            if (MonsterGrid == null)
            {
                GD.PrintErr("MonsterGrid 缺失，无法构建怪物列表。");
                return;
            }

            foreach (Node child in MonsterGrid.GetChildren())
            {
                child.QueueFree();
            }

            MonsterGrid.Columns = GridColumns;
            _monsterButtons.Clear();

            foreach (var entry in _monsterEntries)
            {
                var button = CreateMonsterButton(entry);
                MonsterGrid.AddChild(button);
                _monsterButtons[entry] = button;
            }
        }

        private Button CreateMonsterButton(MonsterEntry entry)
        {
            var button = new Button
            {
                Text = entry.Name,
                CustomMinimumSize = new Vector2(160, 140),
                ToggleMode = true,
                TooltipText = $"{entry.Name}\n点击查看详情"
            };

            button.AddThemeFontSizeOverride("font_size", 18);
            // 将 entry 存储到按钮的元数据中，使用 Callable.From 连接信号
            button.SetMeta("monster_entry_id", entry.Id);
            var callable = Callable.From(() => SelectMonster(entry));
            button.Connect(Button.SignalName.Pressed, callable);
            return button;
        }

        private void SelectMonster(MonsterEntry entry)
        {
            _currentMonsterSelection = entry;

            foreach (var pair in _monsterButtons)
            {
                pair.Value.ButtonPressed = pair.Key == entry;
            }

            UpdateMonsterPanel(entry);
        }

        private void UpdateMonsterPanel(MonsterEntry entry)
        {
            if (MonsterName != null)
                MonsterName.Text = entry.Name;

            if (MonsterImage != null)
                MonsterImage.Texture = entry.Texture;

            if (MonsterDescription != null)
                MonsterDescription.Text = entry.Description;

            if (MonsterHpValue != null)
                MonsterHpValue.Text = entry.Stats.Hp;

            if (MonsterDefenseValue != null)
                MonsterDefenseValue.Text = entry.Stats.Defense;

            if (MonsterSpeedValue != null)
                MonsterSpeedValue.Text = entry.Stats.Speed;

            if (MonsterAttackValue != null)
                MonsterAttackValue.Text = entry.Stats.Attack;

            if (MonsterRangeValue != null)
                MonsterRangeValue.Text = entry.Stats.AttackRange;

            if (MonsterSkillDescription != null)
                MonsterSkillDescription.Text = entry.SkillDescription;
        }

        private void ClearMonsterPanel()
        {
            if (MonsterName != null) MonsterName.Text = PlaceholderText;
            if (MonsterImage != null) MonsterImage.Texture = null;
            if (MonsterDescription != null) MonsterDescription.Text = PlaceholderText;
            if (MonsterHpValue != null) MonsterHpValue.Text = PlaceholderText;
            if (MonsterDefenseValue != null) MonsterDefenseValue.Text = PlaceholderText;
            if (MonsterSpeedValue != null) MonsterSpeedValue.Text = PlaceholderText;
            if (MonsterAttackValue != null) MonsterAttackValue.Text = PlaceholderText;
            if (MonsterRangeValue != null) MonsterRangeValue.Text = PlaceholderText;
            if (MonsterSkillDescription != null) MonsterSkillDescription.Text = PlaceholderText;
        }

        private void BuildNpcEntries()
        {
            _npcEntries.Clear();
            for (int i = 0; i < 16; i++)
            {
                _npcEntries.Add(new NpcEntry(
                    $"npc_placeholder_{i}",
                    PlaceholderText,
                    null,
                    PlaceholderText,
                    PlaceholderText,
                    PlaceholderText));
            }
        }

        private void BuildWeaponEntries()
        {
            _weaponEntries.Clear();
            for (int i = 0; i < 16; i++)
            {
                _weaponEntries.Add(new WeaponEntry(
                    $"weapon_placeholder_{i}",
                    PlaceholderText,
                    null,
                    PlaceholderText,
                    new WeaponStats(PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText),
                    PlaceholderText));
            }
        }

        private void PopulateNpcGrid()
        {
            if (NpcGrid == null)
            {
                GD.PrintErr("NPCGrid 缺失，无法构建 NPC 列表。");
                return;
            }

            foreach (Node child in NpcGrid.GetChildren())
            {
                child.QueueFree();
            }

            NpcGrid.Columns = GridColumns;
            _npcButtons.Clear();

            foreach (var entry in _npcEntries)
            {
                var button = CreateNpcButton(entry);
                NpcGrid.AddChild(button);
                _npcButtons[entry] = button;
            }
        }

        private void PopulateWeaponGrid()
        {
            if (WeaponGrid == null)
            {
                GD.PrintErr("WeaponGrid 缺失，无法构建武器列表。");
                return;
            }

            foreach (Node child in WeaponGrid.GetChildren())
            {
                child.QueueFree();
            }

            WeaponGrid.Columns = GridColumns;
            _weaponButtons.Clear();

            foreach (var entry in _weaponEntries)
            {
                var button = CreateWeaponButton(entry);
                WeaponGrid.AddChild(button);
                _weaponButtons[entry] = button;
            }
        }

        private Button CreateNpcButton(NpcEntry entry)
        {
            var button = new Button
            {
                Text = entry.Name,
                CustomMinimumSize = new Vector2(160, 140),
                ToggleMode = true,
                TooltipText = $"{entry.Name}\n点击查看详情"
            };

            button.AddThemeFontSizeOverride("font_size", 18);
            // 使用 Callable.From 连接信号
            button.SetMeta("npc_entry_id", entry.Id);
            var callable = Callable.From(() => SelectNpc(entry));
            button.Connect(Button.SignalName.Pressed, callable);
            return button;
        }

        private Button CreateWeaponButton(WeaponEntry entry)
        {
            var button = new Button
            {
                Text = entry.Name,
                CustomMinimumSize = new Vector2(160, 140),
                ToggleMode = true,
                TooltipText = $"{entry.Name}\n点击查看详情"
            };

            button.AddThemeFontSizeOverride("font_size", 18);
            // 使用 Callable.From 连接信号
            button.SetMeta("weapon_entry_id", entry.Id);
            var callable = Callable.From(() => SelectWeapon(entry));
            button.Connect(Button.SignalName.Pressed, callable);
            return button;
        }

        private void SelectNpc(NpcEntry entry)
        {
            _currentNpcSelection = entry;

            foreach (var pair in _npcButtons)
            {
                pair.Value.ButtonPressed = pair.Key == entry;
            }

            UpdateNpcPanel(entry);
        }

        private void SelectWeapon(WeaponEntry entry)
        {
            _currentWeaponSelection = entry;

            foreach (var pair in _weaponButtons)
            {
                pair.Value.ButtonPressed = pair.Key == entry;
            }

            UpdateWeaponPanel(entry);
        }

        private void UpdateNpcPanel(NpcEntry entry)
        {
            if (NpcName != null)
                NpcName.Text = entry.Name;

            if (NpcImage != null)
                NpcImage.Texture = entry.Texture;

            if (NpcDescriptionText != null)
                NpcDescriptionText.Text = entry.Description;

            if (NpcAffectionValue != null)
                NpcAffectionValue.Text = entry.AffectionLevel;

            if (NpcQuestDescription != null)
                NpcQuestDescription.Text = entry.QuestInfo;

            if (NpcHeartIcon != null)
                NpcHeartIcon.Text = entry.HeartSymbol;
        }

        private void UpdateWeaponPanel(WeaponEntry entry)
        {
            if (WeaponName != null)
                WeaponName.Text = entry.Name;

            if (WeaponImage != null)
                WeaponImage.Texture = entry.Texture;

            if (WeaponDescriptionText != null)
                WeaponDescriptionText.Text = entry.Description;

            if (WeaponAttackValue != null)
                WeaponAttackValue.Text = entry.Stats.Attack;

            if (WeaponElementValue != null)
                WeaponElementValue.Text = entry.Stats.Element;

            if (WeaponWeightValue != null)
                WeaponWeightValue.Text = entry.Stats.Weight;

            if (WeaponRarityValue != null)
                WeaponRarityValue.Text = entry.Stats.Rarity;

            if (WeaponSpecialValue != null)
                WeaponSpecialValue.Text = entry.Stats.SpecialEffect;

            if (WeaponEffectDescription != null)
                WeaponEffectDescription.Text = entry.SkillDescription;
        }

        private void ClearNpcPanel()
        {
            if (NpcName != null) NpcName.Text = PlaceholderText;
            if (NpcImage != null) NpcImage.Texture = null;
            if (NpcHeartIcon != null) NpcHeartIcon.Text = "♥";
            if (NpcDescriptionText != null) NpcDescriptionText.Text = PlaceholderText;
            if (NpcAffectionValue != null) NpcAffectionValue.Text = PlaceholderText;
            if (NpcQuestDescription != null) NpcQuestDescription.Text = PlaceholderText;
        }

        private void ClearWeaponPanel()
        {
            if (WeaponName != null) WeaponName.Text = PlaceholderText;
            if (WeaponImage != null) WeaponImage.Texture = null;
            if (WeaponDescriptionText != null) WeaponDescriptionText.Text = PlaceholderText;
            if (WeaponAttackValue != null) WeaponAttackValue.Text = PlaceholderText;
            if (WeaponElementValue != null) WeaponElementValue.Text = PlaceholderText;
            if (WeaponWeightValue != null) WeaponWeightValue.Text = PlaceholderText;
            if (WeaponRarityValue != null) WeaponRarityValue.Text = PlaceholderText;
            if (WeaponSpecialValue != null) WeaponSpecialValue.Text = PlaceholderText;
            if (WeaponEffectDescription != null) WeaponEffectDescription.Text = PlaceholderText;
        }

        private sealed class MonsterEntry
        {
            public string Id { get; }
            public string Name { get; }
            public Texture2D? Texture { get; }
            public string Description { get; }
            public MonsterStats Stats { get; }
            public string SkillDescription { get; }

            public MonsterEntry(string id, string name, Texture2D? texture, string description, MonsterStats stats, string skillDescription)
            {
                Id = id;
                Name = name;
                Texture = texture;
                Description = description;
                Stats = stats;
                SkillDescription = skillDescription;
            }
        }

        private readonly struct MonsterStats
        {
            public string Hp { get; }
            public string Defense { get; }
            public string Speed { get; }
            public string Attack { get; }
            public string AttackRange { get; }

            public MonsterStats(string hp, string defense, string speed, string attack, string attackRange)
            {
                Hp = hp;
                Defense = defense;
                Speed = speed;
                Attack = attack;
                AttackRange = attackRange;
            }
        }

        private sealed class WeaponEntry
        {
            public string Id { get; }
            public string Name { get; }
            public Texture2D? Texture { get; }
            public string Description { get; }
            public WeaponStats Stats { get; }
            public string SkillDescription { get; }

            public WeaponEntry(string id, string name, Texture2D? texture, string description, WeaponStats stats, string skillDescription)
            {
                Id = id;
                Name = name;
                Texture = texture;
                Description = description;
                Stats = stats;
                SkillDescription = skillDescription;
            }
        }

        private readonly struct WeaponStats
        {
            public string Attack { get; }
            public string Element { get; }
            public string Weight { get; }
            public string Rarity { get; }
            public string SpecialEffect { get; }

            public WeaponStats(string attack, string element, string weight, string rarity, string specialEffect)
            {
                Attack = attack;
                Element = element;
                Weight = weight;
                Rarity = rarity;
                SpecialEffect = specialEffect;
            }
        }

        private sealed class NpcEntry
        {
            public string Id { get; }
            public string Name { get; }
            public Texture2D? Texture { get; }
            public string Description { get; }
            public string AffectionLevel { get; }
            public string QuestInfo { get; }
            public string HeartSymbol { get; }

            public NpcEntry(string id, string name, Texture2D? texture, string description, string affectionLevel, string questInfo, string heartSymbol = "♥")
            {
                Id = id;
                Name = name;
                Texture = texture;
                Description = description;
                AffectionLevel = affectionLevel;
                QuestInfo = questInfo;
                HeartSymbol = heartSymbol;
            }
        }
    }
}

