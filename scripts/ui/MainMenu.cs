using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 主菜单界面 - 游戏的主入口界面
    /// </summary>
    public partial class MainMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button StartGameButton { get; private set; } = null!;
        [Export] public Button ModeSelectionButton { get; private set; } = null!;
        [Export] public Button LoadGameButton { get; private set; } = null!;
        [Export] public Button SettingsButton { get; private set; } = null!;
        [Export] public Button QuitButton { get; private set; } = null!;

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

            // 连接按钮信号
            if (StartGameButton != null)
            {
                StartGameButton.Pressed += OnStartGamePressed;
            }

            if (ModeSelectionButton != null)
            {
                ModeSelectionButton.Pressed += OnModeSelectionPressed;
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
            GD.Print("开始游戏");
        }

        private void OnModeSelectionPressed()
        {
            EmitSignal(SignalName.ModeSelectionRequested);
            GD.Print("打开模式选择");
        }

        private void OnLoadGamePressed()
        {
            EmitSignal(SignalName.LoadGameRequested);
            GD.Print("打开存档选择");
        }

        private void OnSettingsPressed()
        {
            EmitSignal(SignalName.SettingsRequested);
            GD.Print("打开设置");
        }

        private void OnQuitPressed()
        {
            EmitSignal(SignalName.QuitRequested);
            GD.Print("退出游戏");
        }
    }
}

