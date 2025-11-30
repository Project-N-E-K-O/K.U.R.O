using Godot;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 技能详情窗口 - 显示所有技能的详细信息
    /// 层级与物品栏相同（GameUI层）
    /// </summary>
    public partial class SkillDetailWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public ScrollContainer SkillsScrollContainer { get; private set; } = null!;
        [Export] public VBoxContainer SkillsContainer { get; private set; } = null!;

        private bool _isOpen = false;

        // 技能数据（从SkillWindow获取或独立管理）
        private readonly List<SkillDetailData> _allSkills = new();

        [Signal] public delegate void SkillDetailClosedEventHandler();

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;
            
            CacheNodeReferences();
            InitializePlaceholderSkills();
            UpdateSkillDisplay();
            HideWindow();
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            SkillsScrollContainer ??= GetNodeOrNull<ScrollContainer>("MainPanel/Body/SkillsScroll");
            SkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsScroll/SkillsContainer");

            if (CloseButton != null)
            {
                CloseButton.Pressed += HideWindow;
            }
        }

        /// <summary>
        /// 初始化占位技能数据
        /// </summary>
        private void InitializePlaceholderSkills()
        {
            // 占位主技能
            _allSkills.Add(new SkillDetailData
            {
                Id = "skill_placeholder_1",
                Name = "主技能1",
                Description = "这是一个主技能的详细描述。主技能需要主动释放，具有冷却时间。",
                Icon = null,
                Cooldown = 5.0f,
                IsActive = true,
                Damage = "100",
                Range = "5米",
                ManaCost = "20"
            });

            _allSkills.Add(new SkillDetailData
            {
                Id = "skill_placeholder_2",
                Name = "主技能2",
                Description = "这是另一个主技能的详细描述。",
                Icon = null,
                Cooldown = 10.0f,
                IsActive = true,
                Damage = "200",
                Range = "10米",
                ManaCost = "40"
            });

            // 占位被动技能
            _allSkills.Add(new SkillDetailData
            {
                Id = "passive_placeholder_1",
                Name = "被动技能1",
                Description = "这是一个被动技能的详细描述。被动技能会自动生效，无需主动释放。",
                Icon = null,
                Cooldown = 0.0f,
                IsActive = false,
                Damage = "N/A",
                Range = "N/A",
                ManaCost = "N/A"
            });

            _allSkills.Add(new SkillDetailData
            {
                Id = "passive_placeholder_2",
                Name = "被动技能2",
                Description = "这是另一个被动技能的详细描述。",
                Icon = null,
                Cooldown = 0.0f,
                IsActive = false,
                Damage = "N/A",
                Range = "N/A",
                ManaCost = "N/A"
            });
        }

        /// <summary>
        /// 更新技能显示
        /// </summary>
        private void UpdateSkillDisplay()
        {
            // 清空现有显示
            if (SkillsContainer != null)
            {
                foreach (Node child in SkillsContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            // 显示所有技能
            if (SkillsContainer != null)
            {
                foreach (var skill in _allSkills)
                {
                    var skillCard = CreateSkillDetailCard(skill);
                    SkillsContainer.AddChild(skillCard);
                }
            }
        }

        /// <summary>
        /// 创建技能详情卡片
        /// </summary>
        private Control CreateSkillDetailCard(SkillDetailData skill)
        {
            var card = new Panel();
            card.CustomMinimumSize = new Vector2(600, 200);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 16);
            margin.AddThemeConstantOverride("margin_top", 16);
            margin.AddThemeConstantOverride("margin_right", 16);
            margin.AddThemeConstantOverride("margin_bottom", 16);
            card.AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 12);
            margin.AddChild(vbox);

            // 技能名称和类型
            var headerHbox = new HBoxContainer();
            headerHbox.AddThemeConstantOverride("separation", 12);
            vbox.AddChild(headerHbox);

            // 技能图标
            var iconRect = new TextureRect();
            iconRect.CustomMinimumSize = new Vector2(80, 80);
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            if (skill.Icon != null)
            {
                iconRect.Texture = skill.Icon;
            }
            headerHbox.AddChild(iconRect);

            // 技能名称和类型标签
            var nameVbox = new VBoxContainer();
            nameVbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            headerHbox.AddChild(nameVbox);

            var nameLabel = new Label();
            nameLabel.Text = skill.Name;
            nameLabel.AddThemeFontSizeOverride("font_size", 24);
            nameVbox.AddChild(nameLabel);

            var typeLabel = new Label();
            typeLabel.Text = skill.IsActive ? "主技能" : "被动技能";
            typeLabel.AddThemeFontSizeOverride("font_size", 18);
            typeLabel.AddThemeColorOverride("font_color", skill.IsActive ? new Color(0.3f, 0.7f, 1.0f) : new Color(1.0f, 0.7f, 0.3f));
            nameVbox.AddChild(typeLabel);

            // 技能描述
            var descLabel = new RichTextLabel();
            descLabel.Text = skill.Description;
            descLabel.BbcodeEnabled = true;
            descLabel.FitContent = true;
            descLabel.CustomMinimumSize = new Vector2(0, 60);
            vbox.AddChild(descLabel);

            // 技能属性（仅主技能显示）
            if (skill.IsActive)
            {
                var statsHbox = new HBoxContainer();
                statsHbox.AddThemeConstantOverride("separation", 24);
                vbox.AddChild(statsHbox);

                // 冷却时间
                var cooldownLabel = new Label();
                cooldownLabel.Text = $"冷却时间: {skill.Cooldown:F1}秒";
                cooldownLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(cooldownLabel);

                // 伤害
                var damageLabel = new Label();
                damageLabel.Text = $"伤害: {skill.Damage}";
                damageLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(damageLabel);

                // 范围
                var rangeLabel = new Label();
                rangeLabel.Text = $"范围: {skill.Range}";
                rangeLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(rangeLabel);

                // 法力消耗
                var manaLabel = new Label();
                manaLabel.Text = $"法力消耗: {skill.ManaCost}";
                manaLabel.AddThemeFontSizeOverride("font_size", 16);
                statsHbox.AddChild(manaLabel);
            }

            return card;
        }

        /// <summary>
        /// 显示窗口
        /// </summary>
        public void ShowWindow()
        {
            if (_isOpen) return;

            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            _isOpen = true;
            
            // 设置暂停，确保游戏时间停止（与物品栏相同）
            var tree = GetTree();
            if (tree != null)
            {
                tree.Paused = true;
            }
            
            // 尝试将窗口移到父节点的最后，确保输入处理优先级
            var parent = GetParent();
            if (parent != null)
            {
                parent.MoveChild(this, parent.GetChildCount() - 1);
            }
        }

        /// <summary>
        /// 隐藏窗口
        /// </summary>
        public void HideWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            _isOpen = false;
            
            // 取消暂停，恢复游戏时间
            var tree = GetTree();
            if (tree != null)
            {
                // 检查是否有其他UI需要保持暂停
                bool shouldKeepPaused = ShouldKeepPaused();
                if (!shouldKeepPaused)
                {
                    tree.Paused = false;
                }
            }
            
            EmitSignal(SignalName.SkillDetailClosed);
        }

        public override void _Input(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            // ESC键关闭窗口（同时检查action和keycode，确保能捕获ESC键）
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
                AcceptEvent();
                return;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            // ESC键关闭窗口（GUI输入备用处理）
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
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            // ESC键关闭窗口（未处理输入的备用处理）
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
        }

        /// <summary>
        /// 检查物品栏是否打开
        /// </summary>
        private bool IsInventoryWindowOpen()
        {
            var root = GetTree().Root;
            if (root != null)
            {
                var inventoryWindows = FindAllInventoryWindowsInTree(root);
                
                foreach (var inventoryWindow in inventoryWindows)
                {
                    if (inventoryWindow.Visible)
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 在场景树中查找所有物品栏窗口
        /// </summary>
        private System.Collections.Generic.List<InventoryWindow> FindAllInventoryWindowsInTree(Node node)
        {
            var result = new System.Collections.Generic.List<InventoryWindow>();
            
            // 检查当前节点
            if (node is InventoryWindow inventoryWindow)
            {
                result.Add(inventoryWindow);
            }
            
            // 递归检查子节点
            foreach (Node child in node.GetChildren())
            {
                result.AddRange(FindAllInventoryWindowsInTree(child));
            }
            
            return result;
        }

        /// <summary>
        /// 检查是否应该保持暂停状态
        /// </summary>
        private bool ShouldKeepPaused()
        {
            // 检查物品获得弹窗是否打开
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                return true;
            }

            // 检查物品栏是否打开
            if (IsInventoryWindowOpen())
            {
                return true;
            }

            // 检查菜单是否打开
            var battleMenu = Kuros.Managers.UIManager.Instance?.GetUI<BattleMenu>("BattleMenu");
            if (battleMenu != null && battleMenu.Visible)
            {
                return true;
            }

            // 检查对话是否激活
            if (Managers.DialogueManager.Instance != null && Managers.DialogueManager.Instance.IsDialogueActive)
            {
                return true;
            }

            // 注意：不检查技能窗口（SkillWindow），因为技能窗口本身不暂停游戏
            // SkillWindow 默认是打开的，但它不管理暂停状态

            return false;
        }

        /// <summary>
        /// 检查技能窗口是否打开
        /// </summary>
        private bool IsSkillWindowOpen()
        {
            var root = GetTree().Root;
            if (root != null)
            {
                var skillWindows = FindAllSkillWindowsInTree(root);
                
                foreach (var skillWindow in skillWindows)
                {
                    if (skillWindow.Visible && skillWindow.IsOpen)
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 在场景树中查找所有技能窗口
        /// </summary>
        private System.Collections.Generic.List<SkillWindow> FindAllSkillWindowsInTree(Node node)
        {
            var result = new System.Collections.Generic.List<SkillWindow>();
            
            // 检查当前节点
            if (node is SkillWindow skillWindow)
            {
                result.Add(skillWindow);
            }
            
            // 递归检查子节点
            foreach (Node child in node.GetChildren())
            {
                result.AddRange(FindAllSkillWindowsInTree(child));
            }
            
            return result;
        }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// 技能详情数据类
        /// </summary>
        private class SkillDetailData
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public Texture2D? Icon { get; set; }
            public float Cooldown { get; set; } = 0.0f;
            public bool IsActive { get; set; } = true;
            public string Damage { get; set; } = "0";
            public string Range { get; set; } = "0";
            public string ManaCost { get; set; } = "0";
        }
    }
}

