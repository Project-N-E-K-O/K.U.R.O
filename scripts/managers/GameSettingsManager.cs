using Godot;
using System;
using System.Linq;

namespace Kuros.Managers
{
	/// <summary>
	/// 游戏设置管理器：负责加载/保存窗口模式配置并在启动时应用
	/// </summary>
	public partial class GameSettingsManager : Node
	{
		public static GameSettingsManager Instance { get; private set; } = null!;

		private const string ConfigDirectory = "user://config";
		private const string ConfigFileName = "window_settings.cfg";
		private static readonly string ConfigPath = $"{ConfigDirectory}/{ConfigFileName}";
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
			LoadSettings();
			ApplyCurrentPreset();
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

		public int GetPresetIndex(string presetId)
		{
			for (int i = 0; i < _presets.Length; i++)
			{
				if (_presets[i].Id == presetId)
				{
					return i;
				}
			}
			return 0;
		}

		public WindowPreset GetPresetByIndex(int index)
		{
			if (index < 0 || index >= _presets.Length)
			{
				return _presets[0];
			}
			return _presets[index];
		}

		private void LoadSettings()
		{
			var config = new ConfigFile();
			EnsureConfigDirectory();
			var result = config.Load(ConfigPath);

			if (result == Error.Ok)
			{
				_currentPresetId = (string)config.GetValue(WindowSection, PresetKey, _currentPresetId);
			}
			else if (result == Error.FileNotFound)
			{
				GD.Print($"GameSettingsManager: 首次运行未找到配置，创建默认文件: {ConfigPath}");
				SaveSettings();
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

			EnsureConfigDirectory();
			var err = config.Save(ConfigPath);
			if (err != Error.Ok)
			{
				GD.PushWarning($"GameSettingsManager: 保存配置失败 ({err})，路径: {ConfigPath}");
			}
		}

		private static void EnsureConfigDirectory()
		{
			var absolutePath = ProjectSettings.GlobalizePath(ConfigDirectory);
			DirAccess.MakeDirRecursiveAbsolute(absolutePath);
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

		private WindowPreset GetPresetById(string presetId)
		{
			var preset = _presets.FirstOrDefault(p => p.Id == presetId);
			return preset.Id == null ? _presets[0] : preset;
		}

		public readonly record struct WindowPreset(string Id, string DisplayName, DisplayServer.WindowMode Mode, Vector2I Size);
	}
}
