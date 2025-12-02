using Godot;

namespace Kuros.UI
{
	/// <summary>
	/// 加载屏幕 - 显示关卡加载进度
	/// 进度条会匀速增加到89%，然后等待加载完成后瞬间跳到100%
	/// </summary>
	public partial class LoadingScreen : Control
	{
		[ExportCategory("UI References")]
		[Export] public ProgressBar ProgressBar { get; private set; } = null!;
		[Export] public Label PercentageLabel { get; private set; } = null!;
		
		// 进度条动画参数
		private const float TARGET_PROGRESS = 89.0f; // 目标进度（89%）
		private const float ANIMATION_DURATION = 2.0f; // 动画持续时间（秒）
		
		// 当前进度
		private float _currentProgress = 0.0f;
		private bool _isAnimating = false;
		private bool _isLoadingComplete = false;
		private double _animationTimer = 0.0;
		
		// 信号
		[Signal] public delegate void LoadingCompleteEventHandler();
		
		public override void _Ready()
		{
			base._Ready();
			
			// 自动查找节点引用
			CacheNodeReferences();
			
			// 初始状态：隐藏
			Visible = false;
			ProcessMode = ProcessModeEnum.Always; // 确保在暂停时也能更新
		}
		
		private void CacheNodeReferences()
		{
			ProgressBar ??= GetNodeOrNull<ProgressBar>("ProgressContainer/ProgressBar");
			PercentageLabel ??= GetNodeOrNull<Label>("ProgressContainer/PercentageLabel");
			
			if (ProgressBar == null)
			{
				GD.PrintErr("LoadingScreen: 无法找到ProgressBar节点！");
			}
			
			if (PercentageLabel == null)
			{
				GD.PrintErr("LoadingScreen: 无法找到PercentageLabel节点！");
			}
		}
		
		/// <summary>
		/// 显示加载屏幕并开始动画
		/// </summary>
		public void ShowLoading()
		{
			Visible = true;
			_currentProgress = 0.0f;
			_isAnimating = true;
			_isLoadingComplete = false;
			_animationTimer = 0.0;
			
			UpdateProgressDisplay(0.0f);
		}
		
		/// <summary>
		/// 标记加载完成，进度条会瞬间跳到100%
		/// </summary>
		public void SetLoadingComplete()
		{
			if (_isLoadingComplete) return;
			
			_isLoadingComplete = true;
			_isAnimating = false;
			_currentProgress = 100.0f;
			UpdateProgressDisplay(100.0f);
			
			// 延迟发送完成信号，让用户看到100%
			CallDeferred(MethodName.EmitLoadingComplete);
		}
		
		private void EmitLoadingComplete()
		{
			EmitSignal(SignalName.LoadingComplete);
		}
		
		/// <summary>
		/// 隐藏加载屏幕
		/// </summary>
		public void HideLoading()
		{
			Visible = false;
			_isAnimating = false;
			_isLoadingComplete = false;
			_currentProgress = 0.0f;
			_animationTimer = 0.0;
		}
		
		public override void _Process(double delta)
		{
			base._Process(delta);
			
			// 如果正在动画且未完成加载，更新进度
			if (_isAnimating && !_isLoadingComplete)
			{
				_animationTimer += delta;
				
				// 计算进度（使用线性插值）
				float progress = (float)(_animationTimer / ANIMATION_DURATION);
				
				// 限制在0到TARGET_PROGRESS之间
				if (progress >= 1.0f)
				{
					progress = 1.0f;
					_currentProgress = TARGET_PROGRESS;
					_isAnimating = false; // 动画完成，等待加载完成
				}
				else
				{
					_currentProgress = progress * TARGET_PROGRESS;
				}
				
				UpdateProgressDisplay(_currentProgress);
			}
		}
		
		/// <summary>
		/// 更新进度显示
		/// </summary>
		private void UpdateProgressDisplay(float progress)
		{
			if (ProgressBar != null)
			{
				ProgressBar.Value = progress;
			}
			
			if (PercentageLabel != null)
			{
				// 显示整数百分比
				PercentageLabel.Text = $"{(int)progress}%";
			}
		}
		
		/// <summary>
		/// 获取当前进度
		/// </summary>
		public float GetCurrentProgress()
		{
			return _currentProgress;
		}
		
		/// <summary>
		/// 检查是否加载完成
		/// </summary>
		public bool IsLoadingComplete()
		{
			return _isLoadingComplete;
		}
	}
}

