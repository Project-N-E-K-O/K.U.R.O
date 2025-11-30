using Godot;
using Kuros.Items;
using Kuros.Managers;

namespace Kuros.UI
{
	/// <summary>
	/// 获得物品弹窗 - 当玩家第一次获得物品时显示物品信息
	/// </summary>
	public partial class ItemObtainedPopup : Control
	{
		[ExportCategory("UI References")]
		[Export] public Label TitleLabel { get; private set; } = null!;
		[Export] public TextureRect ItemIconRect { get; private set; } = null!;
		[Export] public RichTextLabel ItemInfoLabel { get; private set; } = null!;
		[Export] public Panel BackgroundPanel { get; private set; } = null!;
		[Export] public Control ClickableArea { get; private set; } = null!; // 可点击的空白区域

		[ExportCategory("Settings")]
		[Export] public string TitleText { get; set; } = "获得新物品";

		private ItemDefinition? _currentItem;
		private bool _isShowing = false;
		private bool _wasPausedBefore = false;

		// 信号
		[Signal] public delegate void PopupClosedEventHandler();

		public override void _Ready()
		{
			base._Ready();

			// 自动查找节点引用
			CacheNodeReferences();

			// 初始化UI
			InitializeUI();

			// 默认隐藏
			Visible = false;
		}

		private void CacheNodeReferences()
		{
			TitleLabel ??= GetNodeOrNull<Label>("BackgroundPanel/VBoxContainer/TitleLabel");
			ItemIconRect ??= GetNodeOrNull<TextureRect>("BackgroundPanel/VBoxContainer/ItemIconRect");
			ItemInfoLabel ??= GetNodeOrNull<RichTextLabel>("BackgroundPanel/VBoxContainer/ItemInfoLabel");
			BackgroundPanel ??= GetNodeOrNull<Panel>("BackgroundPanel");
			ClickableArea ??= GetNodeOrNull<Control>("ClickableArea");
		}

		private void InitializeUI()
		{
			// 设置处理模式，确保在暂停时也能接收输入
			ProcessMode = ProcessModeEnum.Always;

			// 设置标题
			if (TitleLabel != null)
			{
				TitleLabel.Text = TitleText;
			}

			// 连接点击区域信号
			if (ClickableArea != null)
			{
				ClickableArea.GuiInput += OnClickableAreaGuiInput;
			}
		}

		/// <summary>
		/// 显示物品信息弹窗
		/// </summary>
		/// <param name="item">物品定义</param>
		public void ShowItem(ItemDefinition item)
		{
			if (item == null)
			{
				GD.PrintErr("ItemObtainedPopup: 物品为空！");
				return;
			}

			if (_isShowing)
			{
				GD.PrintErr("ItemObtainedPopup: 弹窗已在显示中，无法显示新物品");
				return;
			}

			_currentItem = item;
			_isShowing = true;

			// 更新UI显示
			UpdateItemDisplay(item);

			// 显示弹窗
			ShowPopup();
		}

		/// <summary>
		/// 更新物品显示
		/// </summary>
		private void UpdateItemDisplay(ItemDefinition item)
		{
			// 更新物品图标
			if (ItemIconRect != null)
			{
				if (item.Icon != null)
				{
					ItemIconRect.Texture = item.Icon;
					ItemIconRect.Visible = true;
				}
				else
				{
					ItemIconRect.Visible = false;
				}
			}

			// 更新物品信息
			if (ItemInfoLabel != null)
			{
				string infoText = BuildItemInfoText(item);
				ItemInfoLabel.Text = infoText;
			}
		}

		/// <summary>
		/// 构建物品信息文本
		/// </summary>
		private string BuildItemInfoText(ItemDefinition item)
		{
			var text = new System.Text.StringBuilder();

			// 物品名称
			text.AppendLine($"[b]{item.DisplayName}[/b]");
			text.AppendLine();

			// 物品描述
			if (!string.IsNullOrEmpty(item.Description))
			{
				text.AppendLine(item.Description);
				text.AppendLine();
			}

			// 物品分类
			if (!string.IsNullOrEmpty(item.Category))
			{
				text.AppendLine($"[i]分类: {item.Category}[/i]");
			}

			// 最大堆叠数量
			if (item.MaxStackSize > 1)
			{
				text.AppendLine($"[i]最大堆叠: {item.MaxStackSize}[/i]");
			}

			// TODO: 如果有攻击力等属性，可以在这里添加
			// 例如：text.AppendLine($"[b]攻击力: {item.AttackPower}[/b]");

			return text.ToString();
		}

		/// <summary>
		/// 显示弹窗
		/// </summary>
		private void ShowPopup()
		{
			Visible = true;
			ProcessMode = ProcessModeEnum.Always;
			SetProcessInput(true);
			SetProcessUnhandledInput(true);

			// 保存当前暂停状态
			var tree = GetTree();
			if (tree != null)
			{
				_wasPausedBefore = tree.Paused;
				// 暂停游戏
				tree.Paused = true;
			}

			// 设置较低的ZIndex，确保菜单栏可以在弹窗之上显示
			ZIndex = 100;

			// 设置鼠标过滤：弹窗本身不阻止鼠标，但ClickableArea会处理点击
			// 这样菜单栏可以正常接收鼠标输入
			MouseFilter = MouseFilterEnum.Ignore;
			if (ClickableArea != null)
			{
				ClickableArea.MouseFilter = MouseFilterEnum.Stop; // 只有点击区域阻止鼠标
			}
			if (BackgroundPanel != null)
			{
				BackgroundPanel.MouseFilter = MouseFilterEnum.Stop; // 背景面板阻止鼠标穿透
			}

			// 将窗口移到父节点的最后，确保输入处理优先级最高
			// 在Godot中，_Input()是从后往前调用的，所以最后面的节点会先处理输入
			// 这样ESC键会被弹窗优先处理并禁用
			var parent = GetParent();
			if (parent != null)
			{
				// 检查菜单是否打开，如果打开，不要移到菜单之后
				var battleMenu = Kuros.Managers.UIManager.Instance?.GetUI<BattleMenu>("BattleMenu");
				if (battleMenu != null && battleMenu.Visible && battleMenu.GetParent() == parent)
				{
					// 菜单已打开，将弹窗移到菜单之前（但仍在最后，因为菜单会在弹窗之后）
					var menuIndex = battleMenu.GetIndex();
					var popupIndex = GetIndex();
					if (popupIndex >= menuIndex)
					{
						parent.MoveChild(this, menuIndex);
						GD.Print("ItemObtainedPopup: 菜单已打开，已将弹窗移到菜单之前");
					}
				}
				else
				{
					// 菜单未打开，移到父节点的最后，确保输入处理优先级最高
					// 在Godot中，_Input()是从后往前调用的，所以最后面的节点会先处理输入
					// 这样ESC键会被弹窗优先捕获并禁用
					var lastIndex = parent.GetChildCount() - 1;
					parent.MoveChild(this, lastIndex);
					GD.Print("ItemObtainedPopup: 已将弹窗移到最后，确保ESC键优先处理");
				}
			}
		}

		/// <summary>
		/// 隐藏弹窗
		/// </summary>
		public void HidePopup()
		{
			if (!_isShowing)
			{
				return;
			}

			Visible = false;
			_isShowing = false;
			_currentItem = null;
			SetProcessInput(false);
			SetProcessUnhandledInput(false);

			// 清除Space键和Attack动作的输入状态，防止关闭后触发攻击
			// 使用延迟恢复游戏，确保输入事件完全过期
			CallDeferred(MethodName.RestoreGameState);
		}

		/// <summary>
		/// 恢复游戏状态（延迟调用，确保输入事件已过期）
		/// </summary>
		private void RestoreGameState()
		{
			// 发送关闭信号
			EmitSignal(SignalName.PopupClosed);

			// 使用Timer延迟恢复游戏状态，确保Space键的输入事件完全过期
			// 延迟0.2秒（约12帧），足够让输入事件过期
			var tree = GetTree();
			if (tree != null)
			{
				// 先清除attack动作的输入状态（通过模拟释放事件）
				// 注意：Godot没有直接清除输入的方法，所以我们使用延迟
				var timer = tree.CreateTimer(0.2);
				timer.Timeout += () =>
				{
					if (IsInstanceValid(this) && tree != null)
					{
						// 检查是否有其他UI需要保持暂停（如菜单栏）
						bool shouldPause = ShouldKeepPaused();
						tree.Paused = shouldPause || _wasPausedBefore;
					}
				};
			}
		}

		/// <summary>
		/// 检查是否应该保持暂停状态
		/// </summary>
		private bool ShouldKeepPaused()
		{
			// 检查菜单栏是否打开
			var battleMenu = Kuros.Managers.UIManager.Instance?.GetUI<BattleMenu>("BattleMenu");
			if (battleMenu != null && battleMenu.Visible)
			{
				return true;
			}

			// 检查物品栏是否打开
			var inventoryWindow = Kuros.Managers.UIManager.Instance?.GetUI<InventoryWindow>("InventoryWindow");
			if (inventoryWindow != null && inventoryWindow.Visible)
			{
				return true;
			}

			return false;
		}

		/// <summary>
		/// 处理输入事件
		/// </summary>
		public override void _Input(InputEvent @event)
		{
			if (!_isShowing || !Visible)
			{
				return;
			}

			// ESC键：完全禁用（弹窗显示时不允许使用ESC键）
			// 检查动作映射和直接键码，确保完全捕获ESC键
			bool isEscKey = false;
			if (@event.IsActionPressed("ui_cancel"))
			{
				isEscKey = true;
			}
			else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
			{
				if (keyEvent.Keycode == Key.Escape || keyEvent.PhysicalKeycode == Key.Escape)
				{
					isEscKey = true;
				}
			}

			if (isEscKey)
			{
				// 完全禁用ESC键：标记为已处理并接受事件，防止传播到其他节点
				GetViewport().SetInputAsHandled();
				AcceptEvent();
				return;
			}

			// Space键：关闭弹窗
			if (@event.IsActionPressed("attack") || @event.IsActionPressed("ui_accept"))
			{
				HandleSpaceKey();
				GetViewport().SetInputAsHandled();
				AcceptEvent(); // 确保事件被接受，防止传播
				return;
			}

			// 禁止其他所有键盘输入（除了鼠标和Space）
			if (@event is InputEventKey keyEvent2 && keyEvent2.Pressed)
			{
				// 只允许Space（ESC已经在上面被禁用）
				if (keyEvent2.Keycode != Key.Space &&
				    keyEvent2.PhysicalKeycode != Key.Space)
				{
					GetViewport().SetInputAsHandled();
				}
			}
		}

		/// <summary>
		/// 处理未处理的输入
		/// </summary>
		public override void _UnhandledInput(InputEvent @event)
		{
			if (!_isShowing || !Visible)
			{
				return;
			}

			// ESC键：完全禁用（弹窗显示时不允许使用ESC键）
			// 检查动作映射和直接键码，确保完全捕获ESC键
			bool isEscKey = false;
			if (@event.IsActionPressed("ui_cancel"))
			{
				isEscKey = true;
			}
			else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
			{
				if (keyEvent.Keycode == Key.Escape || keyEvent.PhysicalKeycode == Key.Escape)
				{
					isEscKey = true;
				}
			}

			if (isEscKey)
			{
				// 完全禁用ESC键：标记为已处理并接受事件，防止传播到其他节点
				GetViewport().SetInputAsHandled();
				AcceptEvent();
				return;
			}

			// Space键：关闭弹窗
			if (@event.IsActionPressed("attack") || @event.IsActionPressed("ui_accept"))
			{
				HandleSpaceKey();
				GetViewport().SetInputAsHandled();
				AcceptEvent(); // 确保事件被接受，防止传播
				return;
			}
		}


		/// <summary>
		/// 处理Space键 - 关闭弹窗
		/// </summary>
		private void HandleSpaceKey()
		{
			// 立即隐藏弹窗，但延迟恢复游戏状态
			// 这样可以防止Space键的输入传播到游戏逻辑中
			_isShowing = false;
			Visible = false;
			SetProcessInput(false);
			SetProcessUnhandledInput(false);
			_currentItem = null;
			
			// 延迟恢复游戏状态，确保输入事件完全过期
			CallDeferred(MethodName.RestoreGameState);
		}

		/// <summary>
		/// 处理点击区域输入
		/// </summary>
		private void OnClickableAreaGuiInput(InputEvent @event)
		{
			if (!_isShowing || !Visible)
			{
				return;
			}

			// 检查鼠标点击是否在菜单上，如果是，则不处理（让菜单处理）
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
			{
				var battleMenu = Kuros.Managers.UIManager.Instance?.GetUI<BattleMenu>("BattleMenu");
				if (battleMenu != null && battleMenu.Visible)
				{
					// 检查点击位置是否在菜单内
					var menuRect = battleMenu.GetRect();
					if (menuRect.HasPoint(mouseEvent.GlobalPosition))
					{
						// 点击在菜单上，不处理，让菜单处理
						return;
					}
				}

				// 鼠标左键点击空白区域关闭弹窗
				if (mouseEvent.ButtonIndex == MouseButton.Left)
				{
					HidePopup();
					AcceptEvent();
				}
			}
		}

		public override void _ExitTree()
		{
			// 确保恢复游戏状态
			if (_isShowing)
			{
				var tree = GetTree();
				if (tree != null)
				{
					tree.Paused = _wasPausedBefore;
				}
			}

			base._ExitTree();
		}
	}
}

