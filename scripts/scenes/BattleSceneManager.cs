using Godot;
using System;
using Kuros.Core;
using Kuros.Items;
using Kuros.Managers;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Scenes
{
	/// <summary>
	/// 战斗场景管理器 - 负责管理战斗场景的UI加载和连接
	/// 可以附加到战斗场景的根节点
	/// </summary>
	public partial class BattleSceneManager : Node2D
	{
		[ExportCategory("References")]
		[Export] public GameActor Player { get; private set; } = null!;

	[ExportCategory("UI Settings")]
	[Export] public bool AutoLoadHUD = true;
	[Export] public bool AutoLoadMenu = true;
	[Export] public bool AutoLoadSkillWindow = true;
	[Export] public bool AutoShowLevelName = true;
	[Export] public string LevelName = "关卡 1"; // 关卡名称，如果为空则使用场景名称

	private BattleHUD? _battleHUD;
	private BattleMenu? _battleMenu;
	private SkillWindow? _skillWindow;
	private SaveSlotSelection? _saveSlotSelection;
	private LevelNamePopup? _levelNamePopup;

		public override void _Ready()
		{
			// 延迟查找Player和加载UI，确保场景树完全构建
			CallDeferred(MethodName.InitializeBattleScene);
			
			// 延迟检查并恢复游戏状态，确保UI已加载完成
			CallDeferred(MethodName.EnsureGameResumed);
		}

		/// <summary>
		/// 确保游戏恢复运行（场景加载后，如果PauseManager没有暂停请求，确保游戏未暂停）
		/// </summary>
		private void EnsureGameResumed()
		{
			// 使用 PauseManager 管理暂停状态，这里不需要额外操作
			// 如果 PauseManager 的计数为0，游戏应该已经恢复运行
			if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
			{
				GameLogger.Info(nameof(BattleSceneManager), "检测到PauseManager有暂停请求，保持暂停状态");
			}
			else
			{
				GameLogger.Info(nameof(BattleSceneManager), "游戏已恢复运行");
			}
		}

		private void InitializeBattleScene()
		{
			// 如果没有指定玩家，尝试查找
			if (Player == null)
			{
				// 尝试多种路径查找Player节点
				var foundPlayer = GetNodeOrNull<GameActor>("Player");
				
				if (foundPlayer == null)
				{
					// 尝试从父节点查找
					var parent = GetParent();
					if (parent != null)
					{
						foundPlayer = parent.GetNodeOrNull<GameActor>("Player");
					}
				}
				
				if (foundPlayer == null)
				{
					// 尝试在整个场景树中查找
					var playerInGroup = GetTree().GetFirstNodeInGroup("player");
					if (playerInGroup != null)
					{
						foundPlayer = playerInGroup as GameActor;
					}
				}
				
				if (foundPlayer == null)
				{
					GameLogger.Warn(nameof(BattleSceneManager), "未找到Player节点！UI将正常加载，但不会连接玩家数据。");
					GameLogger.Warn(nameof(BattleSceneManager), "提示：可以在Inspector中手动指定Player节点，或确保场景中有名为'Player'的节点。");
				}
				else
				{
					Player = foundPlayer;
					GameLogger.Info(nameof(BattleSceneManager), $"找到Player节点: {Player.Name}");
				}
			}

			// 应用加载的游戏数据（如果有）
			ApplyLoadedGameData();

			// 加载UI
			LoadUIs();

			// 显示关卡名称弹窗
			if (AutoShowLevelName)
			{
				ShowLevelNamePopup();
			}
		}

		private void LoadUIs()
		{
			// 加载UI
			if (AutoLoadHUD)
			{
				LoadHUD();
			}

			if (AutoLoadMenu)
			{
				LoadMenu();
			}

			if (AutoLoadSkillWindow)
			{
				LoadSkillWindow();
			}
		}

		/// <summary>
		/// 应用加载的游戏数据到玩家
		/// </summary>
		private void ApplyLoadedGameData()
		{
			if (SaveManager.Instance == null)
			{
				GameLogger.Info(nameof(BattleSceneManager), "SaveManager未初始化，跳过应用游戏数据");
				return;
			}

			var gameData = SaveManager.Instance.CurrentGameData;
			if (gameData == null)
			{
				GameLogger.Info(nameof(BattleSceneManager), "没有待应用的游戏数据，使用默认值");
				return;
			}

			if (Player == null)
			{
				GameLogger.Warn(nameof(BattleSceneManager), "玩家节点为空，无法应用游戏数据");
				return;
			}

			// 应用基础属性
			int targetHealth = gameData.CurrentHealth > 0 ? gameData.CurrentHealth : gameData.MaxHealth;
			int targetMaxHealth = gameData.MaxHealth > 0 ? gameData.MaxHealth : Player.MaxHealth;
			Player.RestoreHealth(targetHealth, targetMaxHealth);
			
			GameLogger.Info(nameof(BattleSceneManager), $"应用游戏数据: 血量 {Player.CurrentHealth}/{Player.MaxHealth}, 等级 {gameData.Level}");

			// 如果玩家是 SamplePlayer，应用额外属性
			if (Player is SamplePlayer samplePlayer)
			{
				// 注意：GameSaveData 目前没有 Score 和 Gold 字段，如果需要可以后续添加
				// samplePlayer.AddScore(...);
				// samplePlayer.SetGold(...);
			}

			// 应用数据后清除待应用标记（数据已应用，但保留在 SaveManager 中供其他系统使用）
			// 注意：不清除 CurrentGameData，因为可能还有其他系统需要使用
			GameLogger.Info(nameof(BattleSceneManager), "游戏数据已应用到玩家");
		}

		/// <summary>
		/// 应用加载的游戏数据到当前游戏状态（从读档菜单调用）
		/// </summary>
		/// <param name="gameData">要应用的游戏数据</param>
		/// <returns>是否成功应用</returns>
		private bool ApplyLoadedData(GameSaveData gameData)
		{
			if (gameData == null)
			{
				GameLogger.Error(nameof(BattleSceneManager), "游戏数据为空，无法应用");
				return false;
			}

			if (Player == null)
			{
				GameLogger.Error(nameof(BattleSceneManager), "玩家节点为空，无法应用游戏数据");
				return false;
			}

			try
			{
				// 1. 应用玩家血量
				int targetHealth = gameData.CurrentHealth > 0 ? gameData.CurrentHealth : gameData.MaxHealth;
				int targetMaxHealth = gameData.MaxHealth > 0 ? gameData.MaxHealth : Player.MaxHealth;
				Player.RestoreHealth(targetHealth, targetMaxHealth);
				GameLogger.Info(nameof(BattleSceneManager), $"恢复玩家血量: {Player.CurrentHealth}/{Player.MaxHealth}");

				// 2. 如果玩家是 SamplePlayer，应用额外属性
				if (Player is SamplePlayer samplePlayer)
				{
					// 应用武器（如果 WeaponName 不为空）
					if (!string.IsNullOrEmpty(gameData.WeaponName))
					{
						ApplyWeapon(samplePlayer, gameData.WeaponName);
					}

					// 注意：GameSaveData 目前没有 Score、Gold、Inventory 等字段
					// 如果需要，可以在 GameSaveData 中添加这些字段并在此处应用
					// samplePlayer.AddScore(...);
					// samplePlayer.SetGold(...);
					// ApplyInventory(samplePlayer, gameData);
				}

				// 3. 应用玩家位置（如果有保存位置数据）
				// 注意：当前 GameSaveData 没有位置字段，如果需要可以后续添加
				// if (gameData.PlayerPosition != Vector2.Zero)
				// {
				//     Player.GlobalPosition = gameData.PlayerPosition;
				// }

				// 4. 更新UI
				RefreshUI();

				// 5. 触发物理/状态刷新
				// 确保玩家状态机处于正确状态
				if (Player.StateMachine != null && Player.CurrentHealth > 0)
				{
					// 如果玩家已死亡，可能需要特殊处理
					if (Player.IsDead)
					{
						GameLogger.Warn(nameof(BattleSceneManager), "玩家处于死亡状态，可能需要重新初始化");
					}
					else
					{
						// 确保玩家处于正常状态（Idle 或当前状态）
						// 不强制改变状态，让状态机自然处理
					}
				}

				GameLogger.Info(nameof(BattleSceneManager), $"成功应用游戏数据: 等级 {gameData.Level}, 武器 {gameData.WeaponName}");
				return true;
			}
			catch (Exception ex)
			{
				GameLogger.Error(nameof(BattleSceneManager), $"应用游戏数据时发生错误: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// 应用武器到玩家
		/// </summary>
		private void ApplyWeapon(SamplePlayer player, string weaponName)
		{
			if (player == null || string.IsNullOrEmpty(weaponName))
			{
				return;
			}

			// 尝试通过资源路径加载物品
			// 假设 weaponName 可能是资源路径或 ItemId
			ItemDefinition? weaponItem = null;

			// 首先尝试作为资源路径加载
			if (weaponName.StartsWith("res://"))
			{
				weaponItem = GD.Load<ItemDefinition>(weaponName);
			}
			else
			{
				// 尝试在常见路径中查找
				string[] possiblePaths = {
					$"res://data/{weaponName}.tres",
					$"res://resources/items/{weaponName}.tres",
					$"res://data/DefaultSwordItem.tres" // 默认武器
				};

				foreach (var path in possiblePaths)
				{
					weaponItem = GD.Load<ItemDefinition>(path);
					if (weaponItem != null)
					{
						break;
					}
				}
			}

			if (weaponItem != null)
			{
				// 尝试将武器添加到快捷栏或装备栏
				if (player.InventoryComponent != null)
				{
					// 尝试添加到快捷栏（如果快捷栏存在）
					if (player.InventoryComponent.QuickBar != null)
					{
						// 尝试添加到快捷栏的第一个可用槽位（索引1-4，索引0是默认小木剑）
						for (int i = 1; i < 5; i++)
						{
							if (player.InventoryComponent.QuickBar.TryAddItemToSlot(weaponItem, 1, i) > 0)
							{
								GameLogger.Info(nameof(BattleSceneManager), $"武器 {weaponName} 已添加到快捷栏槽位 {i}");
								// 可选：自动切换到该武器
								// player.SwitchToQuickBarSlot(i);
								return;
							}
						}
					}

					// 如果快捷栏添加失败，尝试添加到背包
					if (player.InventoryComponent.Backpack != null)
					{
						player.InventoryComponent.Backpack.AddItem(weaponItem, 1);
						GameLogger.Info(nameof(BattleSceneManager), $"武器 {weaponName} 已添加到背包");
					}
				}
			}
			else
			{
				GameLogger.Warn(nameof(BattleSceneManager), $"无法加载武器: {weaponName}");
			}
		}

		/// <summary>
		/// 刷新UI以反映当前游戏状态
		/// </summary>
		private void RefreshUI()
		{
			// 刷新 BattleHUD
			if (_battleHUD != null && IsInstanceValid(_battleHUD) && Player != null)
			{
				// 重新连接玩家数据以更新UI
				if (Player is SamplePlayer samplePlayer)
				{
					_battleHUD.AttachActor(Player);
				}
			}

			// 注意：SamplePlayer 的血量变化会通过 HealthChanged 事件自动更新UI
			// 不需要手动调用 UpdateStatsUI（它是私有方法）
		}

		/// <summary>
		/// 加载战斗HUD
		/// </summary>
		public void LoadHUD()
		{
			if (UIManager.Instance == null)
			{
				GameLogger.Error(nameof(BattleSceneManager), "UIManager未初始化！请在project.godot中将UIManager添加为autoload。");
				return;
			}

			_battleHUD = UIManager.Instance.LoadBattleHUD();
			
			if (_battleHUD != null)
			{
				// 连接BattleMenuRequested信号
				if (!_battleHUD.IsConnected(BattleHUD.SignalName.BattleMenuRequested, new Callable(this, MethodName.OnBattleMenuRequested)))
				{
					_battleHUD.BattleMenuRequested += OnBattleMenuRequested;
				}

				// 如果找到了Player，连接它
				if (Player != null && Player is SamplePlayer samplePlayer)
				{
					_battleHUD.AttachActor(Player);
				}
				else
				{
					_battleHUD.SetFallbackStats();
					GameLogger.Info(nameof(BattleSceneManager), "HUD已加载，但未连接玩家数据。");
				}
			}
		}

		/// <summary>
		/// 使用 Godot 原生 Connect 方法连接信号
		/// 这种方式在导出版本中比 C# 委托方式更可靠
		/// </summary>
		private void ConnectSignal(GodotObject source, StringName signalName, string methodName)
		{
			if (source == null) return;
			var callable = new Callable(this, methodName);
			if (!source.IsConnected(signalName, callable))
			{
				source.Connect(signalName, callable);
			}
		}

		/// <summary>
		/// 加载战斗菜单
		/// </summary>
		public void LoadMenu()
		{
			if (UIManager.Instance == null)
			{
				GameLogger.Error(nameof(BattleSceneManager), "UIManager未初始化！");
				return;
			}

			_battleMenu = UIManager.Instance.LoadBattleMenu();
			
			if (_battleMenu != null)
			{
				// 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
				ConnectSignal(_battleMenu, BattleMenu.SignalName.ResumeRequested, nameof(OnMenuResume));
				ConnectSignal(_battleMenu, BattleMenu.SignalName.QuitRequested, nameof(OnMenuQuit));
				ConnectSignal(_battleMenu, BattleMenu.SignalName.SettingsRequested, nameof(OnMenuSettingsRequested));
				ConnectSignal(_battleMenu, BattleMenu.SignalName.SaveRequested, nameof(OnMenuSaveRequested));
				ConnectSignal(_battleMenu, BattleMenu.SignalName.LoadRequested, nameof(OnMenuLoadRequested));
			}
		}

		/// <summary>
		/// 处理BattleMenuRequested信号 - 打开战斗菜单
		/// </summary>
		private void OnBattleMenuRequested()
		{
			if (_battleMenu != null && IsInstanceValid(_battleMenu))
			{
				_battleMenu.OpenMenu();
			}
			else
			{
				// 如果菜单未加载，先加载它
				LoadMenu();
				if (_battleMenu != null)
				{
					_battleMenu.OpenMenu();
				}
			}
		}

		/// <summary>
		/// 加载技能界面
		/// </summary>
		public void LoadSkillWindow()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("BattleSceneManager: UIManager未初始化！");
				return;
			}

			_skillWindow = UIManager.Instance.LoadSkillWindow();
			
			if (_skillWindow != null)
			{
				GD.Print("BattleSceneManager: 技能界面已加载");
			}
		}

		/// <summary>
		/// 显示关卡名称弹窗
		/// </summary>
		public void ShowLevelNamePopup()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("BattleSceneManager: UIManager未初始化！");
				return;
			}

			// 加载关卡名称弹窗
			_levelNamePopup = UIManager.Instance.LoadLevelNamePopup();
			
			if (_levelNamePopup != null)
			{
				// 确定关卡名称
				string levelName = LevelName;
				if (string.IsNullOrEmpty(levelName))
				{
					// 如果未设置关卡名称，使用场景名称
					var scene = GetTree().CurrentScene;
					if (scene != null)
					{
						levelName = scene.Name;
					}
					else
					{
						levelName = "未知关卡";
					}
				}

				// 显示关卡名称
				_levelNamePopup.ShowLevelName(levelName);
				GD.Print($"BattleSceneManager: 显示关卡名称: {levelName}");
			}
		}

		/// <summary>
		/// 卸载所有UI
		/// </summary>
		public void UnloadAllUI()
		{
			if (UIManager.Instance == null) return;

			if (_battleHUD != null && Player != null)
			{
				_battleHUD.DetachActor(Player);
			}

			// 断开信号连接
			if (_battleHUD != null && IsInstanceValid(_battleHUD))
			{
				if (_battleHUD.IsConnected(BattleHUD.SignalName.BattleMenuRequested, new Callable(this, MethodName.OnBattleMenuRequested)))
				{
					_battleHUD.BattleMenuRequested -= OnBattleMenuRequested;
				}
			}

			if (_battleMenu != null && IsInstanceValid(_battleMenu))
			{
				_battleMenu.ResumeRequested -= OnMenuResume;
				_battleMenu.QuitRequested -= OnMenuQuit;
				_battleMenu.SettingsRequested -= OnMenuSettingsRequested;
				_battleMenu.SaveRequested -= OnMenuSaveRequested;
				_battleMenu.LoadRequested -= OnMenuLoadRequested;
			}

			if (_battleSettingsMenu != null && IsInstanceValid(_battleSettingsMenu))
			{
				_battleSettingsMenu.BackRequested -= OnSettingsBackRequested;
			}

			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.SlotSelected -= OnSaveSlotSelected;
				_saveSlotSelection.BackRequested -= OnSaveSlotSelectionBackRequested;
				_saveSlotSelection.ModeSwitchRequested -= OnSaveSlotSelectionModeSwitchRequested;
			}

			UIManager.Instance.UnloadBattleHUD();
			UIManager.Instance.UnloadBattleMenu();
			UIManager.Instance.UnloadSettingsMenu();
			UIManager.Instance.UnloadSkillWindow();
			UIManager.Instance.UnloadSaveSlotSelection();
			UIManager.Instance.UnloadLevelNamePopup();

			_battleHUD = null;
			_battleMenu = null;
			_battleSettingsMenu = null;
			_skillWindow = null;
			_saveSlotSelection = null;
			_levelNamePopup = null;
		}

		private void OnMenuResume()
		{
			// 菜单关闭逻辑已在BattleMenu中处理
			GameLogger.Info(nameof(BattleSceneManager), "继续游戏");
		}

		private void OnMenuQuit()
		{
			// 返回主菜单
			GameLogger.Info(nameof(BattleSceneManager), "返回主菜单");
			var tree = GetTree();
			if (tree != null)
			{
				UnloadAllUI();
				// 清除所有暂停请求，确保场景切换时游戏未暂停
				if (PauseManager.Instance != null)
				{
					PauseManager.Instance.ClearAllPauses();
				}
				tree.ChangeSceneToFile("res://scenes/MainMenu.tscn");
			}
		}

		private SettingsMenu? _battleSettingsMenu;

		private void OnMenuSettingsRequested()
		{
			// 打开设置界面
			GameLogger.Info(nameof(BattleSceneManager), "打开设置菜单");
			if (UIManager.Instance == null) return;

			// 隐藏战斗菜单
			if (_battleMenu != null && IsInstanceValid(_battleMenu))
			{
				_battleMenu.Visible = false;
			}

			// 加载设置菜单
			var settingsMenu = UIManager.Instance.LoadSettingsMenu();
			if (settingsMenu != null)
			{
				settingsMenu.Visible = true;
				// 避免重复连接信号
				if (_battleSettingsMenu != settingsMenu)
				{
					// 断开旧连接
					if (_battleSettingsMenu != null && IsInstanceValid(_battleSettingsMenu))
					{
						_battleSettingsMenu.BackRequested -= OnSettingsBackRequested;
					}
					_battleSettingsMenu = settingsMenu;
					_battleSettingsMenu.BackRequested += OnSettingsBackRequested;
				}
			}
		}

		private void OnSettingsBackRequested()
		{
			// 关闭设置菜单，重新显示战斗菜单
			if (_battleSettingsMenu != null && IsInstanceValid(_battleSettingsMenu))
			{
				_battleSettingsMenu.Visible = false;
			}

			if (_battleMenu != null && IsInstanceValid(_battleMenu))
			{
				_battleMenu.Visible = true;
			}
		}

		/// <summary>
		/// 打开存档/读档选择界面
		/// </summary>
		/// <param name="mode">存档模式（Save或Load）</param>
		private void OpenSaveSlotSelection(SaveLoadMode mode)
		{
			string modeText = mode == SaveLoadMode.Save ? "存档" : "读档";
			GameLogger.Info(nameof(BattleSceneManager), $"打开{modeText}界面");
			
			if (UIManager.Instance == null) return;

			// 隐藏战斗菜单
			if (_battleMenu != null && IsInstanceValid(_battleMenu))
			{
				_battleMenu.Visible = false;
			}

			// 加载存档选择菜单
			if (_saveSlotSelection == null || !IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection = UIManager.Instance.LoadSaveSlotSelection();
				if (_saveSlotSelection != null)
				{
					_saveSlotSelection.SetMode(mode);
					_saveSlotSelection.SetAllowSave(true);
					_saveSlotSelection.SetSource(true); // 从战斗菜单进入
					_saveSlotSelection.SlotSelected += OnSaveSlotSelected;
					_saveSlotSelection.BackRequested += OnSaveSlotSelectionBackRequested;
					_saveSlotSelection.ModeSwitchRequested += OnSaveSlotSelectionModeSwitchRequested;
				}
			}
			else
			{
				_saveSlotSelection.SetMode(mode);
				_saveSlotSelection.SetAllowSave(true);
				_saveSlotSelection.SetSource(true);
				_saveSlotSelection.RefreshSlots();
			}

			if (_saveSlotSelection != null)
			{
				_saveSlotSelection.Visible = true;
			}
		}

		private void OnMenuSaveRequested()
		{
			OpenSaveSlotSelection(SaveLoadMode.Save);
		}

		private void OnMenuLoadRequested()
		{
			OpenSaveSlotSelection(SaveLoadMode.Load);
		}

		private void OnSaveSlotSelectionBackRequested()
		{
			// 关闭存档选择菜单，重新显示战斗菜单
			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.Visible = false;
			}

			if (_battleMenu != null && IsInstanceValid(_battleMenu))
			{
				_battleMenu.Visible = true;
			}
		}

		private void OnSaveSlotSelectionModeSwitchRequested(int newMode)
		{
			// 切换模式
			if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
			{
				_saveSlotSelection.SetMode((SaveLoadMode)newMode);
			}
		}

		private void OnSaveSlotSelected(int slotIndex)
		{
			if (_saveSlotSelection == null) return;

			if (_saveSlotSelection.Mode == SaveLoadMode.Save)
			{
				GD.Print($"保存到存档槽位: {slotIndex}");
				
				// 实现实际的存档逻辑
				if (SaveManager.Instance != null)
				{
					var gameData = SaveManager.Instance.GetCurrentGameData();
					gameData.SlotIndex = slotIndex;
					
					if (SaveManager.Instance.SaveGame(slotIndex, gameData))
					{
						GD.Print($"成功保存到槽位 {slotIndex}");
						// 刷新存档列表
						_saveSlotSelection.RefreshSlots();
					}
					else
					{
						GD.PrintErr($"保存失败: 槽位 {slotIndex}");
					}
				}
				
				// 存档完成后返回战斗菜单
				OnSaveSlotSelectionBackRequested();
			}
			else
			{
				GD.Print($"加载存档槽位: {slotIndex}");
				
				// 实现实际的读档逻辑
				if (SaveManager.Instance != null)
				{
					var gameData = SaveManager.Instance.LoadGame(slotIndex);
					if (gameData != null)
					{
						GD.Print($"成功加载槽位 {slotIndex}");
						
						// 存储游戏数据到 SaveManager，供后续使用
						SaveManager.Instance.SetCurrentGameData(gameData);
						
						// 应用游戏数据到当前游戏状态
						if (ApplyLoadedData(gameData))
						{
							GameLogger.Info(nameof(BattleSceneManager), $"成功应用游戏数据，槽位 {slotIndex}");
							
							// 读档成功后关闭所有菜单并继续游戏
							if (_saveSlotSelection != null && IsInstanceValid(_saveSlotSelection))
							{
								_saveSlotSelection.Visible = false;
							}
							if (_battleMenu != null && IsInstanceValid(_battleMenu))
							{
								_battleMenu.CloseMenu();
							}
						}
						else
						{
							GD.PrintErr($"应用游戏数据失败: 槽位 {slotIndex}");
							// 应用失败，不关闭菜单
							return;
						}
					}
					else
					{
						GD.PrintErr($"加载失败: 槽位 {slotIndex}");
						return; // 加载失败，不关闭菜单
					}
				}
			}
		}

		public override void _ExitTree()
		{
			// 场景退出时清理UI
			UnloadAllUI();
		}
	}
}
