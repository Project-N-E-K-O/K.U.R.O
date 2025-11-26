using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 战斗菜单 - 暂停菜单、设置等
    /// 可以通过ESC键或按钮打开/关闭
    /// </summary>
    public partial class BattleMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button ResumeButton { get; private set; } = null!;
        [Export] public Button SettingsButton { get; private set; } = null!;
        [Export] public Button QuitButton { get; private set; } = null!;
        [Export] public Control MenuPanel { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public bool PauseGameWhenOpen = true;

        // 信号
        [Signal] public delegate void MenuOpenedEventHandler();
        [Signal] public delegate void MenuClosedEventHandler();
        [Signal] public delegate void ResumeRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void QuitRequestedEventHandler();

        private bool _isOpen = false;

        public override void _Ready()
        {
            // Pause下也要接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点
            if (MenuPanel == null)
            {
                MenuPanel = GetNodeOrNull<Control>("MenuPanel");
            }

            if (ResumeButton == null)
            {
                ResumeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/ResumeButton");
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
            if (ResumeButton != null)
            {
                ResumeButton.Pressed += OnResumePressed;
            }

            if (SettingsButton != null)
            {
                SettingsButton.Pressed += OnSettingsPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed += OnQuitPressed;
            }

            // 初始状态：隐藏菜单
            SetMenuVisible(false);
        }

        public override void _Input(InputEvent @event)
        {
            // ESC键切换菜单
            if (@event.IsActionPressed("ui_cancel"))
            {
                ToggleMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>
        /// 打开菜单
        /// </summary>
        public void OpenMenu()
        {
            if (_isOpen) return;

            SetMenuVisible(true);
            _isOpen = true;

            if (PauseGameWhenOpen)
            {
                GetTree().Paused = true;
            }

            EmitSignal(SignalName.MenuOpened);
        }

        /// <summary>
        /// 关闭菜单
        /// </summary>
        public void CloseMenu()
        {
            if (!_isOpen) return;

            SetMenuVisible(false);
            _isOpen = false;

            if (PauseGameWhenOpen)
            {
                GetTree().Paused = false;
            }

            EmitSignal(SignalName.MenuClosed);
        }

        /// <summary>
        /// 切换菜单状态
        /// </summary>
        public void ToggleMenu()
        {
            if (_isOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        private void SetMenuVisible(bool visible)
        {
            Visible = visible;
            if (MenuPanel != null)
            {
                MenuPanel.Visible = visible;
            }
        }

        private void OnResumePressed()
        {
            EmitSignal(SignalName.ResumeRequested);
            CloseMenu();
        }

        private void OnSettingsPressed()
        {
            EmitSignal(SignalName.SettingsRequested);
            // 这里可以打开设置菜单
            GD.Print("打开设置菜单");
        }

        private void OnQuitPressed()
        {
            EmitSignal(SignalName.QuitRequested);
            // 场景切换逻辑由BattleSceneManager处理
        }

        public bool IsOpen => _isOpen;
    }
}

