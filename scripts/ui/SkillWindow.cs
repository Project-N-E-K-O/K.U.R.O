using Godot;
using System;
using System.Collections.Generic;
using Kuros.Actors.Heroes;

namespace Kuros.UI
{
    /// <summary>
    /// 技能界面窗口 - 显示主技能和被动技能
    /// </summary>
    public partial class SkillWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;
        [Export] public VBoxContainer PassiveSkillsContainer { get; private set; } = null!;
        [Export] public Label PassiveSkillsTitle { get; private set; } = null!;
        [Export] public Button DetailButton { get; private set; } = null!;

        private bool _isOpen = false;
        private SkillDetailWindow? _skillDetailWindow;
        private const string SkillDetailWindowPath = "res://scenes/ui/windows/SkillDetailWindow.tscn";
        private InventoryWindow? _cachedInventoryWindow;
        private PlayerBuildController? _buildController;
        private readonly List<OwnedBuildViewData> _ownedBuilds = new();

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;
            
            CacheNodeReferences();
            SubscribeBuildControllerSignals();
            RefreshBuildIcons();
            // 默认显示在战斗场景中
            Visible = true;
            _isOpen = true;
        }

        public override void _ExitTree()
        {
            UnsubscribeBuildControllerSignals();
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            base._UnhandledInput(@event);

            if (!_isOpen || !Visible)
            {
                return;
            }

            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Tab)
            {
                OnDetailButtonPressed();
                GetViewport().SetInputAsHandled();
            }
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
            PassiveSkillsContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsScroll/PassiveSkillsContainer");
            PassiveSkillsTitle ??= GetNodeOrNull<Label>("MainPanel/Body/SkillsVBox/PassiveSkillsSection/PassiveSkillsTitle");
            DetailButton ??= GetNodeOrNull<Button>("MainPanel/Body/DetailButton");

            // 避免空格等 ui_accept 键触发详情按钮，导致与攻击键冲突。
            if (DetailButton != null)
            {
                DetailButton.FocusMode = Control.FocusModeEnum.None;
            }

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(CloseButton, nameof(HideWindow));
            ConnectButtonSignal(DetailButton, nameof(OnDetailButtonPressed));
        }

        /// <summary>
        /// 初始化构筑图标数据：根据当前武器构筑，仅显示已拥有的 build 图标。
        /// </summary>
        private void InitializePlaceholderSkills()
        {
            _ownedBuilds.Clear();

            _buildController ??= FindPlayerBuildController();
            var buildController = _buildController;
            if (buildController == null)
            {
                return;
            }

            buildController.RefreshBuildState();
            int currentPoints = buildController.CurrentBuildCount;
            string currentBuildClass = ResolveCurrentBuildClass(buildController);

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
                int byPoints = a.RequiredPoints.CompareTo(b.RequiredPoints);
                if (byPoints != 0) return byPoints;
                return a.Level.CompareTo(b.Level);
            });

            foreach (var entry in entries)
            {
                if (currentPoints < entry.RequiredPoints)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentBuildClass) &&
                    !string.IsNullOrWhiteSpace(entry.BuildClass) &&
                    !string.Equals(entry.BuildClass.Trim(), currentBuildClass, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _ownedBuilds.Add(new OwnedBuildViewData
                {
                    Name = string.IsNullOrWhiteSpace(entry.BuildName) ? $"构筑 Lv.{entry.Level}" : entry.BuildName,
                    IconPath = entry.IconPath ?? string.Empty,
                    Icon = LoadBuildIcon(entry.IconPath ?? string.Empty)
                });
            }
        }

        private void RefreshBuildIcons()
        {
            InitializePlaceholderSkills();
            UpdateSkillDisplay();
        }

        private void SubscribeBuildControllerSignals()
        {
            var controller = FindPlayerBuildController();
            if (controller == _buildController && controller != null)
            {
                return;
            }

            UnsubscribeBuildControllerSignals();
            _buildController = controller;
            if (_buildController == null)
            {
                return;
            }

            _buildController.BuildCountChanged += OnBuildCountChanged;
            _buildController.BuildLevelChanged += OnBuildLevelChanged;
        }

        private void UnsubscribeBuildControllerSignals()
        {
            if (_buildController == null)
            {
                return;
            }

            _buildController.BuildCountChanged -= OnBuildCountChanged;
            _buildController.BuildLevelChanged -= OnBuildLevelChanged;
            _buildController = null;
        }

        private void OnBuildCountChanged(int _)
        {
            if (IsInsideTree())
            {
                CallDeferred(nameof(RefreshBuildIcons));
            }
        }

        private void OnBuildLevelChanged(int _)
        {
            if (IsInsideTree())
            {
                CallDeferred(nameof(RefreshBuildIcons));
            }
        }

        private static string ResolveCurrentBuildClass(PlayerBuildController buildController)
        {
            if (!string.IsNullOrWhiteSpace(buildController.BuildClass))
            {
                return buildController.BuildClass.Trim();
            }

            var activeWeapon = buildController.Inventory?.GetActiveCombatWeaponDefinition();
            if (!string.IsNullOrWhiteSpace(activeWeapon?.BuildClass))
            {
                return activeWeapon.BuildClass.Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// 更新技能显示
        /// </summary>
        private void UpdateSkillDisplay()
        {
            if (PassiveSkillsContainer != null)
            {
                foreach (Node child in PassiveSkillsContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            if (PassiveSkillsTitle != null)
            {
                PassiveSkillsTitle.Text = string.Empty;
                PassiveSkillsTitle.Visible = false;
            }

            if (PassiveSkillsContainer != null && PassiveSkillsContainer.GetParent() is Control passiveSection)
            {
                passiveSection.Visible = true;
                PassiveSkillsContainer.AddChild(CreateBuildIconsPanel());
            }
        }

        private Control CreateBuildIconsPanel()
        {
            var margin = new MarginContainer();
            margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_top", 12);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_bottom", 12);

            var root = new VBoxContainer();
            root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            root.AddThemeConstantOverride("separation", 16);
            margin.AddChild(root);

            if (_ownedBuilds.Count == 0)
            {
                return margin;
            }

            for (int i = 0; i < _ownedBuilds.Count; i++)
            {
                var centerContainer = new CenterContainer();
                centerContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                centerContainer.CustomMinimumSize = new Vector2(0, 96);
                root.AddChild(centerContainer);

                var iconRect = new TextureRect();
                iconRect.CustomMinimumSize = new Vector2(96, 96);
                iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                iconRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
                iconRect.Texture = _ownedBuilds[i].Icon;
                centerContainer.AddChild(iconRect);
            }

            return margin;
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
                if (uid == ResourceUid.InvalidId)
                {
                    string uidText = normalizedPath.Substring("uid://".Length);
                    uid = ResourceUid.TextToId(uidText);
                }
                if (uid != ResourceUid.InvalidId)
                {
                    string resolvedPath = ResourceUid.GetIdPath(uid);
                    if (!string.IsNullOrWhiteSpace(resolvedPath))
                    {
                        return ResourceLoader.Load<Texture2D>(resolvedPath);
                    }
                }
            }

            return null;
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

            SubscribeBuildControllerSignals();
            RefreshBuildIcons();
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

        private class OwnedBuildViewData
        {
            public string Name { get; set; } = string.Empty;
            public string IconPath { get; set; } = string.Empty;
            public Texture2D? Icon { get; set; }
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

