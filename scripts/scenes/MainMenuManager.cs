using Godot;
using Kuros.Managers;
using Kuros.UI;
using Kuros.Utils;

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
				GameLogger.Error(nameof(MainMenuManager), "UIManager未初始化！");
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
		/// 加载存档选择菜单
		/// </summary>
		public void LoadSaveSlotSelection()
		{
			if (UIManager.Instance == null) return;

			HideAllMenus();

			// 如果已经加载，直接显示并刷新
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
			GameLogger.Info(nameof(MainMenuManager), "开始新游戏");
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
			LoadSaveSlotSelection();
		}

		private void OnSettingsRequested()
		{
			LoadSettingsMenu();
		}

		private void OnModeSelected(string modeName)
		{
			GameLogger.Info(nameof(MainMenuManager), $"选择了模式: {modeName}");
			var tree = GetTree();
			CleanupUI();
			// 根据模式加载不同的场景
			tree.ChangeSceneToFile(BattleScenePath);
		}

		private void OnSaveSlotSelected(int slotIndex)
		{
			GameLogger.Info(nameof(MainMenuManager), $"加载存档槽位: {slotIndex}");
			var tree = GetTree();
			CleanupUI();
			// 这里应该加载存档数据
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
