using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 战斗菜单 - 暂停菜单
    /// 通过ESC键打开/关闭
    /// </summary>
    public partial class BattleMenu : Control
    {
        // 信号
        [Signal] public delegate void ResumeRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void QuitRequestedEventHandler();
        [Signal] public delegate void ExitGameRequestedEventHandler();

        private bool _isOpen = false;
        private Control _menuContainer = null!;

        public bool IsOpen => _isOpen;

        public override void _Ready()
        {
            // 暂停时也要接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 创建菜单UI
            CreateMenuUI();

            // 初始隐藏
            _menuContainer.Visible = false;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event.IsActionPressed("Return"))
            {
                ToggleMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        private void CreateMenuUI()
        {
            // 菜单容器 - 设置为Always以在暂停时也能响应
            _menuContainer = new Control();
            _menuContainer.Name = "MenuContainer";
            _menuContainer.SetAnchorsPreset(LayoutPreset.FullRect);
            _menuContainer.ProcessMode = ProcessModeEnum.Always;
            AddChild(_menuContainer);

            // 背景遮罩
            var background = new ColorRect();
            background.Name = "Background";
            background.SetAnchorsPreset(LayoutPreset.FullRect);
            background.Color = new Color(0, 0, 0, 0.7f);
            _menuContainer.AddChild(background);

            // 面板
            var panel = new Panel();
            panel.Name = "Panel";
            panel.SetAnchorsPreset(LayoutPreset.Center);
            panel.CustomMinimumSize = new Vector2(400, 350);
            panel.Position = new Vector2(-200, -175);
            _menuContainer.AddChild(panel);

            // 按钮容器
            var vbox = new VBoxContainer();
            vbox.Name = "VBoxContainer";
            vbox.SetAnchorsPreset(LayoutPreset.Center);
            vbox.Position = new Vector2(-150, -140);
            vbox.CustomMinimumSize = new Vector2(300, 280);
            vbox.AddThemeConstantOverride("separation", 20);
            _menuContainer.AddChild(vbox);

            // 标题
            var title = new Label();
            title.Text = "暂停菜单";
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.AddThemeFontSizeOverride("font_size", 32);
            vbox.AddChild(title);

            // 分隔线
            var separator = new HSeparator();
            vbox.AddChild(separator);

            // 继续游戏按钮
            var resumeBtn = new Button();
            resumeBtn.Text = "继续游戏";
            resumeBtn.AddThemeFontSizeOverride("font_size", 20);
            resumeBtn.Pressed += OnResumePressed;
            vbox.AddChild(resumeBtn);

            // 设置按钮
            var settingsBtn = new Button();
            settingsBtn.Text = "设置";
            settingsBtn.AddThemeFontSizeOverride("font_size", 20);
            settingsBtn.Pressed += OnSettingsPressed;
            vbox.AddChild(settingsBtn);

            // 返回主菜单按钮
            var quitBtn = new Button();
            quitBtn.Text = "返回主菜单";
            quitBtn.AddThemeFontSizeOverride("font_size", 20);
            quitBtn.Pressed += OnQuitPressed;
            vbox.AddChild(quitBtn);

            // 退出游戏按钮
            var exitBtn = new Button();
            exitBtn.Text = "退出游戏";
            exitBtn.AddThemeFontSizeOverride("font_size", 20);
            exitBtn.Pressed += OnExitGamePressed;
            vbox.AddChild(exitBtn);
        }

        public void OpenMenu()
        {
            if (_isOpen) return;

            _menuContainer.Visible = true;
            _isOpen = true;
            GetTree().Paused = true;
        }

        public void CloseMenu()
        {
            if (!_isOpen) return;

            _menuContainer.Visible = false;
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

        private void OnResumePressed()
        {
            EmitSignal(SignalName.ResumeRequested);
            CloseMenu();
        }

        private void OnSettingsPressed()
        {
            EmitSignal(SignalName.SettingsRequested);
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
    }
}
