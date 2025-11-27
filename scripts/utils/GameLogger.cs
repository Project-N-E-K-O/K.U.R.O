using Godot;
using System;
using System.IO;
using System.Text;

namespace Kuros.Utils
{
	/// <summary>
	/// 全局日志工具：在导出版本中把日志写到可执行文件同级目录，开关受 ProjectSettings 控制。
	/// </summary>
	public static class GameLogger
	{
		private const string SettingKey = "kuro/logging/enable_file_logging";
		private static readonly object FileLock = new();
		private static bool _initialized;
		private static string? _logFilePath;
		private static bool _enabled = ReadInitialEnabledState();

		private enum LogLevel
		{
			Debug,
			Info,
			Warning,
			Error
		}

		private static bool ReadInitialEnabledState()
		{
			if (ProjectSettings.HasSetting(SettingKey))
			{
				return ProjectSettings.GetSetting(SettingKey).AsBool();
			}

			return true;
		}

		public static bool Enabled
		{
			get => _enabled;
			set
			{
				if (_enabled == value)
				{
					return;
				}

				_enabled = value;

				if (_enabled)
				{
					// 重新建立文件。
					_initialized = false;
					_logFilePath = null;
					Initialize();
				}
			}
		}

		public static string? CurrentLogFile => _logFilePath;

		public static void RefreshFromProjectSettings()
		{
			if (ProjectSettings.HasSetting(SettingKey))
			{
				Enabled = ProjectSettings.GetSetting(SettingKey).AsBool();
			}
		}

		public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);
		public static void Info(string category, string message) => Write(LogLevel.Info, category, message);
		public static void Warn(string category, string message) => Write(LogLevel.Warning, category, message);
		public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

		public static void Error(string category, Exception exception, string? message = null)
		{
			var builder = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(message))
			{
				builder.AppendLine(message);
			}
			builder.AppendLine(exception.ToString());
			Write(LogLevel.Error, category, builder.ToString().TrimEnd());
		}

		private static void Write(LogLevel level, string category, string message)
		{
			if (!Enabled)
			{
				return;
			}

			Initialize();

			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			string line = $"[{timestamp}] [{level}] [{category}] {message}";

			switch (level)
			{
				case LogLevel.Error:
					GD.PrintErr(line);
					break;
				case LogLevel.Warning:
					GD.PushWarning(line);
					GD.Print(line);
					break;
				default:
					GD.Print(line);
					break;
			}

			if (string.IsNullOrEmpty(_logFilePath))
			{
				return;
			}

			try
			{
				lock (FileLock)
				{
					File.AppendAllText(_logFilePath, line + System.Environment.NewLine, Encoding.UTF8);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"GameLogger: Failed to write log file. {ex.Message}");
			}
		}

		private static void Initialize()
		{
			if (_initialized || !Enabled)
			{
				return;
			}

			try
			{
				string directory = ResolveLogDirectory();
				Directory.CreateDirectory(directory);
				string fileName = $"kuro_{DateTime.Now:yyyyMMdd_HHmmss}.log";
				_logFilePath = Path.Combine(directory, fileName);
				File.WriteAllText(_logFilePath, $"[{DateTime.Now:O}] Log session started{System.Environment.NewLine}", Encoding.UTF8);
				_initialized = true;
			}
			catch (Exception ex)
			{
				_initialized = false;
				_logFilePath = null;
				GD.PrintErr($"GameLogger: Failed to initialize log file. {ex.Message}");
			}
		}

		private static string ResolveLogDirectory()
		{
			if (OS.HasFeature("standalone"))
			{
				string executablePath = OS.GetExecutablePath();
				if (!string.IsNullOrEmpty(executablePath))
				{
					string? directory = Path.GetDirectoryName(executablePath);
					if (!string.IsNullOrEmpty(directory))
					{
						return directory;
					}
				}
			}

			return ProjectSettings.GlobalizePath("user://logs");
		}
	}
}
