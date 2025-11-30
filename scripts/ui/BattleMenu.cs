using Godot;
using Kuros.Managers;

namespace Kuros.UI
{
    /// <summary>
    /// 战斗菜单 - 暂停菜单
    /// 通过ESC键打开/关闭
    /// 战斗菜单 - 暂停菜单
    /// 通过ESC键打开/关闭
    /// </summary>
    public partial class BattleMenu : Control
    {
        private const string CompendiumScenePath = "res://scenes/ui/windows/CompendiumWindow.tscn";

        // 信号
        [Signal] public delegate void ResumeRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void SaveRequestedEventHandler();
        [Signal] public delegate void LoadRequestedEventHandler();
        [Signal] public delegate void QuitRequestedEventHandler();
        [Signal] public delegate void ExitGameRequestedEventHandler();

        [ExportCategory("UI References")]
        [Export] public Button ResumeButton { get; private set; } = null!;
        [Export] public Button SettingsButton { get; private set; } = null!;
        [Export] public Button CompendiumButton { get; private set; } = null!;
        [Export] public Button SaveButton { get; private set; } = null!;
        [Export] public Button LoadButton { get; private set; } = null!;
        [Export] public Button QuitButton { get; private set; } = null!;
        [Export] public Button ExitButton { get; private set; } = null!;
        [Export] public Button ExitButton { get; private set; } = null!;

        private bool _isOpen = false;
        private CompendiumWindow? _compendiumWindow;
        private PackedScene? _compendiumScene;

        public bool IsOpen => _isOpen;
        private CompendiumWindow? _compendiumWindow;
        private PackedScene? _compendiumScene;

        public bool IsOpen => _isOpen;

        public override void _Ready()
        {
            // 暂停时也要接收输入
            // 暂停时也要接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点引用
            ResumeButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/ResumeButton");
            SettingsButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/SettingsButton");
            CompendiumButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/CompendiumButton");
            SaveButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/SaveButton");
            LoadButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/LoadButton");
            QuitButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/QuitButton");
            ExitButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/ExitButton");

            // 连接按钮信号
            if (ResumeButton != null)
                ResumeButton.Pressed += OnResumePressed;
            if (SettingsButton != null)
                SettingsButton.Pressed += OnSettingsPressed;
            if (CompendiumButton != null)
                CompendiumButton.Pressed += OnCompendiumPressed;
            if (SaveButton != null)
                SaveButton.Pressed += OnSavePressed;
            if (LoadButton != null)
                LoadButton.Pressed += OnLoadPressed;
            if (QuitButton != null)
                QuitButton.Pressed += OnQuitPressed;
            if (ExitButton != null)
                ExitButton.Pressed += OnExitGamePressed;

            LoadCompendiumWindow();
            if (ExitButton != null)
                ExitButton.Pressed += OnExitGamePressed;

            LoadCompendiumWindow();

            // 延迟确保隐藏（在UIManager设置可见之后）
            CallDeferred(MethodName.EnsureHidden);
            // 延迟确保隐藏（在UIManager设置可见之后）
            CallDeferred(MethodName.EnsureHidden);
        }

        public override void _Input(InputEvent @event)
        {
            // 如果对话正在进行，完全不处理任何输入，让对话窗口处理
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            {
                // 重要：不要调用 SetInputAsHandled()，让输入继续传播到 DialogueWindow
                // 直接返回，不处理任何输入
                return;
            }
            
            // 如果是 ESC 键，先检查物品栏是否打开
            bool isEscKey = false;
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
            {
                isEscKey = true;
            }
            
            if (isEscKey)
            {
                // 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
                var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
                if (itemPopup != null && itemPopup.Visible)
                {
                    // 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
                    GD.Print("BattleMenu._Input: 物品获得弹窗打开，ESC键被禁用，不处理");
                    return; // 不处理，也不调用SetInputAsHandled，让弹窗处理（禁用）
                }
                
                // 检查物品栏是否打开
                bool inventoryOpen = IsInventoryWindowOpen();
                
                if (inventoryOpen)
                {
                    // 物品栏打开时，ESC键会被物品栏处理（关闭物品栏），这里不处理
                    GD.Print("BattleMenu._Input: 物品栏打开，ESC键由物品栏处理，不拦截");
                    return; // 不处理，也不调用SetInputAsHandled，让物品栏处理
                }
                
                // 检查图鉴窗口是否打开
                bool compendiumOpen = IsCompendiumWindowOpen();
                
                if (compendiumOpen)
                {
                    // 图鉴窗口打开时，ESC键会被图鉴窗口处理，这里不处理
                    GD.Print("BattleMenu._Input: 图鉴窗口打开，ESC键由图鉴窗口处理，不拦截");
                    return; // 不处理，也不调用SetInputAsHandled，让图鉴窗口处理
                }
            }
            
            // 处理Return键（Enter）和ui_cancel（ESC）来打开/关闭菜单
            if (@event.IsActionPressed("Return") || isEscKey)
            {
                ToggleMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        private bool IsInventoryWindowOpen()
        {
            // 直接在整个场景树中查找所有 InventoryWindow
            var root = GetTree().Root;
            if (root != null)
            {
                var inventoryWindows = FindAllInventoryWindowsInTree(root);
                
                foreach (var inventoryWindow in inventoryWindows)
                {
                    if (inventoryWindow.Visible)
                    {
                        GD.Print($"BattleMenu.IsInventoryWindowOpen: 找到打开的物品栏，Visible={inventoryWindow.Visible}");
                        return true;
                    }
                }
            }
            
            GD.Print("BattleMenu.IsInventoryWindowOpen: 未找到打开的物品栏");
            return false;
        }

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

        private InventoryWindow? FindInventoryWindowInTree(Node node)
        {
            // 检查当前节点
            if (node is InventoryWindow inventoryWindow)
            {
                return inventoryWindow;
            }
            
            // 递归检查子节点
            foreach (Node child in node.GetChildren())
            {
                var found = FindInventoryWindowInTree(child);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }

        private bool IsCompendiumWindowOpen()
        {
            // 直接在整个场景树中查找所有 CompendiumWindow
            var root = GetTree().Root;
            if (root != null)
            {
                var compendiumWindows = FindAllCompendiumWindowsInTree(root);
                
                foreach (var compendiumWindow in compendiumWindows)
                {
                    if (compendiumWindow.Visible)
                    {
                        GD.Print($"BattleMenu.IsCompendiumWindowOpen: 找到打开的图鉴窗口，Visible={compendiumWindow.Visible}");
                        return true;
                    }
                }
            }
            
            GD.Print("BattleMenu.IsCompendiumWindowOpen: 未找到打开的图鉴窗口");
            return false;
        }

        private System.Collections.Generic.List<CompendiumWindow> FindAllCompendiumWindowsInTree(Node node)
        {
            var result = new System.Collections.Generic.List<CompendiumWindow>();
            
            // 检查当前节点
            if (node is CompendiumWindow compendiumWindow)
            {
                result.Add(compendiumWindow);
            }
            
            // 递归检查子节点
            foreach (Node child in node.GetChildren())
            {
                result.AddRange(FindAllCompendiumWindowsInTree(child));
            }
            
            return result;
        }

        private void LoadCompendiumWindow()
        {
            _compendiumScene ??= GD.Load<PackedScene>(CompendiumScenePath);
            if (_compendiumScene == null)
            {
                GD.PrintErr("无法加载图鉴窗口场景：", CompendiumScenePath);
                return;
            }

            _compendiumWindow = _compendiumScene.Instantiate<CompendiumWindow>();
            AddChild(_compendiumWindow);
            // HideWindow() is called in CompendiumWindow._Ready(), so no need to call it here
        }


        public void OpenMenu()
        {
            if (_isOpen) return;

            // 如果物品栏打开，阻止打开菜单
            if (IsInventoryWindowOpen())
            {
                GD.Print("BattleMenu.OpenMenu: 物品栏打开，无法打开菜单");
                return;
            }

            Visible = true;
            _isOpen = true;
            GetTree().Paused = true;

            // 确保菜单在弹窗之上显示
            EnsureMenuOnTop();
        }

        /// <summary>
        /// 确保菜单在弹窗之上显示
        /// </summary>
        private void EnsureMenuOnTop()
        {
            // 设置较高的ZIndex，确保菜单在弹窗之上
            ZIndex = 1000;

            // 将菜单移到父节点的最后，确保在场景树中也在所有其他UI之后
            // 这样即使ZIndex相同，菜单也会渲染在最上层
            var parent = GetParent();
            if (parent != null)
            {
                var currentIndex = GetIndex();
                var lastIndex = parent.GetChildCount() - 1;
                
                // 如果菜单不在最后，移到最后
                if (currentIndex < lastIndex)
                {
                    parent.MoveChild(this, lastIndex);
                    GD.Print("BattleMenu: 已将菜单移到最上层");
                }
            }
        }

        public void CloseMenu()
        {
            if (!_isOpen) return;

            Visible = false;
            Visible = false;
            _isOpen = false;
            
            // 检查是否有其他UI需要保持暂停（如物品获得弹窗）
            if (!ShouldKeepPaused())
            {
                GetTree().Paused = false;
            }
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
            var inventoryWindow = Kuros.Managers.UIManager.Instance?.GetUI<InventoryWindow>("InventoryWindow");
            if (inventoryWindow != null && inventoryWindow.Visible)
            {
                return true;
            }

            // 检查对话是否激活
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            {
                return true;
            }

            return false;
        }

        public void ToggleMenu()
        {
            // 如果物品栏打开，阻止切换菜单
            if (IsInventoryWindowOpen())
            {
                GD.Print("BattleMenu.ToggleMenu: 物品栏打开，无法切换菜单");
                return;
            }

            if (_isOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        private void EnsureHidden()
        private void EnsureHidden()
        {
            if (!_isOpen)
            if (!_isOpen)
            {
                Visible = false;
                Visible = false;
            }
        }

        private void OnResumePressed()
        {
            EmitSignal(SignalName.ResumeRequested);
            CloseMenu();
        }

        private void OnSettingsPressed()
        {
            EmitSignal(SignalName.SettingsRequested);
        }

        private void OnQuitPressed()
        {
            // 先关闭菜单并取消暂停
            CloseMenu();
            // 先关闭菜单并取消暂停
            CloseMenu();
            EmitSignal(SignalName.QuitRequested);
        }

        private void OnExitGamePressed()
        {
            EmitSignal(SignalName.ExitGameRequested);
            GetTree().Quit();
        }

        private void OnCompendiumPressed()
        {
            if (_compendiumWindow == null)
            {
                GD.PrintErr("图鉴窗口未创建");
                return;
            }

            if (_compendiumWindow.Visible)
            {
                _compendiumWindow.HideWindow();
            }
            else
            {
                _compendiumWindow.ShowWindow();
            }
        }

        private void OnSavePressed()
        {
            EmitSignal(SignalName.SaveRequested);
            GD.Print("打开存档界面");
        }

        private void OnLoadPressed()
        {
            EmitSignal(SignalName.LoadRequested);
            GD.Print("打开读档界面");
        }
    }
}
