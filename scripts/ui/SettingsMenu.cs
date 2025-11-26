using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 设置菜单 - 游戏设置界面
    /// </summary>
    public partial class SettingsMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public HSlider MasterVolumeSlider { get; private set; } = null!;
        [Export] public HSlider MusicVolumeSlider { get; private set; } = null!;
        [Export] public HSlider SFXVolumeSlider { get; private set; } = null!;
        [Export] public OptionButton ResolutionOption { get; private set; } = null!;
        [Export] public CheckBox FullscreenCheckBox { get; private set; } = null!;
        [Export] public OptionButton LanguageOption { get; private set; } = null!;

        // 信号
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void SettingsChangedEventHandler();

        public override void _Ready()
        {
            // 确保在游戏暂停时也能接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点
            if (BackButton == null)
            {
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            }

            if (MasterVolumeSlider != null)
            {
                MasterVolumeSlider.ValueChanged += OnMasterVolumeChanged;
                MasterVolumeSlider.Value = 100.0; // 默认值
            }

            if (MusicVolumeSlider != null)
            {
                MusicVolumeSlider.ValueChanged += OnMusicVolumeChanged;
                MusicVolumeSlider.Value = 100.0;
            }

            if (SFXVolumeSlider != null)
            {
                SFXVolumeSlider.ValueChanged += OnSFXVolumeChanged;
                SFXVolumeSlider.Value = 100.0;
            }

            if (ResolutionOption != null)
            {
                ResolutionOption.ItemSelected += OnResolutionSelected;
                // 添加分辨率选项
                ResolutionOption.AddItem("1280x720");
                ResolutionOption.AddItem("1920x1080");
                ResolutionOption.AddItem("2560x1440");
            }

            if (FullscreenCheckBox != null)
            {
                FullscreenCheckBox.Toggled += OnFullscreenToggled;
            }

            if (LanguageOption != null)
            {
                LanguageOption.ItemSelected += OnLanguageSelected;
                LanguageOption.AddItem("简体中文");
                LanguageOption.AddItem("English");
            }

            if (BackButton != null)
            {
                BackButton.Pressed += OnBackPressed;
            }
        }

        private void OnMasterVolumeChanged(double value)
        {
            // 这里可以设置主音量
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), (float)(value - 100) / 2.0f);
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnMusicVolumeChanged(double value)
        {
            // 这里可以设置音乐音量
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnSFXVolumeChanged(double value)
        {
            // 这里可以设置音效音量
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnResolutionSelected(long index)
        {
            // 这里可以改变分辨率
            var resolutions = new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(2560, 1440) };
            if (index < resolutions.Length)
            {
                DisplayServer.WindowSetSize(resolutions[index]);
            }
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnFullscreenToggled(bool pressed)
        {
            if (pressed)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }
            else
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            }
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnLanguageSelected(long index)
        {
            // 这里可以改变语言
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }
    }
}

