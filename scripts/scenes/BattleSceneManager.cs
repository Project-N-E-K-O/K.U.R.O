using Godot;
using System;
using Kuros.Actors.Heroes;
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
		/// <summary>AI 可读关卡描述（供 GameStateProvider 快照喂给 LLM——本关地形/敌人构成等情境说明）。
		/// 各战斗场景的 BattleSceneManager 节点 Inspector 配置。</summary>
		[Export(PropertyHint.MultilineText)] public string AiLevelDescription { get; set; } = string.Empty;

		private BattleHUD? _battleHUD;
		private BattleMenu? _battleMenu;
		private SkillWindow? _skillWindow;
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

			// 应用加载的游戏数据（HP 等基础属性）
			ApplyLoadedGameData();

			// 加载UI（LoadHUD 内部会调用 SetQuickBar，QuickBar 此后才可用）
			LoadUIs();

			// 背包还原推迟到下一帧执行，确保 HUD._Ready 已完成，不会错过 NotifyHealthChanged 信号
			CallDeferred(MethodName.RestoreInventoryTransit);
			CallDeferred(MethodName.RestoreBuildState);

			// 显示关卡名称弹窗
			if (AutoShowLevelName && GetNodeOrNull<Kuros.Systems.Stage.StageSession>("StageSession") == null)
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
		/// 还原场景切换时的背包过渡快照（必须在 LoadUIs 之后调用，QuickBar 此时才已设置）
		/// </summary>
		private void RestoreInventoryTransit()
		{
			if (SaveManager.Instance?.PendingInventoryTransit == null) return;
			if (Player is not SamplePlayer sp) return;

			var inv = sp.InventoryComponent ?? sp.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
			if (inv == null) return;

			var transit = SaveManager.Instance.PendingInventoryTransit;
			// 传入玩家引用：恢复家具槽时应用 OnEquip 效果（与运行时拾取一致）
			transit.RestoreTo(inv, sp);

			// 如果过渡数据中保存了 HP，也一并恢复
			if (transit.CurrentHealth > 0)
				sp.RestoreHealth(transit.CurrentHealth, transit.MaxHealth > 0 ? transit.MaxHealth : sp.MaxHealth);

			SaveManager.Instance.PendingInventoryTransit = null;
			GameLogger.Info(nameof(BattleSceneManager), "已从过渡快照还原背包与血量。");
		}
		private void RestoreBuildState()
		{
			if (Player is not SamplePlayer sp) return;
			BuildSelectionManager.Instance?.RestoreBuildState(sp);
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
			// v2 存档只存储元进度（通关次数、循环次数等），不存储局内 HP。
			// 因此读档后以满血开始，而非从存档恢复血量。
			int targetHealth = Player.MaxHealth;
			int targetMaxHealth = Player.MaxHealth;
			Player.RestoreHealth(targetHealth, targetMaxHealth);
			
			GameLogger.Info(nameof(BattleSceneManager), $"应用游戏数据: 血量 {Player.CurrentHealth}/{Player.MaxHealth}, 等级 {0}");

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
		public void ShowLevelNamePopup(string? arrivalName = null)
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
				string levelName = arrivalName ?? LevelName;
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
			}

			if (_battleSettingsMenu != null && IsInstanceValid(_battleSettingsMenu))
			{
				_battleSettingsMenu.BackRequested -= OnSettingsBackRequested;
			}

			UIManager.Instance.UnloadBattleHUD();
			UIManager.Instance.UnloadBattleMenu();
			UIManager.Instance.UnloadSettingsMenu();
			UIManager.Instance.UnloadSkillWindow();
			UIManager.Instance.UnloadLevelNamePopup();

			_battleHUD = null;
			_battleMenu = null;
			_battleSettingsMenu = null;
			_skillWindow = null;
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
				// 清除构筑选择状态（已选核心/构筑卡）——返回主菜单 = 退出战斗，重进存档后重新选择构筑，
				// 否则 RestoreBuildState 会从残留记录把旧构筑效果恢复到玩家身上
				if (Kuros.Managers.BuildSelectionManager.Instance != null)
				{
					Kuros.Managers.BuildSelectionManager.Instance.ClearBuildState();
				}
				tree.ChangeSceneToFile("res://scenes/ui/menus/MainMenu.tscn");
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
		/// 过场动画开始时隐藏 HUD 和技能窗口（菜单类 UI 本身就是隐藏的，无需处理）。
		/// 由 CutsceneManager 调用。
		/// </summary>
		public void HideAllUI()
		{
			if (_battleHUD != null && IsInstanceValid(_battleHUD))
				_battleHUD.Hide();
			if (_skillWindow != null && IsInstanceValid(_skillWindow))
				_skillWindow.Hide();
			GameLogger.Info(nameof(BattleSceneManager), "[Cutscene] UI 已隐藏");
		}

		/// <summary>
		/// 过场动画结束时恢复 HUD 和技能窗口。
		/// 由 CutsceneManager 调用。
		/// </summary>
		public void ShowAllUI()
		{
			if (_battleHUD != null && IsInstanceValid(_battleHUD))
				_battleHUD.Show();
			if (_skillWindow != null && IsInstanceValid(_skillWindow))
				_skillWindow.Show();
			GameLogger.Info(nameof(BattleSceneManager), "[Cutscene] UI 已恢复");
		}

		public override void _ExitTree()
		{
			// 场景退出时清理UI
			UnloadAllUI();
		}
	}
}
