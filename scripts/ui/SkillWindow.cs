using Godot;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 技能界面窗口 - 显示主技能和被动技能
    /// </summary>
    public partial class SkillWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public VBoxContainer ActiveSkillsContainer { get; private set; } = null!;
        [Export] public VBoxContainer PassiveSkillsContainer { get; private set; } = null!;
        [Export] public Label ActiveSkillsTitle { get; private set; } = null!;
        [Export] public Label PassiveSkillsTitle { get; private set; } = null!;
        [Export] public Button DetailButton { get; private set; } = null!;

        private bool _isOpen = false;
        private SkillDetailWindow? _skillDetailWindow;
        private const string SkillDetailWindowPath = "res://scenes/ui/windows/SkillDetailWindow.tscn";
        private InventoryWindow? _cachedInventoryWindow;

        // 技能数据（占位数据，等待接入真实技能接口）
        private readonly List<SkillData> _activeSkills = new();
        private readonly List<SkillData> _passiveSkills = new();
        
        // 技能卡片引用，用于更新冷却时间
        private readonly Dictionary<string, SkillCardControl> _skillCards = new();

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;
            
            CacheNodeReferences();
            InitializePlaceholderSkills();
            UpdateSkillDisplay();
            // 默认显示在战斗场景中
            Visible = true;
            _isOpen = true;
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            
            // 如果游戏暂停（菜单栏或物品栏打开），不更新冷却时间
            var tree = GetTree();
            if (tree != null && tree.Paused)
            {
                return;
            }
            
            // 如果物品栏打开，不更新冷却时间
            if (IsInventoryWindowOpen())
            {
                return;
            }
            
            // 实时更新技能冷却时间
            UpdateCooldowns((float)delta);
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
            ActiveSkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsVBox/ActiveSkillsSection/ActiveSkillsScroll/ActiveSkillsContainer");
            PassiveSkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsScroll/PassiveSkillsContainer");
            ActiveSkillsTitle ??= GetNodeOrNull<Label>("MainPanel/Body/SkillsVBox/ActiveSkillsSection/ActiveSkillsTitle");
            PassiveSkillsTitle ??= GetNodeOrNull<Label>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsTitle");
            DetailButton ??= GetNodeOrNull<Button>("MainPanel/Body/DetailButton");

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(CloseButton, nameof(HideWindow));
            ConnectButtonSignal(DetailButton, nameof(OnDetailButtonPressed));
        }

        /// <summary>
        /// 初始化占位技能数据（等待接入真实技能接口）
        /// </summary>
        private void InitializePlaceholderSkills()
        {
            // 占位主技能
            _activeSkills.Add(new SkillData
            {
                Id = "skill_placeholder_1",
                Name = "主技能1",
                Description = "这是一个主技能的描述。主技能需要主动释放。",
                Icon = null,
                Cooldown = 5.0f,
                CurrentCooldown = 0.0f,
                IsActive = true
            });

            _activeSkills.Add(new SkillData
            {
                Id = "skill_placeholder_2",
                Name = "主技能2",
                Description = "这是另一个主技能的描述。",
                Icon = null,
                Cooldown = 10.0f,
                CurrentCooldown = 0.0f,
                IsActive = true
            });

            // 占位被动技能
            _passiveSkills.Add(new SkillData
            {
                Id = "passive_placeholder_1",
                Name = "被动技能1",
                Description = "这是一个被动技能的描述。被动技能会自动生效。",
                Icon = null,
                Cooldown = 0.0f,
                CurrentCooldown = 0.0f,
                IsActive = false
            });

            _passiveSkills.Add(new SkillData
            {
                Id = "passive_placeholder_2",
                Name = "被动技能2",
                Description = "这是另一个被动技能的描述。",
                Icon = null,
                Cooldown = 0.0f,
                CurrentCooldown = 0.0f,
                IsActive = false
            });
        }

        /// <summary>
        /// 更新技能显示
        /// </summary>
        private void UpdateSkillDisplay()
        {
            // 清空现有显示
            if (ActiveSkillsContainer != null)
            {
                foreach (Node child in ActiveSkillsContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            if (PassiveSkillsContainer != null)
            {
                foreach (Node child in PassiveSkillsContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            // 清空技能卡片引用，避免引用已释放的节点
            _skillCards.Clear();

            // 显示主技能
            if (ActiveSkillsContainer != null)
            {
                foreach (var skill in _activeSkills)
                {
                    var skillCard = CreateSkillCard(skill);
                    ActiveSkillsContainer.AddChild(skillCard);
                    _skillCards[skill.Id] = skillCard;
                }
            }

            // 显示被动技能
            if (PassiveSkillsContainer != null)
            {
                foreach (var skill in _passiveSkills)
                {
                    var skillCard = CreateSkillCard(skill);
                    PassiveSkillsContainer.AddChild(skillCard);
                    _skillCards[skill.Id] = skillCard;
                }
            }
        }

        /// <summary>
        /// 更新所有技能的冷却时间
        /// </summary>
        private void UpdateCooldowns(float delta)
        {
            foreach (var skill in _activeSkills)
            {
                if (_skillCards.TryGetValue(skill.Id, out var card))
                {
                    // 更新冷却时间（模拟冷却倒计时）
                    if (skill.CurrentCooldown > 0)
                    {
                        skill.CurrentCooldown = Mathf.Max(0, skill.CurrentCooldown - delta);
                        card.UpdateCooldown(skill.CurrentCooldown, skill.Cooldown);
                    }
                    else if (skill.CurrentCooldown <= 0 && skill.Cooldown > 0)
                    {
                        // 冷却完成，显示就绪状态
                        card.UpdateCooldown(0, skill.Cooldown);
                    }
                }
            }
        }

        /// <summary>
        /// 创建技能卡片
        /// </summary>
        private SkillCardControl CreateSkillCard(SkillData skill)
        {
            var card = new SkillCardControl();
            card.CustomMinimumSize = new Vector2(300, 100);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_top", 12);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_bottom", 12);
            card.AddChild(margin);

            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 12);
            margin.AddChild(hbox);

            // 技能图标
            var iconRect = new TextureRect();
            iconRect.CustomMinimumSize = new Vector2(80, 80);
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            if (skill.Icon != null)
            {
                iconRect.Texture = skill.Icon;
            }
            hbox.AddChild(iconRect);

            // 技能信息
            var vbox = new VBoxContainer();
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.AddChild(vbox);

            // 技能名称
            var nameLabel = new Label();
            nameLabel.Text = skill.Name;
            nameLabel.AddThemeFontSizeOverride("font_size", 20);
            vbox.AddChild(nameLabel);

            // 技能描述
            var descLabel = new RichTextLabel();
            descLabel.Text = skill.Description;
            descLabel.BbcodeEnabled = true;
            descLabel.FitContent = true;
            descLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            vbox.AddChild(descLabel);

            // 冷却时间（仅主技能显示）
            if (skill.IsActive && skill.Cooldown > 0)
            {
                var cooldownLabel = new Label();
                cooldownLabel.Name = "CooldownLabel";
                cooldownLabel.Text = $"冷却: {skill.Cooldown:F1}秒";
                cooldownLabel.AddThemeFontSizeOverride("font_size", 16);
                cooldownLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.0f));
                vbox.AddChild(cooldownLabel);
                card.SetCooldownLabel(cooldownLabel);
            }

            card.Initialize(skill);
            
            // 添加点击事件来测试冷却（仅主技能）
            if (skill.IsActive)
            {
                card.GuiInput += (InputEvent @event) =>
                {
                    // 如果游戏暂停（菜单栏或物品栏打开），阻止技能使用
                    var tree = GetTree();
                    if (tree != null && tree.Paused)
                    {
                        return;
                    }
                    
                    // 如果物品栏打开，阻止技能使用
                    if (IsInventoryWindowOpen())
                    {
                        return;
                    }
                    
                    if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
                    {
                        // 触发技能冷却
                        TriggerSkillCooldown(skill.Id);
                        GetViewport().SetInputAsHandled();
                    }
                };
            }
            
            return card;
        }

        /// <summary>
        /// 触发技能冷却（测试用，等待接入真实技能接口）
        /// </summary>
        public void TriggerSkillCooldown(string skillId)
        {
            // 如果游戏暂停（菜单栏或物品栏打开），阻止技能使用
            var tree = GetTree();
            if (tree != null && tree.Paused)
            {
                GD.Print("SkillWindow.TriggerSkillCooldown: 游戏已暂停，无法使用技能");
                return;
            }
            
            // 如果物品栏打开，阻止技能使用
            if (IsInventoryWindowOpen())
            {
                GD.Print("SkillWindow.TriggerSkillCooldown: 物品栏打开，无法使用技能");
                return;
            }
            
            foreach (var skill in _activeSkills)
            {
                if (skill.Id == skillId && skill.CurrentCooldown <= 0)
                {
                    skill.CurrentCooldown = skill.Cooldown;
                    if (_skillCards.TryGetValue(skillId, out var card))
                    {
                        card.UpdateCooldown(skill.CurrentCooldown, skill.Cooldown);
                    }
                    GD.Print($"技能 {skill.Name} 已使用，冷却时间: {skill.Cooldown}秒");
                    break;
                }
            }
        }

        /// <summary>
        /// 检查物品栏是否打开
        /// </summary>
        private bool IsInventoryWindowOpen()
        {
            // Try cached reference first
            if (_cachedInventoryWindow != null && IsInstanceValid(_cachedInventoryWindow))
            {
                return _cachedInventoryWindow.Visible;
            }
            
            // Fallback: Find via group (requires InventoryWindow to be added to "inventory_window" group)
            _cachedInventoryWindow = GetTree().GetFirstNodeInGroup("inventory_window") as InventoryWindow;
            if (_cachedInventoryWindow != null)
            {
                return _cachedInventoryWindow.Visible;
            }
            
            // Final fallback: Simple search without full tree traversal
            // This is still more efficient than recursive traversal
            var root = GetTree().Root;
            if (root != null)
            {
                _cachedInventoryWindow = FindInventoryWindowInNode(root);
                if (_cachedInventoryWindow != null)
                {
                    return _cachedInventoryWindow.Visible;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 在节点及其所有子节点中递归查找物品栏窗口
        /// </summary>
        private InventoryWindow? FindInventoryWindowInNode(Node node)
        {
            // 检查当前节点
            if (node is InventoryWindow inventoryWindow)
            {
                return inventoryWindow;
            }
            
            // 递归检查所有子节点
            foreach (Node child in node.GetChildren())
            {
                var found = FindInventoryWindowInNode(child);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }

        /// <summary>
        /// 显示窗口
        /// </summary>
        public void ShowWindow()
        {
            if (_isOpen) return;

            Visible = true;
            _isOpen = true;
            // 注意：不在这里暂停游戏，因为BattleMenu已经管理了暂停状态
        }

        /// <summary>
        /// 隐藏窗口
        /// </summary>
        public void HideWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            _isOpen = false;
            // 注意：不在这里取消暂停，因为BattleMenu已经管理了暂停状态
        }

        /// <summary>
        /// 切换窗口显示状态
        /// </summary>
        public void ToggleWindow()
        {
            if (_isOpen)
                HideWindow();
            else
                ShowWindow();
        }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// 技能数据类（占位，等待接入真实技能接口）
        /// </summary>
        private class SkillData
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public Texture2D? Icon { get; set; }
            public float Cooldown { get; set; } = 0.0f;
            public float CurrentCooldown { get; set; } = 0.0f;
            public bool IsActive { get; set; } = true;
        }

        /// <summary>
        /// 技能卡片控件
        /// </summary>
        private partial class SkillCardControl : Panel
        {
            private Label? _cooldownLabel;
            private SkillData? _skillData;

            public void Initialize(SkillData skill)
            {
                _skillData = skill;
                // 初始化时设置冷却时间为0（技能就绪）
                if (skill.IsActive && skill.Cooldown > 0)
                {
                    skill.CurrentCooldown = 0;
                }
            }

            public void SetCooldownLabel(Label label)
            {
                _cooldownLabel = label;
            }

            public void UpdateCooldown(float currentCooldown, float maxCooldown)
            {
                if (_cooldownLabel == null || _skillData == null) return;

                if (currentCooldown > 0)
                {
                    _cooldownLabel.Text = $"冷却: {currentCooldown:F1}秒";
                    _cooldownLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f)); // 红色表示冷却中
                }
                else
                {
                    _cooldownLabel.Text = "就绪";
                    _cooldownLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.3f)); // 绿色表示就绪
                }
            }
        }

        /// <summary>
        /// 从武器系统获取技能（等待接入真实技能接口）
        /// TODO: 当找到技能接口后，实现此方法
        /// </summary>
        private void LoadSkillsFromWeaponSystem()
        {
            // TODO: 从武器系统获取主技能和被动技能
            // 示例代码（需要根据实际接口调整）:
            // var weapon = GetEquippedWeapon();
            // if (weapon != null)
            // {
            //     _activeSkills = weapon.GetActiveSkills();
            //     _passiveSkills = weapon.GetPassiveSkills();
            // }
        }

        /// <summary>
        /// 打开技能详情页面
        /// </summary>
        private void OnDetailButtonPressed()
        {
            // 如果技能详情窗口已经打开，先关闭它
            if (_skillDetailWindow != null && _skillDetailWindow.IsOpen)
            {
                _skillDetailWindow.HideWindow();
                return;
            }

            // 加载技能详情窗口
            if (_skillDetailWindow == null)
            {
                var scene = GD.Load<PackedScene>(SkillDetailWindowPath);
                if (scene == null)
                {
                    GD.PrintErr("无法加载技能详情窗口场景：", SkillDetailWindowPath);
                    return;
                }

                _skillDetailWindow = scene.Instantiate<SkillDetailWindow>();
                
                // 将窗口添加到与技能窗口相同的层级（GameUI层）
                // SkillWindow的父节点应该是GameUILayer（CanvasLayer）
                var parent = GetParent();
                if (parent != null)
                {
                    parent.AddChild(_skillDetailWindow);
                    GD.Print("SkillWindow.OnDetailButtonPressed: 已添加技能详情窗口到父节点（GameUI层）");
                }
                else
                {
                    GD.PrintErr("SkillWindow.OnDetailButtonPressed: 无法找到父节点");
                    return;
                }
            }

            // 显示技能详情窗口
            _skillDetailWindow.ShowWindow();
            GD.Print("SkillWindow.OnDetailButtonPressed: 已打开技能详情窗口");
        }
    }
}

