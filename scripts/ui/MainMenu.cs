using Godot;
using Kuros.Utils;

namespace Kuros.UI
{
	/// <summary>
	/// 主菜单界面 - 游戏的主入口界面
	/// </summary>
	public partial class MainMenu : Control
	{
		private const string CompendiumScenePath = "res://scenes/ui/windows/CompendiumWindow.tscn";

		[ExportCategory("UI References")]
		[Export] public Button StartGameButton { get; private set; } = null!;
		[Export] public Button ModeSelectionButton { get; private set; } = null!;
		[Export] public Button LoadGameButton { get; private set; } = null!;
		[Export] public Button SettingsButton { get; private set; } = null!;
		[Export] public Button QuitButton { get; private set; } = null!;
		[Export] public Button CompendiumButton { get; private set; } = null!;
		[Export] public CompendiumWindow? CompendiumWindow { get; private set; }

		private PackedScene? _compendiumScene;

		// 信号
		[Signal] public delegate void StartGameRequestedEventHandler();
		[Signal] public delegate void ModeSelectionRequestedEventHandler();
		[Signal] public delegate void LoadGameRequestedEventHandler();
		[Signal] public delegate void SettingsRequestedEventHandler();
		[Signal] public delegate void QuitRequestedEventHandler();

		public override void _Ready()
		{
			// 自动查找节点
			if (StartGameButton == null)
			{
				StartGameButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/StartGameButton");
			}

			if (ModeSelectionButton == null)
			{
				ModeSelectionButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/ModeSelectionButton");
			}

			if (CompendiumButton == null)
			{
				CompendiumButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/CompendiumButton");
			}

			if (LoadGameButton == null)
			{
				LoadGameButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/LoadGameButton");
			}

			if (SettingsButton == null)
			{
				SettingsButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SettingsButton");
			}

			if (QuitButton == null)
			{
				QuitButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/QuitButton");
			}

			CompendiumWindow ??= GetNodeOrNull<CompendiumWindow>("CompendiumWindow");
			CompendiumWindow ??= GetNodeOrNull<Control>("CompendiumWindow") as CompendiumWindow;

			// 连接按钮信号
			if (StartGameButton != null)
			{
				StartGameButton.Pressed += OnStartGamePressed;
			}

			if (ModeSelectionButton != null)
			{
				ModeSelectionButton.Pressed += OnModeSelectionPressed;
			}

			if (CompendiumButton != null)
			{
				CompendiumButton.Pressed += OnCompendiumPressed;
			}

			if (LoadGameButton != null)
			{
				LoadGameButton.Pressed += OnLoadGamePressed;
			}

			if (SettingsButton != null)
			{
				SettingsButton.Pressed += OnSettingsPressed;
			}

			if (QuitButton != null)
			{
				QuitButton.Pressed += OnQuitPressed;
			}
		}

		private void OnStartGamePressed()
		{
			EmitSignal(SignalName.StartGameRequested);
			GameLogger.Info(nameof(MainMenu), "开始游戏");
		}

		private void OnModeSelectionPressed()
		{
			EmitSignal(SignalName.ModeSelectionRequested);
			GameLogger.Info(nameof(MainMenu), "打开模式选择");
		}

		private void OnLoadGamePressed()
		{
			EmitSignal(SignalName.LoadGameRequested);
			GameLogger.Info(nameof(MainMenu), "打开存档管理界面");
		}

		private void OnSettingsPressed()
		{
			EmitSignal(SignalName.SettingsRequested);
			GameLogger.Info(nameof(MainMenu), "打开设置");
		}

		private void OnQuitPressed()
		{
			EmitSignal(SignalName.QuitRequested);
			GameLogger.Info(nameof(MainMenu), "退出游戏");
		}

		private void OnCompendiumPressed()
		{
			var window = GetOrCreateCompendiumWindow();
			if (window == null)
			{
				GD.PrintErr("CompendiumWindow 未找到");
				return;
			}

			if (window.Visible)
			{
				window.HideWindow();
			}
			else
			{
				window.ShowWindow();
			}
		}

		private CompendiumWindow? GetOrCreateCompendiumWindow()
		{
			if (CompendiumWindow != null)
			{
				return CompendiumWindow;
			}

			_compendiumScene ??= GD.Load<PackedScene>(CompendiumScenePath);
			if (_compendiumScene == null)
			{
				GD.PrintErr("无法加载图鉴窗口：", CompendiumScenePath);
				return null;
			}

			var instance = _compendiumScene.Instantiate();
			if (instance is not CompendiumWindow window)
			{
				GD.PrintErr("CompendiumWindow 场景存在，但脚本未正确加载。");
				instance.QueueFree();
				return null;
			}

			AddChild(window);
			// HideWindow() is called in CompendiumWindow._Ready(), so no need to call it here
			CompendiumWindow = window;
			return CompendiumWindow;
		}
	}
}
