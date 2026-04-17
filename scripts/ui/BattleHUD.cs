using Godot;
using Kuros.Core;
using Kuros.Systems.Inventory;
using Kuros.Items;
using Kuros.Managers;

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
	// 生命值条内部用于遮罩数值的进度条（新节点），不改变 HealthBar 本身的贴图
	[Export] public TextureProgressBar? HealthFillBar { get; private set; } = null!;
	// 叠加在生命条上的数字显示
	[Export] public Label? HealthValueLabel { get; private set; } = null!;
	[Export] public Label ScoreLabel { get; private set; } = null!;
	[Export] public Button PauseButton { get; private set; } = null!;
	[Export] public Label GoldLabel { get; private set; } = null!;

		[ExportCategory("Styles")]
		// 快捷物品栏整体面板样式（例如使用武器栏底图做 StyleBoxTexture）
		[Export] public StyleBox? QuickBarPanelStyle { get; set; }
		// 金币文本样式（可配合金币图标背景）
		[Export] public StyleBox? GoldLabelStyle { get; set; }
		// 暂停键按钮样式（例如使用“暂停键底”资源）
		[Export] public StyleBox? PauseButtonStyle { get; set; }

		[ExportCategory("Default Items")]
		[Export] public ItemDefinition? DefaultSwordItem { get; set; } // 默认小木剑物品定义
		[Export] public bool SpawnDefaultSwordInQuickBar { get; set; } = false;
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
		// 武器栏框贴图样式：未选中 / 选中（用于 UpdateSlotPanelStyle）
		private StyleBoxTexture? _quickSlotStyleNormal;
		private StyleBoxTexture? _quickSlotStyleSelected;
		// 投掷武器冷却遮罩覆盖层（每个快捷槽一个）
		private readonly ThrowCooldownOverlay?[] _quickSlotCooldownOverlays = new ThrowCooldownOverlay?[5];
		private float _throwCooldownUpdateTimer = 0f;
		private const float ThrowCooldownUpdateInterval = 0.05f;
		
		// 小地图相关
		private Vector2 _mapSize = new Vector2(2000, 1500); // 地图总大小（可以根据实际地图调整）
		private Vector2 _minimapSize = new Vector2(200, 200); // 小地图显示大小

		// 颜色定义（双手装备等）
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

			if (HealthFillBar == null)
			{
				HealthFillBar = GetNodeOrNull<TextureProgressBar>("HealthBar/HealthFillBar");
			}

			ConfigureHealthFillBar();

			if (HealthValueLabel == null)
			{
				HealthValueLabel = GetNodeOrNull<Label>("HealthBar/HealthValueLabel");
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
				GoldLabel = GetNodeOrNull<Label>("GoldContainer/GoldLabel");
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

			// 应用可自定义样式（快捷物品栏、金币、暂停键）
			ApplyCustomStyles();

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
				// 槽位样式在 ApplyCustomStyles 中统一用武器栏框贴图设置

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

			if (SpawnDefaultSwordInQuickBar)
			{
				// 可选：在快捷栏1（索引0）放置默认小木剑占位符
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

			// 检测是否为投掷武器预占槽：empty_item 且槽位在预留集合中
			Kuros.Items.World.RigidBodyWorldItemEntity? thrownWeapon = null;
			if (isEmptyItem && _player?.InventoryComponent?.ReservedQuickBarSlots.Contains(slotIndex) == true)
			{
				thrownWeapon = FindThrownWeaponInSlot(slotIndex);
			}
			
			// 更新标签文字
			if (_quickSlotLabels[slotIndex] != null)
			{
				if (thrownWeapon != null)
					_quickSlotLabels[slotIndex].Text = thrownWeapon.ItemDefinition?.DisplayName ?? "";
				else if (isEmpty || isEmptyItem)
					_quickSlotLabels[slotIndex].Text = "";
				else
					_quickSlotLabels[slotIndex].Text = stack!.Item.DisplayName;
			}
			
			// 更新图标
			if (_quickSlotIcons[slotIndex] != null)
			{
				if (thrownWeapon != null)
				{
					// 显示投掷武器图标，半透明表示不可用状态
					_quickSlotIcons[slotIndex].Texture = thrownWeapon.ItemDefinition?.Icon;
					_quickSlotIcons[slotIndex].Modulate = new Color(1f, 1f, 1f, 0.55f);
				}
				else if (isEmpty || isEmptyItem)
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

			// 更新投掷冷却遮罩
			UpdateThrowCooldownOverlay(slotIndex, thrownWeapon);
		}
		
		/// <summary>
		/// 更新快捷栏槽位面板样式：未选中用武器栏框（未选中），选中用武器栏框（选中）
		/// </summary>
		private void UpdateSlotPanelStyle(int slotIndex, bool selected)
		{
			if (slotIndex < 0 || slotIndex >= 5) return;
			if (_quickSlotPanels[slotIndex] == null) return;
			var style = selected ? _quickSlotStyleSelected : _quickSlotStyleNormal;
			if (style == null) return;
			_quickSlotPanels[slotIndex].AddThemeStyleboxOverride("panel", style);
		}

		/// <summary>
		/// 更新左右手选择的快捷栏高亮（武器栏框选中/未选中贴图）
		/// </summary>
		/// <param name="leftHandSlotIndex">左手选择的槽位索引（0-4，-1表示未选择）</param>
		/// <param name="rightHandSlotIndex">右手选择的槽位索引（-1表示不高亮右手）</param>
		public void UpdateHandSlotHighlight(int leftHandSlotIndex, int rightHandSlotIndex = -1)
		{
			for (int i = 0; i < 5; i++)
			{
				bool selected = (i == rightHandSlotIndex && rightHandSlotIndex >= 0) || (i == leftHandSlotIndex && leftHandSlotIndex >= 0 && leftHandSlotIndex < 5);
				UpdateSlotPanelStyle(i, selected);
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
			
			// 填充快捷栏空槽位。若启用默认小木剑则跳过索引0。
			if (_quickBarContainer != null)
			{
				int startIndex = SpawnDefaultSwordInQuickBar ? 1 : 0;
				for (int i = startIndex; i < 5; i++)
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
			if (player?.InventoryComponent != null && _quickBarContainer != null)
			{
				player.InventoryComponent.SetQuickBar(_quickBarContainer);
				
				// 确保玩家连接快捷栏信号，以便左手物品与选中槽位严格对应
				player.ConnectQuickBarSignals();
				
				// 初始化左手选择（只在还没有选中时才初始化，避免覆盖用户选择）
				// 注意：使用 CallDeferred 确保在快捷栏连接完成后再初始化
				player.CallDeferred(SamplePlayer.MethodName.InitializeLeftHandSelection);
				
				// 延迟更新高亮，保留未选择状态（-1），避免把第一格误高亮。
				int highlightIndex = (player.LeftHandSlotIndex >= 0 && player.LeftHandSlotIndex < 5)
					? player.LeftHandSlotIndex
					: -1;
				CallDeferred(MethodName.UpdateHandSlotHighlight, highlightIndex, -1);
			}
			else
			{
				GD.PrintErr($"BattleHUD.ConnectPlayerInventory: Failed - Player={player != null}, InventoryComponent={player?.InventoryComponent != null}, QuickBarContainer={_quickBarContainer != null}");
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
			int safeMaxHealth = Mathf.Max(1, _maxHealth);
			int safeHealth = Mathf.Clamp(_currentHealth, 0, safeMaxHealth);

			if (HealthBar != null)
			{
				// 保持 HealthBar 作为你在编辑器中布置好的容器/图标，不去修改其贴图
				HealthBar.MaxValue = safeMaxHealth;
				HealthBar.Value = safeHealth;
			}

			// 使用单独的遮罩进度条来表现生命值长度
			if (HealthFillBar != null)
			{
				HealthFillBar.MinValue = 0;
				HealthFillBar.MaxValue = safeMaxHealth;
				HealthFillBar.Value = safeHealth;
			}

			// 在生命条中央叠加数值显示
			if (HealthValueLabel != null)
			{
				HealthValueLabel.Text = $"{safeHealth}/{safeMaxHealth}";
			}
			if (ScoreLabel != null)
			{
				// 只显示数字，便于与图标/背景组合
				ScoreLabel.Text = $"{_score}";
			}
			// PlayerStatsLabel 保留原逻辑用于调试/回退需要
		}

		private void ConfigureHealthFillBar()
		{
			if (HealthFillBar == null)
			{
				return;
			}

			HealthFillBar.FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight;
			HealthFillBar.MinValue = 0;
			HealthFillBar.MaxValue = Mathf.Max(1, _maxHealth);
			HealthFillBar.Value = Mathf.Clamp(_currentHealth, 0, Mathf.Max(1, _maxHealth));
		}

		/// <summary>
		/// 应用战斗 UI 的可自定义样式：快捷物品栏、金币文本、暂停键按钮等。
		/// 仅在对应 StyleBox 被设置时才覆盖，避免破坏你在编辑器中已有的视觉配置。
		/// </summary>
		private void ApplyCustomStyles()
		{
			// 快捷物品栏整体面板：武器栏底（与金币/暂停同样方式，默认贴图）
			var quickBarPanel = GetNodeOrNull<Panel>("QuickBarPanel");
			if (quickBarPanel != null)
			{
				StyleBox? panelStyle = QuickBarPanelStyle;
				if (panelStyle == null)
				{
					var tex = GD.Load<Texture2D>("res://resources/ui/武器栏底.png");
					if (tex != null)
					{
						var stb = new StyleBoxTexture();
						stb.Texture = tex;
						panelStyle = stb;
					}
				}
				if (panelStyle != null)
					quickBarPanel.AddThemeStyleboxOverride("panel", panelStyle);
			}

			// 每个快捷槽：武器栏框（未选中）/ 武器栏框（选中）
			var texNormal = GD.Load<Texture2D>("res://resources/ui/武器栏框（未选中）.png");
			var texSelected = GD.Load<Texture2D>("res://resources/ui/武器栏框（选中）.png");
			if (texNormal != null)
			{
				_quickSlotStyleNormal = new StyleBoxTexture();
				_quickSlotStyleNormal.Texture = texNormal;
			}
			if (texSelected != null)
			{
				_quickSlotStyleSelected = new StyleBoxTexture();
				_quickSlotStyleSelected.Texture = texSelected;
			}
			for (int i = 0; i < 5; i++)
			{
				UpdateSlotPanelStyle(i, false);
			}

			// 金币文本样式
			if (GoldLabel != null && GoldLabelStyle != null)
			{
				GoldLabel.AddThemeStyleboxOverride("normal", GoldLabelStyle);
			}

			// 金币图标：使用 金币.png，点采样避免透明边灰圈
			var goldIcon = GetNodeOrNull<TextureRect>("GoldContainer/GoldIcon");
			if (goldIcon != null)
			{
				goldIcon.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			}

			// 暂停键：透明 StyleBox + 用 TextureRect 显示 暂停.png 并设点采样，去掉透明边缘的灰圈
			if (PauseButton != null)
			{
				Texture2D? pauseTex = null;
				if (PauseButtonStyle is StyleBoxTexture st && st.Texture != null)
				{
					pauseTex = st.Texture;
				}
				else
				{
					pauseTex = GD.Load<Texture2D>("res://resources/ui/暂停.png");
				}

				var transparentStyle = new StyleBoxFlat();
				transparentStyle.BgColor = new Color(0, 0, 0, 0);
				transparentStyle.BorderWidthLeft = 0;
				transparentStyle.BorderWidthTop = 0;
				transparentStyle.BorderWidthRight = 0;
				transparentStyle.BorderWidthBottom = 0;

				PauseButton.AddThemeStyleboxOverride("normal", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("hover", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("pressed", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("disabled", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("focus", transparentStyle);

				if (pauseTex != null)
				{
					// 不再改按钮的尺寸和位置，完全沿用场景里 PauseButton 的布局（= 节点预览中的大小）
					PauseButton.Icon = null;
					var iconRect = PauseButton.GetNodeOrNull<TextureRect>("PauseIconRect");
					if (iconRect == null)
					{
						iconRect = new TextureRect
						{
							Name = "PauseIconRect",
							MouseFilter = Control.MouseFilterEnum.Ignore,
							ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
							StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
						};
						PauseButton.AddChild(iconRect);
					}
					iconRect.Texture = pauseTex;
					iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
					iconRect.OffsetLeft = 0;
					iconRect.OffsetTop = 0;
					iconRect.OffsetRight = 0;
					iconRect.OffsetBottom = 0;
					iconRect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
				}
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

				// 使用 C# 事件驱动血量/分数更新，避免仅依赖未触发的 Godot 信号。
				samplePlayer.StatsUpdated -= OnPlayerStatsUpdated;
				samplePlayer.StatsUpdated += OnPlayerStatsUpdated;
				
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

				// 绑定时立即刷新一次生命值，避免必须等待下一次事件才更新。
				UpdateStats(samplePlayer.CurrentHealth, samplePlayer.MaxHealth, samplePlayer.Score);
				
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
				samplePlayer.StatsUpdated -= OnPlayerStatsUpdated;

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

		private void OnPlayerStatsUpdated(int health, int maxHealth, int score)
		{
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
				GoldLabel.Text = $"{gold}";
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			// 定期刷新飞行中投掷武器的冷却遮罩进度
			if (_player?.InventoryComponent?.ReservedQuickBarSlots.Count > 0)
			{
				_throwCooldownUpdateTimer -= (float)delta;
				if (_throwCooldownUpdateTimer <= 0f)
				{
					_throwCooldownUpdateTimer = ThrowCooldownUpdateInterval;
					foreach (int i in _player.InventoryComponent.ReservedQuickBarSlots)
					{
						if (i >= 0 && i < 5) UpdateQuickBarSlot(i);
					}
				}
			}
		}

		/// <summary>
		/// 在 world_items 组中查找指定槽位的飞行中投掷武器实体
		/// </summary>
		private Kuros.Items.World.RigidBodyWorldItemEntity? FindThrownWeaponInSlot(int slotIndex)
		{
			var tree = GetTree();
			if (tree == null) return null;
			foreach (var node in tree.GetNodesInGroup("world_items"))
			{
				if (node is Kuros.Items.World.RigidBodyWorldItemEntity entity
					&& entity.ReservedQuickBarSlotIndex == slotIndex
					&& entity.LastDroppedBy == _player)
				{
					return entity;
				}
			}
			return null;
		}

		/// <summary>
		/// 更新指定槽位的投掷冷却扇形遮罩
		/// </summary>
		private void UpdateThrowCooldownOverlay(int slotIndex, Kuros.Items.World.RigidBodyWorldItemEntity? thrownWeapon)
		{
			if (_quickSlotIcons[slotIndex] == null) return;

			if (thrownWeapon != null && thrownWeapon.IsThrowWeaponInCooldown)
			{
				var overlay = GetOrCreateCooldownOverlay(slotIndex);
				overlay.Progress = thrownWeapon.ThrowCooldownProgress;
				overlay.Visible = true;
			}
			else
			{
				if (_quickSlotCooldownOverlays[slotIndex] != null)
					_quickSlotCooldownOverlays[slotIndex]!.Visible = false;
			}
		}

		/// <summary>
		/// 懒加载创建冷却遮罩节点（添加到图标节点的子节点，自动跟随尺寸）
		/// </summary>
		private ThrowCooldownOverlay GetOrCreateCooldownOverlay(int slotIndex)
		{
			if (_quickSlotCooldownOverlays[slotIndex] != null
				&& GodotObject.IsInstanceValid(_quickSlotCooldownOverlays[slotIndex]))
				return _quickSlotCooldownOverlays[slotIndex]!;

			var icon = _quickSlotIcons[slotIndex];
			var overlay = new ThrowCooldownOverlay();
			overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			overlay.OffsetLeft = 0; overlay.OffsetTop = 0;
			overlay.OffsetRight = 0; overlay.OffsetBottom = 0;
			icon.AddChild(overlay);
			_quickSlotCooldownOverlays[slotIndex] = overlay;
			return overlay;
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
		/// <summary>
		/// 投掷武器冷却扇形遮罩控件
		/// 从12点钟方向顺时针绘制半透明扇形，覆盖图标表示冷却剩余时间
		/// </summary>
		private partial class ThrowCooldownOverlay : Control
		{
			private float _progress;
			public float Progress
			{
				get => _progress;
				set { _progress = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
			}

			public override void _Ready()
			{
				base._Ready();
				MouseFilter = MouseFilterEnum.Ignore;
			}

			public override void _Draw()
			{
				base._Draw();
				if (_progress <= 0f) return;

				Vector2 rectSize = Size;
				Vector2 center = rectSize * 0.5f;
				Vector2 halfSize = rectSize * 0.5f;

				var overlayColor = new Color(0f, 0f, 0f, 0.5f);

				int steps = 48;
				float startAngle = -Mathf.Pi / 2f; // 从12点钟方向开始
				float endAngle = startAngle + Mathf.Pi * 2f * _progress;
				var points = new Vector2[steps + 2];
				points[0] = center;
				for (int i = 0; i <= steps; i++)
				{
					float t = (float)i / steps;
					float angle = Mathf.Lerp(startAngle, endAngle, t);
					Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
					points[i + 1] = center + GetRectEdgePoint(dir, halfSize);
				}
				DrawPolygon(points, new Color[] { overlayColor });
			}

			private static Vector2 GetRectEdgePoint(Vector2 direction, Vector2 halfSize)
			{
				if (direction == Vector2.Zero) return Vector2.Zero;
				float tx = direction.X != 0f ? halfSize.X / Mathf.Abs(direction.X) : float.MaxValue;
				float ty = direction.Y != 0f ? halfSize.Y / Mathf.Abs(direction.Y) : float.MaxValue;
				return direction * Mathf.Min(tx, ty);
			}

			public override void _Notification(int what)
			{
				base._Notification(what);
				if (what == NotificationResized) QueueRedraw();
			}
		}
	}
}
