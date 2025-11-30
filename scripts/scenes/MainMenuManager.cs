using Godot;
using Kuros.Managers;
using Kuros.UI;

namespace Kuros.Scenes
{
	/// <summary>
	/// 主菜单场景管理器 - 管理主菜单及其子菜单的显示和切换
	/// </summary>
	public partial class MainMenuManager : Control
	{
		[ExportCategory("Scene Paths")]
		[Export] public string BattleScenePath = "res://scenes/ExampleBattle.tscn";

		private MainMenu? _mainMenu;
		private ModeSelectionMenu? _modeSelectionMenu;
		private SettingsMenu? _settingsMenu;
		private SaveSlotSelection? _saveSlotSelection;

		public override void _Ready()
		{
			// 清理可能残留的UI
			CleanupUI();
			
			// 延迟加载，确保UIManager已初始化
			CallDeferred(MethodName.InitializeMenus);
		}

		private void InitializeMenus()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("MainMenuManager: UIManager未初始化！");
				return;
			}

			// 确保清理所有UI
			UIManager.Instance.ClearAllUI();
			_mainMenu = null;
			_modeSelectionMenu = null;
			_settingsMenu = null;
			_saveSlotSelection = null;

			// 加载主菜单
			LoadMainMenu();
		}

		/// <summary>
		/// 加载主菜单
		/// </summary>
		public void LoadMainMenu()
		{
			if (UIManager.Instance == null) return;

			// 隐藏其他菜单
			HideAllMenus();

			// 如果已经加载，直接显示
			if (_mainMenu != null && IsInstanceValid(_mainMenu))
			{
				_mainMenu.Visible = true;
				return;
			}

			_mainMenu = UIManager.Instance.LoadMainMenu();
			if (_mainMenu != null)
			{
				_mainMenu.Visible = true;
				_mainMenu.StartGameRequested += OnStartGame;
				_mainMenu.ModeSelectionRequested += OnModeSelectionRequested;
				_mainMenu.LoadGameRequested += OnLoadGameRequested;
				_mainMenu.SettingsRequested += OnSettingsRequested;
				_mainMenu.QuitRequested += OnQuit;
			}
		}

		/// <summary>
		/// 加载模式选择菜单
		/// </summary>
		public void LoadModeSelectionMenu()
		{
			if (UIManager.Instance == null) return;

			HideAllMenus();

			// 如果已经加载，直接显示
			if (_modeSelectionMenu != null && IsInstanceValid(_modeSelectionMenu))
			{
				_modeSelectionMenu.Visible = true;
				return;
			}

			_modeSelectionMenu = UIManager.Instance.LoadModeSelectionMenu();
			if (_modeSelectionMenu != null)
			{
				_modeSelectionMenu.Visible = true;
				_modeSelectionMenu.ModeSelected += OnModeSelected;
				_modeSelectionMenu.BackRequested += LoadMainMenu;
				_modeSelectionMenu.TestLoadingRequested += OnTestLoadingRequested;
			}
		}

		/// <summary>
		/// 加载设置菜单
		/// </summary>
		public void LoadSettingsMenu()
		{
			if (UIManager.Instance == null) return;

			HideAllMenus();

			// 如果已经加载，直接显示
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

		/// <summary>
		/// 加载存档选择菜单（从主界面进入，只允许读档）
		/// </summary>
		public void LoadSaveSlotSelection(SaveLoadMode mode, bool allowSave = false)
		{
			if (UIManager.Instance == null) return;

			HideAllMenus();

			// 如果已经加载，直接显示并刷新
			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.Visible = true;
				_saveSlotSelection.SetMode(mode);
				_saveSlotSelection.SetAllowSave(allowSave);
				_saveSlotSelection.SetSource(false); // 从主菜单进入
				_saveSlotSelection.RefreshSlots();
				return;
			}

			_saveSlotSelection = UIManager.Instance.LoadSaveSlotSelection();
			if (_saveSlotSelection != null)
			{
				_saveSlotSelection.Visible = true;
				_saveSlotSelection.SetMode(mode);
				_saveSlotSelection.SetAllowSave(allowSave);
				_saveSlotSelection.SetSource(false); // 从主菜单进入
				_saveSlotSelection.SlotSelected += OnSaveSlotSelected;
				_saveSlotSelection.BackRequested += LoadMainMenu;
				_saveSlotSelection.ModeSwitchRequested += OnSaveSlotSelectionModeSwitchRequested;
			}
		}

		private void OnSaveSlotSelectionModeSwitchRequested(int newMode)
		{
			var mode = (SaveLoadMode)newMode;
			// 从主界面进入时，不允许切换到存档模式
			if (mode == SaveLoadMode.Save && _saveSlotSelection != null)
			{
				// 如果当前不允许存档，不允许切换
				if (!_saveSlotSelection.AllowSave)
				{
					GD.Print("从主界面进入，不允许存档");
					return;
				}
			}
			
			// 切换模式
			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.SetMode(mode);
			}
		}

		private void HideAllMenus()
		{
			if (UIManager.Instance == null) return;

			if (_mainMenu != null && IsInstanceValid(_mainMenu))
			{
				_mainMenu.Visible = false;
			}
			if (_modeSelectionMenu != null && IsInstanceValid(_modeSelectionMenu))
			{
				_modeSelectionMenu.Visible = false;
			}
			if (_settingsMenu != null && IsInstanceValid(_settingsMenu))
			{
				_settingsMenu.Visible = false;
			}
			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.Visible = false;
			}
		}

		private void OnStartGame()
		{
			GD.Print("开始新游戏");
			var tree = GetTree();
			CleanupUI();
			tree.ChangeSceneToFile(BattleScenePath);
		}

		private void OnModeSelectionRequested()
		{
			LoadModeSelectionMenu();
		}

		private void OnLoadGameRequested()
		{
			LoadSaveSlotSelection(SaveLoadMode.Load, false); // 从主界面进入，禁用存档
		}

		private void OnSettingsRequested()
		{
			LoadSettingsMenu();
		}

		private void OnModeSelected(string modeName)
		{
			GD.Print($"选择了模式: {modeName}");
			var tree = GetTree();
			CleanupUI();
			// 根据模式加载不同的场景
			tree.ChangeSceneToFile(BattleScenePath);
		}
		
		private void OnTestLoadingRequested()
		{
			GD.Print("开始测试加载页面");
			
			// 创建加载测试管理器
			var loadingTestManager = new LoadingTestManager();
			loadingTestManager.Name = "LoadingTestManager";
			GetTree().Root.AddChild(loadingTestManager);
			
			// 开始加载测试
			loadingTestManager.StartLoadingTest();
		}

		private void OnSaveSlotSelected(int slotIndex)
		{
			if (_saveSlotSelection == null) return;

			// 从主界面进入时，只允许读档
			GD.Print($"加载存档槽位: {slotIndex}");
			
			// 实现实际的读档逻辑
			if (SaveManager.Instance != null)
			{
				var gameData = SaveManager.Instance.LoadGame(slotIndex);
				if (gameData != null)
				{
					GD.Print($"成功加载槽位 {slotIndex}");
					// TODO: 应用游戏数据到游戏状态
					// 例如：恢复玩家血量、等级、武器等
				}
				else
				{
					GD.PrintErr($"加载失败: 槽位 {slotIndex}");
					return; // 加载失败，不切换场景
				}
			}
			
			var tree = GetTree();
			CleanupUI();
			tree.ChangeSceneToFile(BattleScenePath);
		}

		private void OnQuit()
		{
			var tree = GetTree();
			CleanupUI();
			tree.Quit();
		}

		private void CleanupUI()
		{
			if (UIManager.Instance == null) return;

			UIManager.Instance.ClearAllUI();
			_mainMenu = null;
			_modeSelectionMenu = null;
			_settingsMenu = null;
			_saveSlotSelection = null;
		}

		public override void _ExitTree()
		{
			CleanupUI();
		}
	}
}
