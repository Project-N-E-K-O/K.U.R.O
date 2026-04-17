using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Kuros.Actors.Heroes;
using Kuros.Managers;

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

        /// <summary>
        /// 获取窗口是否开启
        /// </summary>
        public bool IsOpen => _isOpen;

        // 技能数据（从SkillWindow获取或独立管理）
        private readonly List<SkillDetailData> _allSkills = new();

        [Signal] public delegate void SkillDetailClosedEventHandler();

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;
            
            CacheNodeReferences();
            RefreshSkillData();
            UpdateSkillDisplay();
            HideWindow();
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
            SkillsScrollContainer ??= GetNodeOrNull<ScrollContainer>("MainPanel/Body/SkillsScroll");
            SkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsScroll/SkillsContainer");

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(CloseButton, nameof(HideWindow));
        }

        private void RefreshSkillData()
        {
            _allSkills.Clear();
            InitializePlaceholderSkills();
        }

        /// <summary>
        /// 初始化技能数据：显示当前已拥有（已生效）的构筑效果（支持多个构筑类型）
        /// </summary>
        private void InitializePlaceholderSkills()
        {
            var buildController = FindPlayerBuildController();
            if (buildController == null)
            {
                _allSkills.Add(new SkillDetailData
                {
                    Id = "build_empty_no_controller",
                    Name = "构筑效果",
                    Description = "未找到玩家构筑控制器，暂时无法读取构筑数据。",
                    Icon = null,
                    IsActive = false,
                    Damage = "N/A",
                    Range = "N/A",
                    ManaCost = "N/A",
                    IsUnlocked = false,
                    BuildLevel = 0,
                    RequiredPoints = 0,
                    CurrentPoints = 0
                });
                return;
            }

            buildController.RefreshBuildState();
            int totalPoints = buildController.CurrentBuildCount;
            var activeBuildClasses = buildController.ActiveBuildClasses;
            var buildCountByClass = buildController.BuildCountByClass;
            var buildLevelByClass = buildController.BuildLevelByClass;

            var entries = new List<BuildLevelEffectEntry>();
            foreach (var entry in buildController.LevelEntries)
            {
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            entries.Sort((a, b) =>
            {
                // 先按构筑类型（BuildClass）排序
                int byBuildClass = string.Compare(
                    a.BuildClass?.Trim() ?? "", 
                    b.BuildClass?.Trim() ?? "", 
                    StringComparison.OrdinalIgnoreCase);
                if (byBuildClass != 0) return byBuildClass;
                
                // 再按等级（Level）排序
                return a.Level.CompareTo(b.Level);
            });

            // 遍历所有条目，为每个活跃的构筑类型显示其对应的效果
            foreach (var entry in entries)
            {
                // 如果条目没有指定构筑类型，跳过
                if (string.IsNullOrWhiteSpace(entry.BuildClass))
                {
                    continue;
                }

                string buildClass = entry.BuildClass.Trim();

                // 检查该构筑类型是否活跃
                bool isActiveBuildClass = activeBuildClasses.Any(bc => string.Equals(bc, buildClass, StringComparison.OrdinalIgnoreCase));
                if (!isActiveBuildClass)
                {
                    continue;
                }

                // 获取该构筑的点数
                if (!buildCountByClass.ContainsKey(buildClass))
                {
                    continue;
                }

                int classPoints = buildCountByClass[buildClass];
                bool isUnlocked = classPoints >= entry.RequiredPoints;

                // 只显示已生效的构筑效果
                if (!isUnlocked)
                {
                    continue;
                }

                _allSkills.Add(new SkillDetailData
                {
                    Id = $"{buildClass}_level_{entry.Level}",
                    Name = string.IsNullOrWhiteSpace(entry.BuildName) 
                        ? $"{buildClass} 等级 {entry.Level}" 
                        : entry.BuildName,
                    Description = string.IsNullOrWhiteSpace(entry.Description)
                        ? $"[{buildClass}] 构筑效果等级 {entry.Level}"
                        : entry.Description,
                    Icon = LoadBuildIcon(entry.IconPath),
                    Cooldown = 0.0f,
                    IsActive = false,
                    Damage = "N/A",
                    Range = "N/A",
                    ManaCost = "N/A",
                    IsUnlocked = isUnlocked,
                    BuildLevel = entry.Level,
                    RequiredPoints = entry.RequiredPoints,
                    CurrentPoints = classPoints
                });
            }

            if (_allSkills.Count == 0)
            {
                _allSkills.Add(new SkillDetailData
                {
                    Id = "build_empty_none_unlocked",
                    Name = "构筑效果",
                    Description = "当前尚未拥有已生效的构筑效果。",
                    Icon = null,
                    IsActive = false,
                    Damage = "N/A",
                    Range = "N/A",
                    ManaCost = "N/A",
                    IsUnlocked = false,
                    BuildLevel = 0,
                    RequiredPoints = 0,
                    CurrentPoints = totalPoints
                });
            }
        }

        private PlayerBuildController? FindPlayerBuildController()
        {
            var tree = GetTree();
            if (tree == null)
            {
                return null;
            }

            var root = tree.CurrentScene ?? tree.Root;
            if (root == null)
            {
                return null;
            }

            return FindBuildControllerInTree(root);
        }

        private static PlayerBuildController? FindBuildControllerInTree(Node node)
        {
            if (node is PlayerBuildController controller)
            {
                return controller;
            }

            foreach (Node child in node.GetChildren())
            {
                var found = FindBuildControllerInTree(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Texture2D? LoadBuildIcon(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            var normalizedPath = iconPath.Trim();
            var texture = ResourceLoader.Load<Texture2D>(normalizedPath);
            if (texture != null)
            {
                return texture;
            }

            if (normalizedPath.StartsWith("uid://", StringComparison.OrdinalIgnoreCase))
            {
                long uid = ResourceUid.TextToId(normalizedPath);
                if (uid != ResourceUid.InvalidId)
                {
                    string resolvedPath = ResourceUid.GetIdPath(uid);
                    if (!string.IsNullOrWhiteSpace(resolvedPath))
                    {
                        return ResourceLoader.Load<Texture2D>(resolvedPath);
                    }
                }
            }

            GD.PushWarning($"SkillDetailWindow: 无法加载构筑图标: {normalizedPath}");
            return null;
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
            typeLabel.Text = skill.IsActive
                ? "主技能"
                : skill.BuildLevel > 0
                    ? $"构筑效果 Lv.{skill.BuildLevel} {(skill.IsUnlocked ? "已生效" : "未生效")}"
                    : "被动技能";
            typeLabel.AddThemeFontSizeOverride("font_size", 18);
            typeLabel.AddThemeColorOverride(
                "font_color",
                skill.IsActive
                    ? new Color(0.3f, 0.7f, 1.0f)
                    : skill.BuildLevel > 0
                        ? (skill.IsUnlocked ? new Color(0.3f, 1.0f, 0.6f) : new Color(1.0f, 0.55f, 0.3f))
                        : new Color(1.0f, 0.7f, 0.3f));
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

            RefreshSkillData();
            UpdateSkillDisplay();

            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            _isOpen = true;
            
            // 请求暂停游戏
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PushPause();
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
            SetProcessUnhandledInput(false);
            _isOpen = false;
            
            // 取消暂停请求
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PopPause();
            }
            
            EmitSignal(SignalName.SkillDetailClosed);
        }

        public override void _Input(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: true))
            {
                return;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: false))
            {
                return;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: false, useSetInputAsHandled: true))
            {
                return;
            }
        }

        /// <summary>
        /// 尝试处理关闭窗口的输入（ESC键或物品栏键）
        /// </summary>
        /// <param name="event">输入事件</param>
        /// <param name="useAcceptEvent">是否调用AcceptEvent</param>
        /// <param name="useSetInputAsHandled">是否调用SetInputAsHandled</param>
        /// <returns>如果输入被处理返回true，否则返回false</returns>
        private bool TryHandleCloseInput(InputEvent @event, bool useAcceptEvent, bool useSetInputAsHandled)
        {
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                return false;
            }

            bool isEscKey = @event.IsActionPressed("ui_cancel") ||
                (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape);
            bool isInventoryKey = @event.IsActionPressed("open_inventory");

            if (isEscKey || isInventoryKey)
            {
                HideWindow();
                if (useSetInputAsHandled) GetViewport().SetInputAsHandled();
                if (useAcceptEvent) AcceptEvent();
                return true;
            }
            return false;
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

        /// <summary>
        /// 技能详情数据类
        /// </summary>
        internal class SkillDetailData
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
            public bool IsUnlocked { get; set; } = false;
            public int BuildLevel { get; set; } = 0;
            public int RequiredPoints { get; set; } = 0;
            public int CurrentPoints { get; set; } = 0;
        }
    }
}

