using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;

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
        private float _cooldownRefreshTimer = 0.1f;

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

            if (!_isOpen || !Visible)
            {
                return;
            }

            if (_ownedBuilds.Exists(b => b.CooldownDuration > 0f))
            {
                _cooldownRefreshTimer -= (float)delta;
                if (_cooldownRefreshTimer <= 0f)
                {
                    _cooldownRefreshTimer = 0.1f;
                    RefreshBuildIcons();
                }
            }
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
        /// 初始化构筑图标数据：根据当前活跃的所有构筑类型，显示已拥有的 build 图标。
        /// 支持多个构筑类型同时显示。
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

            // 遍历所有条目，如果属于活跃的任何构筑类型且点数充足，则添加
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

                // 检查该构筑的点数是否满足此条目的需求
                if (!buildCountByClass.ContainsKey(buildClass) || buildCountByClass[buildClass] < entry.RequiredPoints)
                {
                    continue;
                }

                var viewData = new OwnedBuildViewData
                {
                    Name = string.IsNullOrWhiteSpace(entry.BuildName) ? $"{buildClass} Lv.{entry.Level}" : entry.BuildName,
                    IconPath = entry.IconPath ?? string.Empty,
                    Icon = LoadBuildIcon(entry.IconPath ?? string.Empty),
                    EffectId = entry.EffectId ?? string.Empty,
                };

                if (!string.IsNullOrWhiteSpace(viewData.EffectId) && buildController.TargetEffectController != null)
                {
                    var effect = buildController.TargetEffectController.GetEffect(viewData.EffectId);
                    if (effect is ICooldownEffect cooldownEffect)
                    {
                        viewData.HasCooldown = cooldownEffect.IsOnCooldown;
                        viewData.CooldownRemaining = cooldownEffect.CooldownRemaining;
                        viewData.CooldownDuration = cooldownEffect.CooldownDuration;
                    }
                }

                _ownedBuilds.Add(viewData);
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

                if (_ownedBuilds[i].HasCooldown && _ownedBuilds[i].CooldownDuration > 0f)
                {
                    var overlay = new CooldownRing();
                    overlay.Progress = Mathf.Clamp(
                        _ownedBuilds[i].CooldownRemaining / _ownedBuilds[i].CooldownDuration,
                        0f,
                        1f);
                    overlay.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    overlay.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    overlay.CustomMinimumSize = new Vector2(96, 96);
                    iconRect.AddChild(overlay);
                }
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
            public string EffectId { get; set; } = string.Empty;
            public bool HasCooldown { get; set; }
            public float CooldownRemaining { get; set; }
            public float CooldownDuration { get; set; }
        }

        private partial class CooldownRing : Control
        {
            private float _progress;
            public float Progress
            {
                get => _progress;
                set
                {
                    _progress = Mathf.Clamp(value, 0f, 1f);
                    QueueRedraw();
                }
            }

            public override void _Ready()
            {
                base._Ready();
                MouseFilter = MouseFilterEnum.Ignore;
            }

            public override void _Draw()
            {
                base._Draw();
                Vector2 rectSize = Size;
                Vector2 center = rectSize * 0.5f;
                Vector2 halfSize = rectSize * 0.5f;

                var overlayColor = new Color(0f, 0f, 0f, 0.45f);

                if (Progress > 0f)
                {
                    int steps = 48;
                    float startAngle = -Mathf.Pi / 2f;
                    float endAngle = startAngle + Mathf.Pi * 2f * Progress;
                    var points = new Vector2[steps + 2];
                    points[0] = center;

                    for (int i = 0; i <= steps; i++)
                    {
                        float t = (float)i / steps;
                        float angle = Mathf.Lerp(startAngle, endAngle, t);
                        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        points[i + 1] = center + GetRectEdgePoint(direction, halfSize);
                    }

                    DrawPolygon(points, new Color[] { overlayColor });
                }
            }

            private static Vector2 GetRectEdgePoint(Vector2 direction, Vector2 halfSize)
            {
                if (direction == Vector2.Zero)
                {
                    return Vector2.Zero;
                }

                float tx = direction.X != 0f ? halfSize.X / Mathf.Abs(direction.X) : float.MaxValue;
                float ty = direction.Y != 0f ? halfSize.Y / Mathf.Abs(direction.Y) : float.MaxValue;
                float t = Mathf.Min(tx, ty);
                return direction * t;
            }

            public override void _Notification(int what)
            {
                base._Notification(what);
                if (what == NotificationResized)
                {
                    QueueRedraw();
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

