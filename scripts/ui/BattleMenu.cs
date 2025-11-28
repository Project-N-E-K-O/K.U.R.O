using Godot;
using Kuros.Utils;

namespace Kuros.UI
{
    /// <summary>
    /// 战斗菜单 - 暂停菜单
    /// 通过ESC键打开/关闭
    /// </summary>
    public partial class BattleMenu : Control
    {
        private const string CompendiumScenePath = "res://scenes/ui/windows/CompendiumWindow.tscn";
        private const string PauseToggleAction = "ui_cancel";

        // 信号
        [Signal] public delegate void ResumeRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void QuitRequestedEventHandler();
        [Signal] public delegate void ExitGameRequestedEventHandler();

        [ExportCategory("UI References")]
        [Export] public Button ResumeButton { get; private set; } = null!;
        [Export] public Button SettingsButton { get; private set; } = null!;
        [Export] public Button CompendiumButton { get; private set; } = null!;
        [Export] public Button QuitButton { get; private set; } = null!;
        [Export] public Button ExitButton { get; private set; } = null!;

        private bool _isOpen = false;
        private CompendiumWindow? _compendiumWindow;
        private PackedScene? _compendiumScene;

        public bool IsOpen => _isOpen;

        public override void _Ready()
        {
            // 暂停时也要接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点引用
            ResumeButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/ResumeButton");
            SettingsButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/SettingsButton");
            CompendiumButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/CompendiumButton");
            QuitButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/QuitButton");
            ExitButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/ExitButton");

            // 连接按钮信号
            if (ResumeButton != null)
                ResumeButton.Pressed += OnResumePressed;
            if (SettingsButton != null)
                SettingsButton.Pressed += OnSettingsPressed;
            if (CompendiumButton != null)
                CompendiumButton.Pressed += OnCompendiumPressed;
            if (QuitButton != null)
                QuitButton.Pressed += OnQuitPressed;
            if (ExitButton != null)
                ExitButton.Pressed += OnExitGamePressed;

            LoadCompendiumWindow();

            // 延迟确保隐藏（在UIManager设置可见之后）
            CallDeferred(MethodName.EnsureHidden);
        }

        public override void _Input(InputEvent @event)
        {
            if (@event.IsActionPressed(PauseToggleAction))
            {
                ToggleMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        private void LoadCompendiumWindow()
        {
            _compendiumScene ??= GD.Load<PackedScene>(CompendiumScenePath);
            if (_compendiumScene == null)
            {
                GD.PrintErr("无法加载图鉴窗口场景：", CompendiumScenePath);
                return;
            }

            _compendiumWindow = _compendiumScene.Instantiate<CompendiumWindow>();
            AddChild(_compendiumWindow);
            // HideWindow() is called in CompendiumWindow._Ready(), so no need to call it here
        }

        public void OpenMenu()
        {
            if (_isOpen) return;

            Visible = true;
            _isOpen = true;
            GetTree().Paused = true;
        }

        public void CloseMenu()
        {
            if (!_isOpen) return;

            Visible = false;
            _isOpen = false;
            GetTree().Paused = false;
        }

        public void ToggleMenu()
        {
            if (_isOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        private void EnsureHidden()
        {
            if (!_isOpen)
            {
                Visible = false;
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
            GameLogger.Info(nameof(BattleMenu), "打开设置菜单");
        }

        private void OnQuitPressed()
        {
            // 先关闭菜单并取消暂停
            CloseMenu();
            EmitSignal(SignalName.QuitRequested);
        }

        private void OnExitGamePressed()
        {
            EmitSignal(SignalName.ExitGameRequested);
            GetTree().Quit();
        }

        private void OnCompendiumPressed()
        {
            if (_compendiumWindow == null)
            {
                GD.PrintErr("图鉴窗口未创建");
                return;
            }

            if (_compendiumWindow.Visible)
            {
                _compendiumWindow.HideWindow();
            }
            else
            {
                _compendiumWindow.ShowWindow();
            }
        }
    }
}
