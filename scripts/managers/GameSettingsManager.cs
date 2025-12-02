using Godot;
using System;

namespace Kuros.Managers
{
	/// <summary>
	/// 游戏设置管理器：负责加载/保存窗口模式配置并在启动时应用
	/// </summary>
	public partial class GameSettingsManager : Node
	{
		public static GameSettingsManager Instance { get; private set; } = null!;

		private const string ConfigPath = "user://config/window_settings.cfg";
		private const string WindowSection = "Window";
		private const string PresetKey = "Preset";

		private readonly WindowPreset[] _presets =
		{
			new WindowPreset("fullscreen_1080p", "全屏 1920x1080", DisplayServer.WindowMode.Fullscreen, new Vector2I(1920, 1080)),
			new WindowPreset("window_1080p", "窗口 1920x1080", DisplayServer.WindowMode.Windowed, new Vector2I(1920, 1080)),
			new WindowPreset("window_720p", "窗口 1280x720", DisplayServer.WindowMode.Windowed, new Vector2I(1280, 720)),
		};

		private string _currentPresetId = "window_1080p";

		public WindowPreset CurrentPreset => GetPresetById(_currentPresetId);
		public WindowPreset[] Presets => _presets;

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}

			Instance = this;
			EnsureConfigDirectoryExists();
			LoadSettings();
			ApplyCurrentPreset();
		}

		/// <summary>
		/// 确保配置目录存在
		/// </summary>
		private void EnsureConfigDirectoryExists()
		{
			var dirAccess = DirAccess.Open("user://");
			if (dirAccess == null)
			{
				GD.PrintErr("GameSettingsManager: 无法打开 user:// 目录");
				return;
			}

			if (!dirAccess.DirExists("config"))
			{
				var err = dirAccess.MakeDir("config");
				if (err != Error.Ok)
				{
					GD.PrintErr($"GameSettingsManager: 无法创建 config 目录，错误: {err}");
				}
				else
				{
					GD.Print("GameSettingsManager: 已创建 config 目录");
				}
			}
		}

		public void ApplyCurrentPreset()
		{
			var preset = CurrentPreset;
			DisplayServer.WindowSetMode(preset.Mode);

			if (preset.Mode == DisplayServer.WindowMode.Windowed)
			{
				DisplayServer.WindowSetSize(preset.Size);
				CenterWindow();
			}

			ApplyProjectSettings(preset);
		}

		public void SetPreset(string presetId, bool applyImmediately)
		{
			if (string.IsNullOrEmpty(presetId))
				return;

			_currentPresetId = presetId;
			SaveSettings();

			if (applyImmediately)
			{
				ApplyCurrentPreset();
			}
		}

		private WindowPreset GetDefaultPreset()
		{
			return _presets[0];
		}

		private int FindPresetIndex(string presetId)
		{
			for (int i = 0; i < _presets.Length; i++)
			{
				if (_presets[i].Id == presetId)
				{
					return i;
				}
			}
			return -1;
		}

		public int GetPresetIndex(string presetId)
		{
			var index = FindPresetIndex(presetId);
			return index >= 0 ? index : 0;
		}

		public WindowPreset GetPresetByIndex(int index)
		{
			if (index < 0 || index >= _presets.Length)
			{
				return GetDefaultPreset();
			}
			return _presets[index];
		}

		private WindowPreset GetPresetById(string presetId)
		{
			var index = FindPresetIndex(presetId);
			return index >= 0 ? _presets[index] : GetDefaultPreset();
		}

		private void LoadSettings()
		{
			var config = new ConfigFile();
			var result = config.Load(ConfigPath);

			if (result == Error.Ok)
			{
				_currentPresetId = (string)config.GetValue(WindowSection, PresetKey, _currentPresetId);
			}
			else
			{
				GD.PushWarning($"GameSettingsManager: 无法加载配置文件 ({ConfigPath})，使用默认窗口模式。错误: {result}");
				SaveSettings();
			}
		}

		private void SaveSettings()
		{
			var config = new ConfigFile();
			config.SetValue(WindowSection, PresetKey, _currentPresetId);

			var err = config.Save(ConfigPath);
			if (err != Error.Ok)
			{
				GD.PushWarning($"GameSettingsManager: 保存配置失败 ({err})，路径: {ConfigPath}");
			}
		}

		private void CenterWindow()
		{
			var screenSize = DisplayServer.ScreenGetSize();
			var windowSize = DisplayServer.WindowGetSize();
			DisplayServer.WindowSetPosition((screenSize - windowSize) / 2);
		}

		private void ApplyProjectSettings(WindowPreset preset)
		{
			ProjectSettings.SetSetting("display/window/size/mode", preset.Mode == DisplayServer.WindowMode.Fullscreen ? 2 : 0);
			ProjectSettings.SetSetting("display/window/size/viewport_width", preset.Size.X);
			ProjectSettings.SetSetting("display/window/size/viewport_height", preset.Size.Y);
			ProjectSettings.SetSetting("display/window/size/initial_position_type", 2);
			ProjectSettings.SetSetting("display/window/size/resizable", true);
		}

		public readonly record struct WindowPreset(string Id, string DisplayName, DisplayServer.WindowMode Mode, Vector2I Size);
	}
}

