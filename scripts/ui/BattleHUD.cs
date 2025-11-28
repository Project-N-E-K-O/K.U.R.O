using Godot;
using Kuros.Core;
using Kuros.Systems.Inventory;

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

		// 物品栏相关
		private InventoryWindow? _inventoryWindow;
		private InventoryContainer? _inventoryContainer;
		private InventoryContainer? _quickBarContainer;
		private const string InventoryScenePath = "res://scenes/ui/windows/InventoryWindow.tscn";
		private PackedScene? _inventoryScene;

		// 信号：用于通知外部系统
		[Signal] public delegate void HUDReadyEventHandler();
		[Signal] public delegate void BattleMenuRequestedEventHandler();

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

			// 初始化物品栏
			InitializeInventory();

			// 初始化UI显示
			UpdateDisplay();

			// 发出就绪信号
			EmitSignal(SignalName.HUDReady);
		}

		private void InitializeInventory()
		{
			// 创建物品栏容器
			_inventoryContainer = new InventoryContainer
			{
				Name = "PlayerInventory",
				SlotCount = 16
			};
			AddChild(_inventoryContainer);

			// 创建快捷栏容器
			_quickBarContainer = new InventoryContainer
			{
				Name = "QuickBar",
				SlotCount = 5
			};
			AddChild(_quickBarContainer);

			// 加载物品栏窗口场景
			_inventoryScene = GD.Load<PackedScene>(InventoryScenePath);
			if (_inventoryScene != null)
			{
				_inventoryWindow = _inventoryScene.Instantiate<InventoryWindow>();
				AddChild(_inventoryWindow);
				_inventoryWindow.SetInventoryContainer(_inventoryContainer, _quickBarContainer);
				_inventoryWindow.HideWindow();
			}
		}

		/// <summary>
		/// 供外部或UI控件调用以请求打开战斗菜单
		/// </summary>
		public void RequestBattleMenu()
		{
			EmitSignal(SignalName.BattleMenuRequested);
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

		public override void _UnhandledInput(InputEvent @event)
		{
			if (@event.IsActionPressed("open_inventory"))
			{
				if (_inventoryWindow != null)
				{
					if (_inventoryWindow.Visible)
					{
						_inventoryWindow.HideWindow();
					}
					else
					{
						_inventoryWindow.ShowWindow();
					}
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}
}
