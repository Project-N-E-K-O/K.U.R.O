using Godot;
using Kuros.Core;
using Kuros.Systems.Inventory;
using Kuros.Items;
using Kuros.Managers;
using Kuros.Actors.Heroes;

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
	[Export] public Button PauseButton { get; private set; } = null!;
	[Export] public Label GoldLabel { get; private set; } = null!;
	[Export] public Control MinimapContainer { get; private set; } = null!;
	[Export] public ColorRect PlayerMarker { get; private set; } = null!;

		[ExportCategory("Default Items")]
		[Export] public ItemDefinition? DefaultSwordItem { get; set; } // 默认小木剑物品定义
		private const string DefaultSwordItemPath = "res://data/DefaultSwordItem.tres";

		// 当前显示的数据
		private int _currentHealth = 100;
		private int _maxHealth = 100;
		private int _score = 0;

		// 物品栏相关
		private InventoryWindow? _inventoryWindow;
		private InventoryContainer? _inventoryContainer;
		private InventoryContainer? _quickBarContainer;
		
		// 快捷栏UI引用
		private readonly Label[] _quickSlotLabels = new Label[5];
		private readonly Panel[] _quickSlotPanels = new Panel[5];
		private readonly TextureRect[] _quickSlotIcons = new TextureRect[5];
		
		// 小地图相关
		private Vector2 _mapSize = new Vector2(2000, 1500); // 地图总大小（可以根据实际地图调整）
		private Vector2 _minimapSize = new Vector2(200, 200); // 小地图显示大小
		
		// 颜色定义
		private static readonly Color LeftHandColor = new Color(0.2f, 0.5f, 1.0f, 1.0f); // 蓝色
		private static readonly Color RightHandColor = new Color(1.0f, 0.8f, 0.0f, 1.0f); // 黄色
		private static readonly Color DefaultColor = new Color(0.3f, 0.3f, 0.3f, 1.0f); // 默认灰色

		// 信号：用于通知外部系统
		[Signal] public delegate void HUDReadyEventHandler();
		[Signal] public delegate void BattleMenuRequestedEventHandler();

		public override void _Ready()
		{
			// 添加到 "ui" 组，方便其他脚本查找
			AddToGroup("ui");
			
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

			if (PauseButton == null)
			{
				PauseButton = GetNodeOrNull<Button>("PauseButton");
			}

			if (GoldLabel == null)
			{
				GoldLabel = GetNodeOrNull<Label>("GoldLabel");
			}

			// 初始化小地图引用
			if (MinimapContainer == null)
			{
				MinimapContainer = GetNodeOrNull<Control>("MinimapPanel/MinimapContainer");
			}

			if (PlayerMarker == null)
			{
				PlayerMarker = GetNodeOrNull<ColorRect>("MinimapPanel/MinimapContainer/PlayerMarker");
			}

			// 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
			if (PauseButton != null)
			{
				var callable = new Callable(this, nameof(OnPauseButtonPressed));
				if (!PauseButton.IsConnected(Button.SignalName.Pressed, callable))
				{
					PauseButton.Connect(Button.SignalName.Pressed, callable);
				}
			}

			// 缓存快捷栏Label引用（必须在初始化物品栏之前）
			CacheQuickBarLabels();

			// 初始化物品栏
			InitializeInventory();

			// 初始化UI显示
			UpdateDisplay();

			// 延迟更新快捷栏显示，确保所有节点都已准备好
			CallDeferred(MethodName.UpdateQuickBarDisplay);

			// 尝试自动连接玩家（如果场景中已有玩家）
			CallDeferred(MethodName.TryAutoConnectPlayer);

			// 发出就绪信号
			EmitSignal(SignalName.HUDReady);
		}

		private void TryAutoConnectPlayer()
		{
			// 尝试在场景树中查找玩家
			var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
			if (player != null)
			{
				ConnectPlayerInventory(player);
			}
		}

		private void CacheQuickBarLabels()
		{
			for (int i = 0; i < 5; i++)
			{
				_quickSlotLabels[i] = GetNodeOrNull<Label>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}/QuickSlotLabel{i + 1}");
				_quickSlotPanels[i] = GetNodeOrNull<Panel>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}");
				_quickSlotIcons[i] = GetNodeOrNull<TextureRect>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}/QuickSlotIcon{i + 1}");
				
				if (_quickSlotLabels[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotLabel{i + 1}");
				}
				
				if (_quickSlotPanels[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotPanel{i + 1}");
				}
				else
				{
					// 初始化默认边框颜色
					UpdateSlotBorderColor(i, DefaultColor);
				}
				
				if (_quickSlotIcons[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotIcon{i + 1}");
				}
			}
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

		// 连接快捷栏变化信号
		// 注意：_quickBarContainer 在此方法中刚刚创建，且 _Ready() 只调用一次，因此无需检查重复订阅
		_quickBarContainer.SlotChanged += OnQuickBarSlotChanged;
		_quickBarContainer.InventoryChanged += OnQuickBarChanged;

			// 在快捷栏1（索引0）放置默认小木剑占位符
			ItemDefinition? swordItem = DefaultSwordItem;
			
			// 如果未设置，尝试加载默认资源
			if (swordItem == null)
			{
				swordItem = GD.Load<ItemDefinition>(DefaultSwordItemPath);
			}
			
			if (swordItem != null)
			{
				_quickBarContainer.TryAddItemToSlot(swordItem, 1, 0);
				
				// 立即更新快捷栏1的显示
				CallDeferred(MethodName.UpdateQuickBarSlot, 0);
			}
			else
			{
				GD.PrintErr("InitializeInventory: DefaultSwordItem is null and could not load from resource file. Please set DefaultSwordItem in the inspector or create the resource file.");
			}
			
			// 初始化空白道具：填充快捷栏和物品栏的空槽位
			CallDeferred(MethodName.InitializeEmptyItems);

			// 通过UIManager加载物品栏窗口（放在GameUI层，在HUD之上）
			LoadInventoryWindow();
		}

		/// <summary>
		/// 加载物品栏窗口
		/// </summary>
		private void LoadInventoryWindow()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("BattleHUD: UIManager未初始化！");
				return;
			}

		_inventoryWindow = UIManager.Instance.LoadInventoryWindow();
		
		if (_inventoryWindow != null && _inventoryContainer != null && _quickBarContainer != null)
		{
			_inventoryWindow.SetInventoryContainer(_inventoryContainer, _quickBarContainer);
			_inventoryWindow.HideWindow();
		}
		else if (_inventoryWindow != null)
		{
			GD.PrintErr("BattleHUD: 无法设置物品栏容器，_inventoryContainer 或 _quickBarContainer 为 null");
		}
		}

		private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
		{
			// 使用 CallDeferred 确保在下一帧更新，避免在信号处理过程中更新UI
			CallDeferred(MethodName.UpdateQuickBarSlot, slotIndex);
		}

		private void OnQuickBarChanged()
		{
			UpdateQuickBarDisplay();
		}

		public void UpdateQuickBarDisplay()
		{
			if (_quickBarContainer == null)
			{
				GD.PrintErr("UpdateQuickBarDisplay: QuickBarContainer is null");
				return;
			}

			for (int i = 0; i < 5; i++)
			{
				UpdateQuickBarSlot(i);
			}
		}

		private void UpdateQuickBarSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= 5) return;
			if (_quickBarContainer == null)
			{
				GD.PrintErr($"UpdateQuickBarSlot: QuickBarContainer is null for slot {slotIndex}");
				return;
			}

			var stack = _quickBarContainer.GetStack(slotIndex);
			bool isEmpty = stack == null || stack.IsEmpty;
			bool isEmptyItem = !isEmpty && stack!.Item.ItemId == "empty_item";
			
			// 更新标签文字
			if (_quickSlotLabels[slotIndex] != null)
			{
				if (isEmpty || isEmptyItem)
				{
					_quickSlotLabels[slotIndex].Text = "";
				}
				else
				{
					_quickSlotLabels[slotIndex].Text = stack!.Item.DisplayName;
				}
			}
			
			// 更新图标
			if (_quickSlotIcons[slotIndex] != null)
			{
				if (isEmpty || isEmptyItem)
				{
					_quickSlotIcons[slotIndex].Texture = null;
					_quickSlotIcons[slotIndex].Modulate = new Color(1, 1, 1, 0.3f);
				}
				else
				{
					_quickSlotIcons[slotIndex].Texture = stack!.Item.Icon;
					_quickSlotIcons[slotIndex].Modulate = Colors.White;
				}
			}
		}
		
		/// <summary>
		/// 更新快捷栏槽位的边框颜色
		/// </summary>
		private void UpdateSlotBorderColor(int slotIndex, Color color)
		{
			if (slotIndex < 0 || slotIndex >= 5) return;
			if (_quickSlotPanels[slotIndex] == null) return;
			
			// 使用 StyleBoxFlat 来设置边框颜色
			var styleBox = new StyleBoxFlat();
			styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.5f); // 背景色
			styleBox.BorderColor = color;
			styleBox.BorderWidthLeft = 3;
			styleBox.BorderWidthTop = 3;
			styleBox.BorderWidthRight = 3;
			styleBox.BorderWidthBottom = 3;
			styleBox.CornerRadiusTopLeft = 4;
			styleBox.CornerRadiusTopRight = 4;
			styleBox.CornerRadiusBottomLeft = 4;
			styleBox.CornerRadiusBottomRight = 4;
			
			_quickSlotPanels[slotIndex].AddThemeStyleboxOverride("panel", styleBox);
		}
		
		/// <summary>
		/// 更新左右手选择的快捷栏边框颜色
		/// </summary>
		/// <param name="leftHandSlotIndex">左手选择的槽位索引（1-4，-1表示未选择）</param>
		/// <param name="rightHandSlotIndex">右手选择的槽位索引（0，固定为小木剑）</param>
		public void UpdateHandSlotHighlight(int leftHandSlotIndex, int rightHandSlotIndex = 0)
		{
			// 重置所有槽位为默认颜色
			for (int i = 0; i < 5; i++)
			{
				UpdateSlotBorderColor(i, DefaultColor);
			}
			
			// 设置右手颜色（槽位0，小木剑）
			if (rightHandSlotIndex >= 0 && rightHandSlotIndex < 5)
			{
				UpdateSlotBorderColor(rightHandSlotIndex, RightHandColor);
			}
			
			// 设置左手颜色（槽位1-4），无论槽位是否有物品都显示蓝色
			if (leftHandSlotIndex >= 1 && leftHandSlotIndex < 5)
			{
				UpdateSlotBorderColor(leftHandSlotIndex, LeftHandColor);
			}
		}

		/// <summary>
		/// 初始化空白道具：填充快捷栏和物品栏的空槽位
		/// </summary>
		private void InitializeEmptyItems()
		{
			var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
			if (emptyItem == null)
			{
				GD.PrintErr("BattleHUD.InitializeEmptyItems: Failed to load EmptyItem.tres");
				return;
			}
			
			// 填充快捷栏空槽位（跳过索引0，因为是小木剑）
			if (_quickBarContainer != null)
			{
				for (int i = 1; i < 5; i++)
				{
					var stack = _quickBarContainer.GetStack(i);
					if (stack == null || stack.IsEmpty)
					{
						_quickBarContainer.TryAddItemToSlot(emptyItem, 1, i);
					}
				}
			}
			
			// 填充物品栏空槽位
			if (_inventoryContainer != null)
			{
				for (int i = 0; i < _inventoryContainer.SlotCount; i++)
				{
					var stack = _inventoryContainer.GetStack(i);
					if (stack == null || stack.IsEmpty)
					{
						_inventoryContainer.TryAddItemToSlot(emptyItem, 1, i);
					}
				}
			}
		}
		
		/// <summary>
		/// 连接玩家物品栏组件，设置快捷栏引用
		/// </summary>
		public void ConnectPlayerInventory(SamplePlayer player)
		{
			if (player == null)
			{
				GD.PrintErr($"BattleHUD.ConnectPlayerInventory: Player 为 null");
				return;
			}

			// 如果 InventoryComponent 为 null，尝试延迟查找
			// 注意：InventoryComponent 的 setter 是 private，不能直接设置
			// 但 SamplePlayer._Ready() 应该已经初始化了它
			if (player.InventoryComponent == null)
			{
				GD.PushWarning($"BattleHUD.ConnectPlayerInventory: Player.InventoryComponent 为 null，尝试延迟查找...");
				
				// 尝试通过节点查找（但无法直接设置，因为 setter 是 private）
				var foundInventory = player.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
				
				if (foundInventory == null)
				{
					GD.PrintErr($"BattleHUD.ConnectPlayerInventory: Failed - Player={player != null}, InventoryComponent={player?.InventoryComponent != null}, QuickBarContainer={_quickBarContainer != null}");
					GD.PrintErr($"BattleHUD.ConnectPlayerInventory: Player 类型: {player.GetType().Name}, 子节点数量: {player.GetChildCount()}");
					// 打印所有子节点名称以便调试
					foreach (Node child in player.GetChildren())
					{
						GD.PrintErr($"  - 子节点: {child.Name}, 类型: {child.GetType().Name}");
					}
					GD.PrintErr($"BattleHUD.ConnectPlayerInventory: 注意：InventoryComponent 的 setter 是 private，无法从外部设置。请确保 SamplePlayer._Ready() 已正确初始化。");
					return;
				}
				else
				{
					GD.PushWarning($"BattleHUD.ConnectPlayerInventory: 找到了 Inventory 节点，但无法设置 InventoryComponent（setter 是 private）。请检查 SamplePlayer._Ready() 是否正确初始化。");
					return;
				}
			}

			if (_quickBarContainer == null)
			{
				GD.PrintErr($"BattleHUD.ConnectPlayerInventory: QuickBarContainer 为 null");
				return;
			}

			player.InventoryComponent.SetQuickBar(_quickBarContainer);
			
			// 确保玩家连接快捷栏信号，以便左手物品与选中槽位严格对应
			player.ConnectQuickBarSignals();
			
			// 初始化左手选择（只在还没有选中时才初始化，避免覆盖用户选择）
			// 注意：使用 CallDeferred 确保在快捷栏连接完成后再初始化
			player.CallDeferred(SamplePlayer.MethodName.InitializeLeftHandSelection);
			
			// 延迟更新高亮，确保初始化完成后再显示
			int clampedIndex = Mathf.Clamp(player.LeftHandSlotIndex, 1, 4);
			CallDeferred(MethodName.UpdateHandSlotHighlight, clampedIndex, 0);
			
			GD.Print($"BattleHUD.ConnectPlayerInventory: 成功连接 - Player={player.Name}, InventoryComponent={player.InventoryComponent.Name}");
		}

		/// <summary>
		/// 供外部或UI控件调用以请求打开战斗菜单
		/// </summary>
		public void RequestBattleMenu()
		{
			EmitSignal(SignalName.BattleMenuRequested);
		}

		/// <summary>
		/// 暂停按钮点击处理
		/// </summary>
		private void OnPauseButtonPressed()
		{
			RequestBattleMenu();
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
		/// 连接到任意 GameActor（可选实现 IPlayerStatsSource）
		/// </summary>
		public void AttachActor(GameActor actor)
		{
			if (actor is SamplePlayer samplePlayer)
			{
				_player = samplePlayer;
				
				// 连接玩家状态变化信号
				if (!samplePlayer.IsConnected(SamplePlayer.SignalName.StatsChanged, new Callable(this, MethodName.OnPlayerStatsChanged)))
				{
					samplePlayer.StatsChanged += OnPlayerStatsChanged;
				}
				
				// 连接玩家金币变化信号
				if (!samplePlayer.IsConnected(SamplePlayer.SignalName.GoldChanged, new Callable(this, MethodName.OnPlayerGoldChanged)))
				{
					samplePlayer.GoldChanged += OnPlayerGoldChanged;
				}
				
				// 初始化金币显示
				UpdateGoldDisplay(samplePlayer.GetGold());
				
				// 连接玩家物品栏组件
				ConnectPlayerInventory(samplePlayer);
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
				
				if (samplePlayer.IsConnected(SamplePlayer.SignalName.GoldChanged, new Callable(this, MethodName.OnPlayerGoldChanged)))
				{
					samplePlayer.GoldChanged -= OnPlayerGoldChanged;
				}
			}
		}

		/// <summary>
		/// 断开与角色的连接（别名方法，用于兼容性）
		/// </summary>
		public void DetachActor(GameActor actor)
		{
			DisconnectFromPlayer(actor);
		}

		/// <summary>
		/// 设置回退状态（当没有连接玩家时使用）
		/// </summary>
		public void SetFallbackStats()
		{
			UpdateStats(100, 100, 0);
		}

		private SamplePlayer? _player;
		private Vector2 _lastPlayerPosition = Vector2.Zero; // 上次的玩家位置，用于小地图更新优化
		
		/// <summary>
		/// 设置玩家引用（用于获取最大生命值等属性）
		/// </summary>
		public void SetPlayer(SamplePlayer playerRef)
		{
			_player = playerRef;
			if (_player != null)
			{
				int maxHealth = _player.MaxHealth;
				int score = _player is IPlayerStatsSource statsSource ? statsSource.Score : _score;
				UpdateStats(_player.CurrentHealth, maxHealth, score);
				// 重置位置缓存，强制下次更新
				_lastPlayerPosition = Vector2.Zero;
			}
		}

		/// <summary>
		/// 处理玩家状态变化信号
		/// </summary>
		private void OnPlayerStatsChanged(int health, int score)
		{
			// 从玩家获取最大生命值
			int maxHealth = _player?.MaxHealth ?? 100;
			UpdateStats(health, maxHealth, score);
		}
		
		private void OnPlayerGoldChanged(int gold)
		{
			UpdateGoldDisplay(gold);
		}
		
		private void UpdateGoldDisplay(int gold)
		{
			if (GoldLabel != null)
			{
				GoldLabel.Text = $"金币: {gold}";
			}
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

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateMinimap();
		}

		/// <summary>
		/// 更新小地图上的玩家位置
		/// </summary>
		private void UpdateMinimap()
		{
			if (MinimapContainer == null || PlayerMarker == null)
			{
				return;
			}

			// 获取玩家位置
			Vector2 playerPosition = Vector2.Zero;
			Node2D? playerNode = null;
			
			// 如果缓存的玩家引用有效，使用它
			if (_player != null && GodotObject.IsInstanceValid(_player))
			{
				playerNode = _player;
				playerPosition = _player.GlobalPosition;
			}
			else
			{
				// 如果玩家不存在，尝试从场景中查找并缓存
				playerNode = GetTree().GetFirstNodeInGroup("player") as Node2D;
				if (playerNode != null)
				{
					playerPosition = playerNode.GlobalPosition;
					// 缓存玩家引用（如果是 SamplePlayer）
					if (playerNode is SamplePlayer samplePlayer)
					{
						_player = samplePlayer;
					}
				}
				else
				{
					// 没有找到玩家，提前返回
					return;
				}
			}

			// 位置变化检测：如果位置没有改变，提前返回
			if (playerPosition == _lastPlayerPosition)
			{
				return;
			}
			
			// 更新缓存的位置
			_lastPlayerPosition = playerPosition;

			// 将玩家世界坐标转换为小地图坐标
			// 假设地图范围从 (0, 0) 到 _mapSize
			float normalizedX = Mathf.Clamp(playerPosition.X / _mapSize.X, 0.0f, 1.0f);
			float normalizedY = Mathf.Clamp(playerPosition.Y / _mapSize.Y, 0.0f, 1.0f);

			// 获取小地图容器的实际大小
			var containerSize = MinimapContainer.Size;
			if (containerSize.X <= 0 || containerSize.Y <= 0)
			{
				return;
			}

			// 计算玩家标记在小地图中的位置
			float markerX = normalizedX * containerSize.X;
			float markerY = normalizedY * containerSize.Y;

			// 更新玩家标记位置（相对于小地图容器）
			PlayerMarker.Position = new Vector2(markerX - PlayerMarker.Size.X / 2, markerY - PlayerMarker.Size.Y / 2);
		}
	}
}
