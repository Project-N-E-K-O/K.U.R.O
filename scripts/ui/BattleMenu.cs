using System;
using Godot;
using Kuros.Managers;
using Kuros.Utils;

namespace Kuros.UI
{
    /// <summary>
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

        private bool _isOpen = false;
        private CompendiumWindow? _compendiumWindow;
        private PackedScene? _compendiumScene;

        // 缓存的窗口引用，避免每次ESC都遍历场景树
        private InventoryWindow? _cachedInventoryWindow;
        private CompendiumWindow? _cachedCompendiumWindow;
        private SkillDetailWindow? _cachedSkillDetailWindow;

        public bool IsOpen => _isOpen;

        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接按钮信号
        /// 这种方式在导出版本中比 C# 委托方式更可靠
        /// </summary>
        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            
            // 使用 Godot 的 Connect 方法，这在导出版本中更可靠
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        public override void _Ready()
        {
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

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(ResumeButton, nameof(OnResumePressed));
            ConnectButtonSignal(SettingsButton, nameof(OnSettingsPressed));
            ConnectButtonSignal(CompendiumButton, nameof(OnCompendiumPressed));
            ConnectButtonSignal(SaveButton, nameof(OnSavePressed));
            ConnectButtonSignal(LoadButton, nameof(OnLoadPressed));
            ConnectButtonSignal(QuitButton, nameof(OnQuitPressed));
            ConnectButtonSignal(ExitButton, nameof(OnExitGamePressed));

            LoadCompendiumWindow();

            // 缓存窗口引用，避免每次ESC都遍历场景树
            CacheWindowReferences();

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
                
                // 检查技能详情窗口是否打开
                bool skillDetailOpen = IsSkillDetailWindowOpen();
                
                if (skillDetailOpen)
                {
                    // 技能详情窗口打开时，ESC键会被技能详情窗口处理，这里不处理
                    GD.Print("BattleMenu._Input: 技能详情窗口打开，ESC键由技能详情窗口处理，不拦截");
                    return; // 不处理，也不调用SetInputAsHandled，让技能详情窗口处理
                }
            }
            
            // 处理Return键（Enter）和ui_cancel（ESC）来打开/关闭菜单
            if (@event.IsActionPressed("Return") || isEscKey)
            {
                ToggleMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>
        /// 缓存窗口引用，避免每次ESC都遍历场景树
        /// </summary>
        private void CacheWindowReferences()
        {
            var root = GetTree().Root;
            if (root == null) return;

            // 尝试从 UIManager 获取 InventoryWindow
            _cachedInventoryWindow = UIManager.Instance?.GetUI<InventoryWindow>("InventoryWindow");
            if (_cachedInventoryWindow == null)
            {
                // 如果 UIManager 中没有，则从场景树中查找
                var inventoryWindows = FindAllNodesOfType<InventoryWindow>(root);
                _cachedInventoryWindow = inventoryWindows.Count > 0 ? inventoryWindows[0] : null;
            }

            // CompendiumWindow 已经在 _compendiumWindow 中缓存了
            _cachedCompendiumWindow = _compendiumWindow;

            // 查找 SkillDetailWindow
            var skillDetailWindows = FindAllNodesOfType<SkillDetailWindow>(root);
            _cachedSkillDetailWindow = skillDetailWindows.Count > 0 ? skillDetailWindows[0] : null;
        }

        /// <summary>
        /// 通用的泛型方法：在场景树中查找所有指定类型的节点
        /// </summary>
        /// <typeparam name="T">要查找的节点类型</typeparam>
        /// <param name="root">根节点</param>
        /// <param name="filter">可选的过滤条件</param>
        /// <returns>找到的所有节点列表</returns>
        private System.Collections.Generic.List<T> FindAllNodesOfType<T>(Node root, System.Func<T, bool>? filter = null) where T : class
        {
            var result = new System.Collections.Generic.List<T>();
            
            if (root == null) return result;

            // Check current node safely
            try
            {
                if (root is T node)
                {
                    if (filter == null || filter(node))
                    {
                        result.Add(node);
                    }
                }
            }
            catch (Exception ex) 
            { 
                GD.Print($"FindAllNodesOfType: Type check error for {root.Name}: {ex.Message}");
            }
            
            // Recursively check children safely
            // Use index loop instead of foreach to catch exceptions per child fetch
            int childCount = root.GetChildCount();
            for (int i = 0; i < childCount; i++)
            {
                Node? child = null;
                try
                {
                    child = root.GetChild(i);
                }
                catch
                {
                    // Failed to fetch child wrapper (e.g. SpineSprite without C# binding), skip it
                    continue;
                }

                if (child != null)
                {
                    try
                    {
                        result.AddRange(FindAllNodesOfType<T>(child, filter));
                    }
                    catch { /* Ignore recursive errors */ }
                }
            }
            
            return result;
        }

        /// <summary>
        /// 检查物品栏窗口是否打开（使用缓存）
        /// </summary>
        private bool IsInventoryWindowOpen()
        {
            // 如果缓存无效，尝试重新查找
            if (_cachedInventoryWindow == null || !IsInstanceValid(_cachedInventoryWindow))
            {
                var root = GetTree().Root;
                if (root != null)
                {
                    var inventoryWindows = FindAllNodesOfType<InventoryWindow>(root);
                    _cachedInventoryWindow = inventoryWindows.Count > 0 ? inventoryWindows[0] : null;
                }
            }

            if (_cachedInventoryWindow != null && _cachedInventoryWindow.Visible)
            {
                GD.Print($"BattleMenu.IsInventoryWindowOpen: 找到打开的物品栏，Visible={_cachedInventoryWindow.Visible}");
                return true;
            }
            
            GD.Print("BattleMenu.IsInventoryWindowOpen: 未找到打开的物品栏");
            return false;
        }

        /// <summary>
        /// 检查图鉴窗口是否打开（使用缓存）
        /// </summary>
        private bool IsCompendiumWindowOpen()
        {
            // CompendiumWindow 已经在 _compendiumWindow 中缓存了
            if (_cachedCompendiumWindow != null && IsInstanceValid(_cachedCompendiumWindow) && _cachedCompendiumWindow.Visible)
            {
                GD.Print($"BattleMenu.IsCompendiumWindowOpen: 找到打开的图鉴窗口，Visible={_cachedCompendiumWindow.Visible}");
                return true;
            }
            
            GD.Print("BattleMenu.IsCompendiumWindowOpen: 未找到打开的图鉴窗口");
            return false;
        }

        /// <summary>
        /// 检查技能详情窗口是否打开（使用缓存）
        /// </summary>
        private bool IsSkillDetailWindowOpen()
        {
            // 如果缓存无效，尝试重新查找
            if (_cachedSkillDetailWindow == null || !IsInstanceValid(_cachedSkillDetailWindow))
            {
                var root = GetTree().Root;
                if (root != null)
                {
                    var skillDetailWindows = FindAllNodesOfType<SkillDetailWindow>(root);
                    _cachedSkillDetailWindow = skillDetailWindows.Count > 0 ? skillDetailWindows[0] : null;
                }
            }

            if (_cachedSkillDetailWindow != null && _cachedSkillDetailWindow.Visible && _cachedSkillDetailWindow.IsOpen)
            {
                GD.Print($"BattleMenu.IsSkillDetailWindowOpen: 找到打开的技能详情窗口，Visible={_cachedSkillDetailWindow.Visible}, IsOpen={_cachedSkillDetailWindow.IsOpen}");
                return true;
            }
            
            GD.Print("BattleMenu.IsSkillDetailWindowOpen: 未找到打开的技能详情窗口");
            return false;
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
            
            // 请求暂停游戏
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PushPause();
            }

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
            _isOpen = false;
            
            // 取消暂停请求
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PopPause();
            }
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
        {
            if (!_isOpen)
            {
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
            GameLogger.Info(nameof(BattleMenu), "打开存档界面");
        }

        private void OnLoadPressed()
        {
            EmitSignal(SignalName.LoadRequested);
            GameLogger.Info(nameof(BattleMenu), "打开读档界面");
        }
    }
}
