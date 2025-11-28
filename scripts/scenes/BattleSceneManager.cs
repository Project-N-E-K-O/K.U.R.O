using Godot;
using Kuros.Core;
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

		private BattleHUD? _battleHUD;
		private BattleMenu? _battleMenu;

		public override void _Ready()
		{
			// 延迟查找Player和加载UI，确保场景树完全构建
			CallDeferred(MethodName.InitializeBattleScene);
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

			// 加载UI
			LoadUIs();
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
				if (Player != null)
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
				// 连接菜单信号
				_battleMenu.ResumeRequested += OnMenuResume;
				_battleMenu.QuitRequested += OnMenuQuit;
				_battleMenu.SettingsRequested += OnMenuSettingsRequested;
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

			_battleHUD = null;
			_battleMenu = null;
			_battleSettingsMenu = null;
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
				tree.Paused = false;
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

		public override void _ExitTree()
		{
			// 场景退出时清理UI
			UnloadAllUI();
		}
	}
}
