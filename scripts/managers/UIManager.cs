using Godot;
using System.Collections.Generic;
using Kuros.UI;

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

		// 当前加载的UI节点
		private Dictionary<string, Node> _loadedUIs = new Dictionary<string, Node>();
		
		// UI容器 - 用于存放不同类型的UI层
		private CanvasLayer _hudLayer = null!;
		private CanvasLayer _menuLayer = null!;

		public override void _Ready()
		{
			Instance = this;
			
			// 创建UI层容器
			_hudLayer = new CanvasLayer();
			_hudLayer.Name = "HUDLayer";
			_hudLayer.Layer = 1; // HUD层
			AddChild(_hudLayer);

			_menuLayer = new CanvasLayer();
			_menuLayer.Name = "MenuLayer";
			_menuLayer.Layer = 2; // 菜单层（在HUD之上）
			AddChild(_menuLayer);
		}

		/// <summary>
		/// 加载并显示UI场景
		/// </summary>
		/// <param name="uiPath">UI场景路径</param>
		/// <param name="layer">UI层类型（HUD或Menu）</param>
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
					if (existing is CanvasItem canvasItem)
					{
						canvasItem.Visible = true;
					}
					return typedNode;
				}
			}

			// 加载场景
			var scene = GD.Load<PackedScene>(uiPath);
			if (scene == null)
			{
				GD.PrintErr($"UIManager: 无法加载UI场景: {uiPath}");
				GD.PrintErr($"UIManager: 请检查文件路径是否正确，文件是否存在");
				return null!;
			}

			var uiNode = scene.Instantiate<T>();
			if (uiNode == null)
			{
				GD.PrintErr($"UIManager: UI场景实例化失败: {uiPath}");
				GD.PrintErr($"UIManager: 请检查场景文件的根节点类型是否与泛型类型T匹配");
				return null!;
			}

			// 添加到对应的层
			CanvasLayer targetLayer = layer == UILayer.HUD ? _hudLayer : _menuLayer;
			targetLayer.AddChild(uiNode);

			// 存储引用
			_loadedUIs[key] = uiNode;

			GD.Print($"UIManager: 已加载UI: {key} (Layer: {layer})");
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
				GD.Print($"UIManager: 已卸载UI: {key}");
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
				if (uiNode is CanvasItem canvasItem)
				{
					canvasItem.Visible = visible;
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
	}

	/// <summary>
	/// UI层类型枚举
	/// </summary>
	public enum UILayer
	{
		HUD,    // 游戏内HUD（血条、分数等）
		Menu    // 菜单层（暂停菜单、设置等）
	}
}
