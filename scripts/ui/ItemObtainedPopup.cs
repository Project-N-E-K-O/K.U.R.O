using Godot;
using Kuros.Items;
using Kuros.Managers;
using System.Collections.Generic;

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
		private readonly Queue<ItemDefinition> _pendingItems = new Queue<ItemDefinition>();

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
		/// 显示物品信息弹窗（将物品加入队列，如果当前没有显示则立即开始显示）
		/// </summary>
		/// <param name="item">物品定义</param>
		public void ShowItem(ItemDefinition item)
		{
			if (item == null)
			{
				GD.PrintErr("ItemObtainedPopup: 物品为空！");
				return;
			}

			// 将物品加入队列
			_pendingItems.Enqueue(item);
			GD.Print($"ItemObtainedPopup: 物品已加入队列: {item.DisplayName}，队列长度: {_pendingItems.Count}");

			// 如果当前没有显示，立即开始显示流程
			if (!_isShowing)
			{
				ProcessNextItem();
			}
		}

		/// <summary>
		/// 立即显示物品（清空队列并立即显示新物品）
		/// </summary>
		/// <param name="item">物品定义</param>
		public void ShowItemImmediate(ItemDefinition item)
		{
			if (item == null)
			{
				GD.PrintErr("ItemObtainedPopup: 物品为空！");
				return;
			}

			// 清空队列
			_pendingItems.Clear();
			GD.Print("ItemObtainedPopup: 队列已清空，立即显示新物品");

			// 如果当前正在显示，先关闭（但不处理队列，因为我们已经清空了）
			if (_isShowing)
			{
				// 直接关闭当前显示
				Visible = false;
				_isShowing = false;
				_currentItem = null;
				SetProcessInput(false);
				SetProcessUnhandledInput(false);
				
				// 发送关闭信号，但不恢复游戏状态（因为我们要立即显示新物品）
				EmitSignal(SignalName.PopupClosed);
				
				// 使用短暂延迟确保UI状态清理完成，然后立即显示新物品
				var tree = GetTree();
				if (tree != null)
				{
					var timer = tree.CreateTimer(0.1);
					timer.Timeout += () =>
					{
						if (IsInstanceValid(this))
						{
							// 立即显示新物品
							_pendingItems.Enqueue(item);
							ProcessNextItem();
						}
					};
					return;
				}
			}

			// 如果当前没有显示，立即显示新物品
			_pendingItems.Enqueue(item);
			ProcessNextItem();
		}

		/// <summary>
		/// 处理队列中的下一个物品（在主线程上执行）
		/// </summary>
		private void ProcessNextItem()
		{
			if (_pendingItems.Count == 0)
			{
				return;
			}

			var item = _pendingItems.Dequeue();
			_currentItem = item;
			_isShowing = true;

			GD.Print($"ItemObtainedPopup: 开始显示物品: {item.DisplayName}，剩余队列: {_pendingItems.Count}");

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
			
			// 请求暂停游戏
			if (PauseManager.Instance != null)
			{
				PauseManager.Instance.PushPause();
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
					// 菜单已打开，将弹窗移到菜单之前
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
					// 菜单未打开，移到所有其他弹窗之后，但确保在菜单之前
					// 首先找到所有其他 ItemObtainedPopup 实例
					int targetIndex = -1;
					for (int i = parent.GetChildCount() - 1; i >= 0; i--)
					{
						var child = parent.GetChild(i);
						if (child is ItemObtainedPopup otherPopup && otherPopup != this && otherPopup.Visible)
						{
							targetIndex = i + 1;
							break;
						}
					}
					
					// 如果没有找到其他弹窗，移到最后
					if (targetIndex < 0)
					{
						targetIndex = parent.GetChildCount() - 1;
					}
					
					// 确保不超过父节点的子节点数量
					if (targetIndex >= parent.GetChildCount())
					{
						targetIndex = parent.GetChildCount() - 1;
					}
					
					var currentIndex = GetIndex();
					if (currentIndex != targetIndex)
					{
						parent.MoveChild(this, targetIndex);
						GD.Print($"ItemObtainedPopup: 已将弹窗移到索引 {targetIndex}，确保正确的显示顺序");
					}
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
					if (IsInstanceValid(this))
					{
						// 检查是否有其他UI需要保持暂停（如菜单栏、物品栏、对话）
						if (ShouldKeepPaused())
						{
							// 有其他UI需要保持暂停，不取消暂停请求
							// 但继续处理队列中的下一个物品
							ProcessNextItemFromQueue();
							return;
						}
						
						// 取消暂停请求
						if (PauseManager.Instance != null)
						{
							PauseManager.Instance.PopPause();
						}

						// 处理队列中的下一个物品（如果有）
						ProcessNextItemFromQueue();
					}
				};
			}
		}

		/// <summary>
		/// 从队列中处理下一个物品（在主线程上执行）
		/// </summary>
		private void ProcessNextItemFromQueue()
		{
			if (_pendingItems.Count > 0)
			{
				// 使用 CallDeferred 确保在主线程上执行
				CallDeferred(MethodName.ProcessNextItem);
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

			// 检查对话是否激活
			if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
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

			// ESC键：关闭弹窗（返回上一层级）
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
				// ESC键关闭弹窗
				HandleSpaceKey(); // 使用相同的关闭逻辑
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

			// 禁止其他所有键盘输入（除了鼠标、Space和ESC）
			if (@event is InputEventKey keyEvent2 && keyEvent2.Pressed)
			{
				// 只允许Space和ESC
				if (keyEvent2.Keycode != Key.Space &&
				    keyEvent2.PhysicalKeycode != Key.Space &&
				    keyEvent2.Keycode != Key.Escape &&
				    keyEvent2.PhysicalKeycode != Key.Escape)
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

			// ESC键：关闭弹窗（返回上一层级）
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
				// ESC键关闭弹窗
				HandleSpaceKey(); // 使用相同的关闭逻辑
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
			// RestoreGameState 会自动处理队列中的下一个物品
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
					// 检查点击位置是否在菜单内（使用全局坐标空间）
					var menuRect = battleMenu.GetGlobalRect();
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
			// 取消订阅点击区域信号，避免悬挂订阅
			if (ClickableArea != null)
			{
				ClickableArea.GuiInput -= OnClickableAreaGuiInput;
			}

			// 确保恢复游戏状态
			if (_isShowing)
			{
				// 取消暂停请求
				if (PauseManager.Instance != null)
				{
					PauseManager.Instance.PopPause();
				}
			}

			base._ExitTree();
		}
	}
}

