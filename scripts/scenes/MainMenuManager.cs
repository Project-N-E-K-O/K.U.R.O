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
		private LoadingTestManager? _loadingTestManager;

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
			GameLogger.Info(nameof(MainMenuManager), "开始新游戏");
			PerformSceneChange(BattleScenePath);
		}
		
		/// <summary>
		/// 执行场景切换的统一方法
		/// </summary>
		private void PerformSceneChange(string scenePath)
		{
			var tree = GetTree();
			if (tree == null)
			{
				GameLogger.Error(nameof(MainMenuManager), "无法获取场景树！");
				return;
			}
			
			// 清除所有暂停请求，确保场景切换时游戏未暂停
			if (PauseManager.Instance != null)
			{
				PauseManager.Instance.ClearAllPauses();
			}
			
			CleanupUI();
			
			// 检查场景路径是否存在
			if (!ResourceLoader.Exists(scenePath))
			{
				GameLogger.Error(nameof(MainMenuManager), $"场景文件不存在: {scenePath}");
				return;
			}
			
			// 尝试预加载场景以检查是否有问题
			var scene = ResourceLoader.Load<PackedScene>(scenePath);
			if (scene == null)
			{
				GameLogger.Error(nameof(MainMenuManager), $"无法加载场景资源: {scenePath}");
				GameLogger.Error(nameof(MainMenuManager), "请检查场景文件是否损坏或引用的资源是否存在");
				return;
			}
			
			// 尝试实例化场景以验证完整性
			var testInstance = scene.Instantiate();
			if (testInstance == null)
			{
				GameLogger.Error(nameof(MainMenuManager), $"场景实例化失败: {scenePath}");
				GameLogger.Error(nameof(MainMenuManager), "场景文件可能损坏或包含无效节点");
				return;
			}
			testInstance.QueueFree(); // 清理测试实例
			
			// 使用 ChangeSceneToFile 方法（Godot 4.x 推荐）
			var error = tree.ChangeSceneToFile(scenePath);
			if (error != Error.Ok)
			{
				GameLogger.Error(nameof(MainMenuManager), $"切换场景失败: {error}, 路径: {scenePath}");
				GameLogger.Error(nameof(MainMenuManager), $"错误详情: {GetErrorDescription(error)}");
				GameLogger.Error(nameof(MainMenuManager), $"错误代码值: {(int)error}");
			}
			else
			{
				GameLogger.Info(nameof(MainMenuManager), $"成功切换到场景: {scenePath}");
			}
		}
		
		private string GetErrorDescription(Error error)
		{
			return error switch
			{
				Error.Ok => "成功",
				Error.Failed => "操作失败",
				Error.OutOfMemory => "内存不足",
				Error.FileNotFound => "文件未找到",
				Error.FileAlreadyInUse => "文件正在使用",
				Error.FileCantOpen => "无法打开文件",
				Error.FileCantWrite => "无法写入文件",
				Error.FileCantRead => "无法读取文件",
				_ => $"未知错误: {error}"
			};
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
			GameLogger.Info(nameof(MainMenuManager), $"选择了模式: {modeName}");
			PerformSceneChange(BattleScenePath);
		}
		
		private void OnTestLoadingRequested()
		{
			GD.Print("开始测试加载页面");
			
			// 如果已存在加载测试管理器，先停止并清理
			CleanupLoadingTestManager();
			
			// 创建新的加载测试管理器
			_loadingTestManager = new LoadingTestManager();
			_loadingTestManager.Name = "LoadingTestManager";
			
			// 连接 TreeExited 信号，当节点被释放时清除引用
			_loadingTestManager.TreeExited += OnLoadingTestManagerExited;
			
			GetTree().Root.AddChild(_loadingTestManager);
			
			// 开始加载测试
			_loadingTestManager.StartLoadingTest();
		}
		
		/// <summary>
		/// 清理加载测试管理器
		/// </summary>
		private void CleanupLoadingTestManager()
		{
			if (_loadingTestManager != null && IsInstanceValid(_loadingTestManager))
			{
				// 断开信号连接
				_loadingTestManager.TreeExited -= OnLoadingTestManagerExited;
				
				// 从场景树中移除并释放
				if (_loadingTestManager.IsInsideTree())
				{
					_loadingTestManager.GetParent()?.RemoveChild(_loadingTestManager);
				}
				_loadingTestManager.QueueFree();
			}
			_loadingTestManager = null;
		}
		
		/// <summary>
		/// 当加载测试管理器被释放时的回调
		/// </summary>
		private void OnLoadingTestManagerExited()
		{
			_loadingTestManager = null;
		}

		private void OnSaveSlotSelected(int slotIndex)
		{
			if (_saveSlotSelection == null) return;

			// 从主界面进入时，只允许读档
			GameLogger.Info(nameof(MainMenuManager), $"加载存档槽位: {slotIndex}");
			
			// 实现实际的读档逻辑
			if (SaveManager.Instance == null)
			{
				GameLogger.Error(nameof(MainMenuManager), "SaveManager.Instance 为空，无法加载存档");
				return;
			}
			
			var gameData = SaveManager.Instance.LoadGame(slotIndex);
			if (gameData == null)
			{
				GameLogger.Error(nameof(MainMenuManager), $"加载失败: 槽位 {slotIndex}");
				return; // 加载失败，不切换场景
			}
			
			// 将加载的游戏数据存储到 SaveManager，供战斗场景使用
			SaveManager.Instance.SetCurrentGameData(gameData);
			GameLogger.Info(nameof(MainMenuManager), $"成功加载槽位 {slotIndex}，数据已存储，准备切换场景");
			
			// 只有在数据成功存储后才切换场景
			PerformSceneChange(BattleScenePath);
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

			// 取消訂閱 _saveSlotSelection 事件以防止處理器洩漏
			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.SlotSelected -= OnSaveSlotSelected;
				_saveSlotSelection.BackRequested -= LoadMainMenu;
				_saveSlotSelection.ModeSwitchRequested -= OnSaveSlotSelectionModeSwitchRequested;
			}
			
			// 清理加载测试管理器
			CleanupLoadingTestManager();

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
