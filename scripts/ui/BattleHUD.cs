using Godot;
using Kuros.Core;

namespace Kuros.UI
{
	/// <summary>
	/// 战斗HUD - 显示玩家状态、分数等信息
	/// 通过信号系统与游戏逻辑解耦
	/// </summary>
	public partial class BattleHUD : Control
	{
		[ExportCategory("UI References")]
		[Export] public Label PlayerStatsLabel { get; private set; } = null!;
		[Export] public Label InstructionsLabel { get; private set; } = null!;
		[Export] public ProgressBar HealthBar { get; private set; } = null!;
		[Export] public Label ScoreLabel { get; private set; } = null!;

		// 当前显示的数据
		private int _currentHealth = 100;
		private int _maxHealth = 100;
		private int _score = 0;

		// 信号：用于通知外部系统
		[Signal] public delegate void HUDReadyEventHandler();

		public override void _Ready()
		{
			// 如果没有在编辑器中分配，尝试自动查找
			if (PlayerStatsLabel == null)
			{
				PlayerStatsLabel = GetNodeOrNull<Label>("PlayerStats");
			}

			if (InstructionsLabel == null)
			{
				InstructionsLabel = GetNodeOrNull<Label>("Instructions");
			}

			if (HealthBar == null)
			{
				HealthBar = GetNodeOrNull<ProgressBar>("HealthBar");
			}

			if (ScoreLabel == null)
			{
				ScoreLabel = GetNodeOrNull<Label>("ScoreLabel");
			}

			// 初始化UI显示
			UpdateDisplay();

			// 发出就绪信号
			EmitSignal(SignalName.HUDReady);
		}

		/// <summary>
		/// 更新玩家状态
		/// </summary>
		public void UpdateStats(int health, int maxHealth, int score)
		{
			_currentHealth = health;
			_maxHealth = maxHealth;
			_score = score;
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			if (HealthBar != null)
			{
				HealthBar.MaxValue = _maxHealth;
				HealthBar.Value = _currentHealth;
			}
			if (ScoreLabel != null)
			{
				ScoreLabel.Text = $"Score: {_score}";
			}
			if (PlayerStatsLabel != null)
			{
				PlayerStatsLabel.Text = $"Player HP: {_currentHealth}/{_maxHealth}\nScore: {_score}";
			}
		}

		/// <summary>
		/// 连接到玩家信号（在外部调用）
		/// </summary>
		public void ConnectToPlayer(GameActor player)
		{
			if (player is SamplePlayer samplePlayer)
			{
				// 连接玩家状态变化信号
				if (!samplePlayer.IsConnected(SamplePlayer.SignalName.StatsChanged, new Callable(this, MethodName.OnPlayerStatsChanged)))
				{
					samplePlayer.StatsChanged += OnPlayerStatsChanged;
				}
			}
		}

		/// <summary>
		/// 断开与玩家的连接
		/// </summary>
		public void DisconnectFromPlayer(GameActor player)
		{
			if (player is SamplePlayer samplePlayer)
			{
				if (samplePlayer.IsConnected(SamplePlayer.SignalName.StatsChanged, new Callable(this, MethodName.OnPlayerStatsChanged)))
				{
					samplePlayer.StatsChanged -= OnPlayerStatsChanged;
				}
			}
		}

		private SamplePlayer? _player;
		
		/// <summary>
		/// 设置玩家引用（用于获取最大生命值等属性）
		/// </summary>
		public void SetPlayer(SamplePlayer playerRef)
		{
			_player = playerRef;
		}

		private void OnPlayerStatsChanged(int health, int score)
		{
			// 从玩家获取最大生命值
			int maxHealth = _player?.MaxHealth ?? 100;
			UpdateStats(health, maxHealth, score);
		}
	}
}
