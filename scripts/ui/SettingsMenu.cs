using Godot;
using Kuros.Managers;

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
		[Export] public OptionButton WindowModeOption { get; private set; } = null!;
        [Export] public OptionButton LanguageOption { get; private set; } = null!;

        // 信号
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void SettingsChangedEventHandler();

		private bool _suppressWindowSelection = false;

        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接按钮信号
        /// 这种方式在导出版本中比 C# 委托方式更可靠
        /// </summary>
        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接滑块信号
        /// </summary>
        private void ConnectSliderSignal(Slider? slider, string methodName)
        {
            if (slider == null) return;
            var callable = new Callable(this, methodName);
            if (!slider.IsConnected(Slider.SignalName.ValueChanged, callable))
            {
                slider.Connect(Slider.SignalName.ValueChanged, callable);
            }
        }

        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接选项按钮信号
        /// </summary>
        private void ConnectOptionButtonSignal(OptionButton? optionButton, string methodName)
        {
            if (optionButton == null) return;
            var callable = new Callable(this, methodName);
            if (!optionButton.IsConnected(OptionButton.SignalName.ItemSelected, callable))
            {
                optionButton.Connect(OptionButton.SignalName.ItemSelected, callable);
            }
        }

        public override void _Ready()
        {
            // 确保在游戏暂停时也能接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点
            if (BackButton == null)
            {
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            }

            if (MasterVolumeSlider == null)
            {
                MasterVolumeSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/MasterVolumeSlider");
            }
            if (MasterVolumeSlider != null)
            {
                ConnectSliderSignal(MasterVolumeSlider, nameof(OnMasterVolumeChanged));
                MasterVolumeSlider.Value = 100.0;
            }

            if (MusicVolumeSlider == null)
            {
                MusicVolumeSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/MusicVolumeSlider");
            }
            if (MusicVolumeSlider != null)
            {
                ConnectSliderSignal(MusicVolumeSlider, nameof(OnMusicVolumeChanged));
                MusicVolumeSlider.Value = 100.0;
            }

            if (SFXVolumeSlider == null)
            {
                SFXVolumeSlider = GetNodeOrNull<HSlider>("MenuPanel/VBoxContainer/SFXVolumeSlider");
            }
            if (SFXVolumeSlider != null)
            {
                ConnectSliderSignal(SFXVolumeSlider, nameof(OnSFXVolumeChanged));
                SFXVolumeSlider.Value = 100.0;
            }

			SetupWindowModeOption();

            if (LanguageOption == null)
            {
                LanguageOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/LanguageOption");
            }
            if (LanguageOption != null)
            {
                LanguageOption.Clear();
                ConnectOptionButtonSignal(LanguageOption, nameof(OnLanguageSelected));
                LanguageOption.AddItem("简体中文");
                LanguageOption.AddItem("English");
            }

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(BackButton, nameof(OnBackPressed));
        }

        private void OnMasterVolumeChanged(double value)
        {
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), (float)(value - 100) / 2.0f);
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnMusicVolumeChanged(double value)
        {
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnSFXVolumeChanged(double value)
        {
            EmitSignal(SignalName.SettingsChanged);
        }

        private void SetupWindowModeOption()
        {
            if (WindowModeOption == null)
            {
                WindowModeOption = GetNodeOrNull<OptionButton>("MenuPanel/VBoxContainer/WindowModeOption");
            }

			if (WindowModeOption == null) return;

			var settings = GameSettingsManager.Instance;
			if (settings == null) return;

			WindowModeOption.Clear();
			var presets = settings.Presets;
			for (int i = 0; i < presets.Length; i++)
			{
				WindowModeOption.AddItem(presets[i].DisplayName, i);
			}
			ConnectOptionButtonSignal(WindowModeOption, nameof(OnWindowModeSelected));

			RestoreSelectedPreset();
		}

		private void OnWindowModeSelected(long index)
		{
			if (_suppressWindowSelection || WindowModeOption == null)
				return;

			var settings = GameSettingsManager.Instance;
			if (settings == null) return;

			if (index < 0 || index >= settings.Presets.Length)
				return;

			// 直接应用并保存窗口模式，无需重启游戏
			var preset = settings.GetPresetByIndex((int)index);
			settings.SetPreset(preset.Id, applyImmediately: true);
			EmitSignal(SignalName.SettingsChanged);
		}

		private void RestoreSelectedPreset()
		{
			var settings = GameSettingsManager.Instance;
			if (settings == null || WindowModeOption == null) return;

			_suppressWindowSelection = true;
			WindowModeOption.Selected = settings.GetPresetIndex(settings.CurrentPreset.Id);
			_suppressWindowSelection = false;
        }

        private void OnLanguageSelected(long index)
        {
            EmitSignal(SignalName.SettingsChanged);
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }

        public override void _Input(InputEvent @event)
        {
            // 只有在控件可见时才处理输入
            if (!IsVisibleInTree())
            {
                return;
            }
            
            // 检查ESC键
            bool isEscKey = false;
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
            {
                isEscKey = true;
            }
            
            if (isEscKey)
            {
                // ESC键返回上一层
                OnBackPressed();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
