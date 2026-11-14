using Godot;
using Kuros.Managers;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Scenes
{
    /// <summary>
    /// 主菜单场景管理器。
    ///
    /// 流程：启动 → TitleScreen → MainMenu（3按钮）→ SaveSlotSelection → Stage_home
    ///
    /// 三个按钮：
    ///   开始游戏 → LoadSaveSlotSelection() → 选槽位 → 读档/新游戏 → 场景切换
    ///   设置     → LoadSettingsMenu()
    ///   退出游戏 → GetTree().Quit()
    ///
    /// 按钮在 .tscn 中预置，_Ready 时通过 GetNodeOrNull 查找并连接信号。
    /// 子菜单（SaveSlotSelection / SettingsMenu）通过 UIManager 加载到独立 CanvasLayer。
    /// 进入子菜单前 HideMenu() 隐藏主菜单面板，返回时 ShowMenu() 恢复。
    /// </summary>
    public partial class MainMenuManager : Control
    {
        [ExportCategory("Scene Paths")]
        [Export] public string BattleScenePath = "res://scenes/Stage_home.tscn";

        [ExportCategory("Main Menu Buttons")]
        [Export] public Button StartGameButton { get; private set; } = null!;
        [Export] public Button SettingsButton { get; private set; } = null!;
        [Export] public Button QuitButton { get; private set; } = null!;

        private Control? _menuPanel;
        private SettingsMenu? _settingsMenu;
        private SaveSlotSelection? _saveSlotSelection;
        private LoadingScreen? _loadingScreen;
        private Callable? _onLoadingCompleteCallable;
        private string _pendingScenePath = "";
        private bool _isLoadingScene;
        private PackedScene? _loadedBattleScene;

        public override void _Ready()
        {
            CleanupUI();
            DialogicUtils.CleanupPersistentState(this);
            CallDeferred(MethodName.InitializeMenus);
        }

        private void InitializeMenus()
        {
            if (UIManager.Instance == null)
            {
                GameLogger.Error(nameof(MainMenuManager), "UIManager未初始化！");
                return;
            }

            UIManager.Instance.ClearAllUI();
            _settingsMenu = null;
            _saveSlotSelection = null;

            ResolveMenuExports();
            ShowMenu();
        }

        private void ResolveMenuExports()
        {
            StartGameButton ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/StartGameButton");
            SettingsButton ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SettingsButton");
            QuitButton ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/QuitButton");
            _menuPanel ??= GetNodeOrNull<Control>("MenuPanel");

            ConnectButton(StartGameButton, nameof(OnStartGamePressed));
            ConnectButton(SettingsButton, nameof(OnSettingsPressed));
            ConnectButton(QuitButton, nameof(OnQuitPressed));
        }

        private void ConnectButton(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
                button.Connect(Button.SignalName.Pressed, callable);
        }

        private void ShowMenu()
        {
            HideAllSubMenus();
            if (_menuPanel != null)
                _menuPanel.Visible = true;
        }

        private void HideMenu()
        {
            if (_menuPanel != null)
                _menuPanel.Visible = false;
        }

        private void HideAllSubMenus()
        {
            if (_settingsMenu != null && IsInstanceValid(_settingsMenu))
                _settingsMenu.Visible = false;
            if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
                _saveSlotSelection.Visible = false;
        }

        // ── 按钮回调 ──

        private void OnStartGamePressed()
        {
            LoadSaveSlotSelection();
        }

        private void OnSettingsPressed()
        {
            LoadSettingsMenu();
        }

        private void OnQuitPressed()
        {
            CleanupUI();
            GetTree()?.Quit();
        }

        // ── 返回主菜单 ──

        public void LoadMainMenu()
        {
            ShowMenu();
        }

        // ── 设置菜单 ──

        public void LoadSettingsMenu()
        {
            if (UIManager.Instance == null) return;

            HideMenu();

            if (_settingsMenu != null && IsInstanceValid(_settingsMenu))
            {
                _settingsMenu.Visible = true;
                return;
            }

            _settingsMenu = UIManager.Instance.LoadSettingsMenu();
            if (_settingsMenu != null)
            {
                _settingsMenu.Visible = true;
                _settingsMenu.BackRequested += LoadMainMenu;
            }
        }

        // ── 存档选择 ──

        public void LoadSaveSlotSelection()
        {
            if (UIManager.Instance == null) return;

            HideMenu();

            if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
            {
                _saveSlotSelection.Visible = true;
                _saveSlotSelection.RefreshSlots();
                return;
            }

            _saveSlotSelection = UIManager.Instance.LoadSaveSlotSelection();
            if (_saveSlotSelection != null)
            {
                _saveSlotSelection.Visible = true;
                _saveSlotSelection.SlotSelected += OnSaveSlotSelected;
                _saveSlotSelection.BackRequested += LoadMainMenu;
            }
        }

        private void OnSaveSlotSelected(int slotIndex)
        {
            GD.Print($"MainMenuManager.OnSaveSlotSelected 被调用，槽位: {slotIndex}");
            if (_saveSlotSelection == null || SaveManager.Instance == null)
            {
                GD.PrintErr($"MainMenuManager.OnSaveSlotSelected: _saveSlotSelection 或 SaveManager 为 null");
                return;
            }

            if (SaveManager.Instance.HasSave(slotIndex))
            {
                GD.Print($"MainMenuManager: 槽位 {slotIndex} 有存档，加载中...");
                var gameData = SaveManager.Instance.LoadGame(slotIndex);
                if (gameData == null)
                {
                    GD.PrintErr($"MainMenuManager: LoadGame 返回 null");
                    return;
                }
                SaveManager.Instance.SetCurrentGameData(gameData);
                GameLogger.Info(nameof(MainMenuManager), $"读档槽位 {slotIndex}，进入场景");
            }
            else
            {
                GD.Print($"MainMenuManager: 槽位 {slotIndex} 无存档，创建新游戏...");
                SaveManager.Instance.NewGame(slotIndex);
                GameLogger.Info(nameof(MainMenuManager), $"新游戏槽位 {slotIndex}，进入场景");
            }

            GD.Print($"MainMenuManager: 即将切换到 {BattleScenePath}");
            PerformSceneChange(BattleScenePath);
        }

        // ── 场景切换 ──

        private void PerformSceneChange(string scenePath)
        {
            var tree = GetTree();
            if (tree == null)
            {
                GameLogger.Error(nameof(MainMenuManager), "无法获取场景树！");
                return;
            }

            if (PauseManager.Instance != null)
                PauseManager.Instance.ClearAllPauses();

            CleanupUI();

            if (!ResourceLoader.Exists(scenePath))
            {
                GameLogger.Error(nameof(MainMenuManager), $"场景文件不存在: {scenePath}");
                return;
            }

            var scene = ResourceLoader.Load<PackedScene>(scenePath);
            if (scene == null)
            {
                GameLogger.Error(nameof(MainMenuManager), $"无法加载场景资源: {scenePath}");
                return;
            }

            var testInstance = scene.Instantiate();
            if (testInstance == null)
            {
                GameLogger.Error(nameof(MainMenuManager), $"场景实例化失败: {scenePath}");
                return;
            }
            testInstance.QueueFree();

            var error = tree.ChangeSceneToFile(scenePath);
            if (error != Error.Ok)
                GameLogger.Error(nameof(MainMenuManager), $"切换场景失败: {error}, 路径: {scenePath}");
            else
                GameLogger.Info(nameof(MainMenuManager), $"成功切换到场景: {scenePath}");
        }

        // ── 清理 ──

        private void CleanupUI()
        {
            if (UIManager.Instance == null) return;

            if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
            {
                _saveSlotSelection.SlotSelected -= OnSaveSlotSelected;
                _saveSlotSelection.BackRequested -= LoadMainMenu;
            }

            if (_loadingScreen != null && IsInstanceValid(_loadingScreen))
            {
                var c = _onLoadingCompleteCallable;
                if (c != null)
                {
                    Callable cb = c.Value;
                    if (_loadingScreen.IsConnected(LoadingScreen.SignalName.LoadingComplete, cb))
                        _loadingScreen.Disconnect(LoadingScreen.SignalName.LoadingComplete, cb);
                }
                _loadingScreen.HideLoading();
                _loadingScreen = null;
            }
            _onLoadingCompleteCallable = null;
            _pendingScenePath = "";

            UIManager.Instance.ClearAllUI();
            _settingsMenu = null;
            _saveSlotSelection = null;
        }

        public override void _ExitTree()
        {
            CleanupUI();
        }
    }
}
