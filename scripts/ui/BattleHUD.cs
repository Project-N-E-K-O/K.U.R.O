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

		private GameActor? _player;
		private IPlayerStatsSource? _playerStatsSource;

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

		private void ApplyStatsSnapshot(int health, int maxHealth, int score)
		{
			_currentHealth = health;
			_maxHealth = maxHealth;
			_score = score;
			UpdateDisplay();
		}

		public void SetFallbackStats(int health = 100, int maxHealth = 100, int score = 0)
		{
			ApplyStatsSnapshot(health, maxHealth, score);
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
		/// 连接到任意 GameActor（可选实现 IPlayerStatsSource）
		/// </summary>
		public void AttachActor(GameActor actor)
		{
			if (actor == null) return;
			if (_player == actor) return;

			DetachCurrentActor();

			_player = actor;
			_player.HealthChanged += OnActorHealthChanged;

			if (actor is IPlayerStatsSource statsSource)
			{
				_playerStatsSource = statsSource;
				_playerStatsSource.StatsUpdated += OnStatsSourceUpdated;
				ApplyStatsSnapshot(_playerStatsSource.CurrentHealth, _playerStatsSource.MaxHealth, _playerStatsSource.Score);
				return;
			}

			ApplyStatsSnapshot(actor.CurrentHealth, actor.MaxHealth, _score);
		}

		public void DetachActor(GameActor actor)
		{
			if (actor == null || _player != actor)
			{
				return;
			}

			DetachCurrentActor();
		}

		private void DetachCurrentActor()
		{
			if (_player != null)
			{
				_player.HealthChanged -= OnActorHealthChanged;
			}

			if (_playerStatsSource != null)
			{
				_playerStatsSource.StatsUpdated -= OnStatsSourceUpdated;
			}

			_player = null;
			_playerStatsSource = null;
		}

		private void OnActorHealthChanged(int health, int maxHealth)
		{
			int score = _playerStatsSource?.Score ?? _score;
			ApplyStatsSnapshot(health, maxHealth, score);
		}

		private void OnStatsSourceUpdated(int health, int maxHealth, int score)
		{
			ApplyStatsSnapshot(health, maxHealth, score);
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
