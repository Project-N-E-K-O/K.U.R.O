using Godot;
using Kuros.Managers;

namespace Kuros.UI
{
	/// <summary>
	/// 关卡名称弹窗 - 在进入关卡时显示关卡名称，持续3秒后自动隐藏
	/// </summary>
	public partial class LevelNamePopup : Control
	{
		[ExportCategory("UI References")]
		[Export] public Label LevelNameLabel { get; private set; } = null!;
		[Export] public Panel BackgroundPanel { get; private set; } = null!;

		[ExportCategory("Settings")]
		[Export] public float DisplayDuration { get; set; } = 3.0f; // 显示持续时间（秒）
		[Export] public float FadeInDuration { get; set; } = 0.3f; // 淡入时间（秒）
		[Export] public float FadeOutDuration { get; set; } = 0.3f; // 淡出时间（秒）

		private Tween? _fadeTween;
		private bool _isShowing = false;
		private SceneTreeTimer? _displayTimer;

		public override void _Ready()
		{
			base._Ready();

			// 自动查找节点引用
			CacheNodeReferences();

			// 初始化UI
			InitializeUI();

			// 默认隐藏
			Visible = false;
			Modulate = new Color(1, 1, 1, 0); // 初始透明度为0
		}

		private void CacheNodeReferences()
		{
			LevelNameLabel ??= GetNodeOrNull<Label>("BackgroundPanel/LevelNameLabel");
			BackgroundPanel ??= GetNodeOrNull<Panel>("BackgroundPanel");
		}

		private void InitializeUI()
		{
			// 设置处理模式
			ProcessMode = ProcessModeEnum.Always;
		}

		/// <summary>
		/// 显示关卡名称弹窗
		/// </summary>
		/// <param name="levelName">关卡名称</param>
		public void ShowLevelName(string levelName)
		{
			if (string.IsNullOrEmpty(levelName))
			{
				GD.PrintErr("LevelNamePopup: 关卡名称为空！");
				return;
			}

			// 如果正在显示，先停止之前的动画
			if (_isShowing)
			{
				StopTween();
			}

			// 设置关卡名称
			if (LevelNameLabel != null)
			{
				LevelNameLabel.Text = levelName;
			}

			// 显示并开始动画
			Visible = true;
			_isShowing = true;

			// 淡入动画
			FadeIn();
		}

		/// <summary>
		/// 淡入动画
		/// </summary>
		private void FadeIn()
		{
			StopTween();

			Modulate = new Color(1, 1, 1, 0); // 从透明开始

			_fadeTween = CreateTween();
			_fadeTween.TweenProperty(this, "modulate:a", 1.0f, FadeInDuration);
			_fadeTween.TweenCallback(Callable.From(() =>
			{
				// 淡入完成后，等待显示时间，然后淡出
				// 先断开之前的定时器（如果存在）
				if (_displayTimer != null && IsInstanceValid(_displayTimer))
				{
					_displayTimer.Timeout -= FadeOut;
				}
				
				_displayTimer = GetTree().CreateTimer(DisplayDuration);
				_displayTimer.Timeout += FadeOut;
			}));
		}

		/// <summary>
		/// 淡出动画
		/// </summary>
		private void FadeOut()
		{
			if (!_isShowing || !IsInstanceValid(this))
			{
				return;
			}

			StopTween();

			_fadeTween = CreateTween();
			_fadeTween.TweenProperty(this, "modulate:a", 0.0f, FadeOutDuration);
			_fadeTween.TweenCallback(Callable.From(() =>
			{
				// 淡出完成后隐藏
				Visible = false;
				_isShowing = false;
			}));
		}

		/// <summary>
		/// 停止当前的Tween动画
		/// </summary>
		private void StopTween()
		{
			if (_fadeTween != null && IsInstanceValid(_fadeTween))
			{
				_fadeTween.Kill();
				_fadeTween = null;
			}
			
			// 断开并清除显示定时器
			if (_displayTimer != null && IsInstanceValid(_displayTimer))
			{
				_displayTimer.Timeout -= FadeOut;
				_displayTimer = null;
			}
		}

		/// <summary>
		/// 立即隐藏弹窗
		/// </summary>
		public void HideImmediately()
		{
			StopTween();
			Visible = false;
			_isShowing = false;
			Modulate = new Color(1, 1, 1, 0);
		}

		public override void _ExitTree()
		{
			StopTween();
			base._ExitTree();
		}
	}
}

