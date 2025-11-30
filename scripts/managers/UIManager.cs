using Godot;
using System.Collections.Generic;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Managers
{
	/// <summary>
	/// UI管理器 - 负责管理所有UI场景的加载、显示和卸载
	/// 使用单例模式，需要在project.godot中配置为autoload
	/// </summary>
	public partial class UIManager : Node
	{
		// 单例实例
		public static UIManager Instance { get; private set; } = null!;

		// UI场景路径
		private const string BATTLE_HUD_PATH = "res://scenes/ui/hud/BattleHUD.tscn";
		private const string BATTLE_MENU_PATH = "res://scenes/ui/menus/BattleMenu.tscn";
		private const string MAIN_MENU_PATH = "res://scenes/ui/menus/MainMenu.tscn";
		private const string MODE_SELECTION_PATH = "res://scenes/ui/menus/ModeSelectionMenu.tscn";
		private const string SETTINGS_MENU_PATH = "res://scenes/ui/menus/SettingsMenu.tscn";
		private const string SAVE_SLOT_SELECTION_PATH = "res://scenes/ui/menus/SaveSlotSelection.tscn";
		private const string DIALOGUE_WINDOW_PATH = "res://scenes/ui/windows/DialogueWindow.tscn";
		private const string SKILL_WINDOW_PATH = "res://scenes/ui/windows/SkillWindow.tscn";
		private const string INVENTORY_WINDOW_PATH = "res://scenes/ui/windows/InventoryWindow.tscn";
		private const string LOADING_SCREEN_PATH = "res://scenes/ui/LoadingScreen.tscn";
		private const string LEVEL_NAME_POPUP_PATH = "res://scenes/ui/LevelNamePopup.tscn";
		private const string ITEM_OBTAINED_POPUP_PATH = "res://scenes/ui/ItemObtainedPopup.tscn";

		// 当前加载的UI节点
		private Dictionary<string, Node> _loadedUIs = new Dictionary<string, Node>();
		
		// UI容器 - 用于存放不同类型的UI层
		private CanvasLayer _hudLayer = null!;
		private CanvasLayer _gameUILayer = null!; // 游戏UI层（物品栏、技能栏等，在HUD之上）
		private CanvasLayer _menuLayer = null!;

		public override void _Ready()
		{
			Instance = this;
			
			// 创建UI层容器
			_hudLayer = new CanvasLayer();
			_hudLayer.Name = "HUDLayer";
			_hudLayer.Layer = 1; // HUD层（玩家状态）
			_hudLayer.Visible = true; // 确保HUD层可见
			AddChild(_hudLayer);

			_gameUILayer = new CanvasLayer();
			_gameUILayer.Name = "GameUILayer";
			_gameUILayer.Layer = 2; // 游戏UI层（物品栏、技能栏等，在HUD之上）
			_gameUILayer.Visible = true; // 确保游戏UI层可见
			AddChild(_gameUILayer);

			_menuLayer = new CanvasLayer();
			_menuLayer.Name = "MenuLayer";
			_menuLayer.Layer = 3; // 菜单层（在游戏UI之上）
			_menuLayer.Visible = true; // 确保菜单层可见
			AddChild(_menuLayer);
		}

		/// <summary>
		/// 加载并显示UI场景
		/// </summary>
		/// <param name="uiPath">UI场景路径</param>
		/// <param name="layer">UI层类型（HUD、GameUI或Menu）</param>
		/// <param name="key">UI的唯一标识符，用于后续引用和卸载</param>
		/// <returns>加载的UI节点</returns>
		public T LoadUI<T>(string uiPath, UILayer layer = UILayer.HUD, string? key = null) where T : Node
		{
			// 如果没有提供key，使用路径作为key
			if (string.IsNullOrEmpty(key))
			{
				key = uiPath;
			}

			// 如果已经加载，直接返回
			if (_loadedUIs.ContainsKey(key))
			{
				var existing = _loadedUIs[key];
				if (existing is T typedNode)
				{
					// 如果是CanvasItem，设置可见性
					// 但对于 InventoryWindow，不要自动显示（它有自己的显示/隐藏逻辑）
					if (existing is CanvasItem existingCanvasItem && !(existing is InventoryWindow))
					{
						existingCanvasItem.Visible = true;
					}
					return typedNode;
				}
			}

			// 加载场景
			var scene = GD.Load<PackedScene>(uiPath);
			if (scene == null)
			{
				GameLogger.Error(nameof(UIManager), $"无法加载UI场景: {uiPath}");
				GameLogger.Error(nameof(UIManager), "请检查文件路径是否正确，文件是否存在");
				return null!;
			}

			var uiNode = scene.Instantiate<T>();
			if (uiNode == null)
			{
				GameLogger.Error(nameof(UIManager), $"UI场景实例化失败: {uiPath}");
				GameLogger.Error(nameof(UIManager), "请检查场景文件的根节点类型是否与泛型类型T匹配");
				return null!;
			}

			// 添加到对应的层
			CanvasLayer targetLayer = layer switch
			{
				UILayer.HUD => _hudLayer,
				UILayer.GameUI => _gameUILayer,
				UILayer.Menu => _menuLayer,
				_ => _hudLayer
			};
			targetLayer.AddChild(uiNode);

			// 确保UI节点是可见的（如果它是CanvasItem）
			// 但对于 InventoryWindow，不要自动显示（它有自己的显示/隐藏逻辑）
			if (uiNode is CanvasItem newCanvasItem && !(uiNode is InventoryWindow))
			{
				newCanvasItem.Visible = true;
			}

			// 存储引用
			_loadedUIs[key] = uiNode;

			GameLogger.Info(nameof(UIManager), $"已加载UI: {key} (Layer: {layer})");
			return uiNode;
		}

		/// <summary>
		/// 卸载UI
		/// </summary>
		public void UnloadUI(string key)
		{
			if (_loadedUIs.TryGetValue(key, out var uiNode))
			{
				uiNode.QueueFree();
				_loadedUIs.Remove(key);
				GameLogger.Info(nameof(UIManager), $"已卸载UI: {key}");
			}
		}

		/// <summary>
		/// 获取已加载的UI
		/// </summary>
		public T GetUI<T>(string key) where T : Node
		{
			if (_loadedUIs.TryGetValue(key, out var uiNode) && uiNode is T typedNode)
			{
				return typedNode;
			}
			return null!;
		}

		/// <summary>
		/// 显示/隐藏UI
		/// </summary>
		public void SetUIVisible(string key, bool visible)
		{
			if (_loadedUIs.TryGetValue(key, out var uiNode))
			{
				if (uiNode is CanvasItem targetCanvasItem)
				{
					targetCanvasItem.Visible = visible;
				}
			}
		}

		/// <summary>
		/// 清除所有UI
		/// </summary>
		public void ClearAllUI()
		{
			foreach (var ui in _loadedUIs.Values)
			{
				ui.QueueFree();
			}
			_loadedUIs.Clear();
		}

		// 便捷方法：加载战斗HUD
		public BattleHUD LoadBattleHUD()
		{
			return LoadUI<BattleHUD>(BATTLE_HUD_PATH, UILayer.HUD, "BattleHUD");
		}

		// 便捷方法：加载战斗菜单
		public BattleMenu LoadBattleMenu()
		{
			return LoadUI<BattleMenu>(BATTLE_MENU_PATH, UILayer.Menu, "BattleMenu");
		}

		// 便捷方法：卸载战斗HUD
		public void UnloadBattleHUD()
		{
			UnloadUI("BattleHUD");
		}

		// 便捷方法：卸载战斗菜单
		public void UnloadBattleMenu()
		{
			UnloadUI("BattleMenu");
		}

		// 便捷方法：加载主菜单
		public MainMenu LoadMainMenu()
		{
			return LoadUI<MainMenu>(MAIN_MENU_PATH, UILayer.Menu, "MainMenu");
		}

		// 便捷方法：加载模式选择菜单
		public ModeSelectionMenu LoadModeSelectionMenu()
		{
			return LoadUI<ModeSelectionMenu>(MODE_SELECTION_PATH, UILayer.Menu, "ModeSelectionMenu");
		}

		// 便捷方法：加载设置菜单
		public SettingsMenu LoadSettingsMenu()
		{
			return LoadUI<SettingsMenu>(SETTINGS_MENU_PATH, UILayer.Menu, "SettingsMenu");
		}

		// 便捷方法：加载存档选择菜单
		public SaveSlotSelection LoadSaveSlotSelection()
		{
			return LoadUI<SaveSlotSelection>(SAVE_SLOT_SELECTION_PATH, UILayer.Menu, "SaveSlotSelection");
		}

		// 便捷方法：卸载主菜单
		public void UnloadMainMenu()
		{
			UnloadUI("MainMenu");
		}

		// 便捷方法：卸载模式选择菜单
		public void UnloadModeSelectionMenu()
		{
			UnloadUI("ModeSelectionMenu");
		}

		// 便捷方法：卸载设置菜单
		public void UnloadSettingsMenu()
		{
			UnloadUI("SettingsMenu");
		}

		// 便捷方法：卸载存档选择菜单
		public void UnloadSaveSlotSelection()
		{
			UnloadUI("SaveSlotSelection");
		}

		// 便捷方法：加载对话窗口
		public DialogueWindow LoadDialogueWindow()
		{
			return LoadUI<DialogueWindow>(DIALOGUE_WINDOW_PATH, UILayer.Menu, "DialogueWindow");
		}

		// 便捷方法：卸载对话窗口
		public void UnloadDialogueWindow()
		{
			UnloadUI("DialogueWindow");
		}

		// 便捷方法：加载技能窗口（放在GameUI层，在HUD之上）
		public SkillWindow LoadSkillWindow()
		{
			return LoadUI<SkillWindow>(SKILL_WINDOW_PATH, UILayer.GameUI, "SkillWindow");
		}

		// 便捷方法：卸载技能窗口
		public void UnloadSkillWindow()
		{
			UnloadUI("SkillWindow");
		}

		// 便捷方法：加载物品栏窗口（放在GameUI层，在HUD之上，和SkillWindow同一层）
		public InventoryWindow LoadInventoryWindow()
		{
			return LoadUI<InventoryWindow>(INVENTORY_WINDOW_PATH, UILayer.GameUI, "InventoryWindow");
		}

		// 便捷方法：卸载物品栏窗口
		public void UnloadInventoryWindow()
		{
			UnloadUI("InventoryWindow");
		}
		
		// 便捷方法：加载加载屏幕
		public LoadingScreen LoadLoadingScreen()
		{
			return LoadUI<LoadingScreen>(LOADING_SCREEN_PATH, UILayer.Menu, "LoadingScreen");
		}
		
		// 便捷方法：卸载加载屏幕
		public void UnloadLoadingScreen()
		{
			UnloadUI("LoadingScreen");
		}
		
		// 便捷方法：加载关卡名称弹窗
		public LevelNamePopup LoadLevelNamePopup()
		{
			return LoadUI<LevelNamePopup>(LEVEL_NAME_POPUP_PATH, UILayer.Menu, "LevelNamePopup");
		}
		
		// 便捷方法：卸载关卡名称弹窗
		public void UnloadLevelNamePopup()
		{
			UnloadUI("LevelNamePopup");
		}
		
		// 便捷方法：加载获得物品弹窗
		public ItemObtainedPopup LoadItemObtainedPopup()
		{
			return LoadUI<ItemObtainedPopup>(ITEM_OBTAINED_POPUP_PATH, UILayer.Menu, "ItemObtainedPopup");
		}
		
		// 便捷方法：卸载获得物品弹窗
		public void UnloadItemObtainedPopup()
		{
			UnloadUI("ItemObtainedPopup");
		}
	}

	/// <summary>
	/// UI层类型枚举
	/// </summary>
	public enum UILayer
	{
		HUD,     // 游戏内HUD（血条、分数等）- Layer 1
		GameUI,  // 游戏UI层（物品栏、技能栏等，在HUD之上）- Layer 2
		Menu     // 菜单层（暂停菜单、设置等，在游戏UI之上）- Layer 3
	}
}
