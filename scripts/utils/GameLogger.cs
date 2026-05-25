using Godot;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace Kuros.Utils
{
	/// <summary>
	/// 全局日志工具：在导出版本中把日志写到可执行文件同级目录，开关受 ProjectSettings 控制。
	/// 日志行通过后台线程异步写入，避免主线程因磁盘 I/O 产生卡顿。
	/// </summary>
	public static class GameLogger
	{
		private const string SettingKey = "kuro/logging/enable_file_logging";
		private static readonly ConcurrentQueue<string> _logQueue = new();
		private static Thread? _writerThread;
		private static volatile bool _writerRunning = false;
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
				else
				{
					// 停止后台写入线程
					StopWriterThread();
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

		/// <summary>
		/// 刷新队列中剩余日志并停止后台写入线程（退出时调用）。
		/// </summary>
		public static void Flush()
		{
			StopWriterThread();
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

			// 入队，由后台线程异步写入，不阻塞主线程
			_logQueue.Enqueue(line + System.Environment.NewLine);
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

				// 启动后台写入线程
				StartWriterThread();
			}
			catch (Exception ex)
			{
				_initialized = false;
				_logFilePath = null;
				GD.PrintErr($"GameLogger: Failed to initialize log file. {ex.Message}");
			}
		}

		private static void StartWriterThread()
		{
			if (_writerRunning)
				return;

			_writerRunning = true;
			_writerThread = new Thread(WriterLoop)
			{
				IsBackground = true,
				Name = "GameLogger.WriterThread"
			};
			_writerThread.Start();
		}

		private static void StopWriterThread()
		{
			_writerRunning = false;
			_writerThread?.Join(2000); // 最多等2秒让剩余日志写完
			_writerThread = null;
		}

		/// <summary>
		/// 后台写入循环：批量消费队列中的日志行并写入文件。
		/// </summary>
		private static void WriterLoop()
		{
			var buffer = new StringBuilder(4096);
			while (_writerRunning || !_logQueue.IsEmpty)
			{
				buffer.Clear();
				// 批量取出队列中所有待写行
				while (_logQueue.TryDequeue(out string? line))
				{
					buffer.Append(line);
				}

				if (buffer.Length > 0 && !string.IsNullOrEmpty(_logFilePath))
				{
					try
					{
						File.AppendAllText(_logFilePath, buffer.ToString(), Encoding.UTF8);
					}
					catch
					{
						// 忽略写入失败，不影响游戏运行
					}
				}

				if (_writerRunning)
				{
					Thread.Sleep(100); // 每100ms批量刷盘一次
				}
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
