using Godot;
using Kuros.Data;
using Kuros.Managers;
using Kuros.Core;
using System.Collections.Generic;

namespace Kuros.UI
{
	/// <summary>
	/// 对话UI窗口 - 显示NPC对话内容
	/// </summary>
	public partial class DialogueWindow : Control
	{
		[ExportCategory("UI References")]
		[Export] public Panel MainPanel { get; private set; } = null!;
		[Export] public Label SpeakerNameLabel { get; private set; } = null!;
		[Export] public RichTextLabel DialogueTextLabel { get; private set; } = null!;
		[Export] public TextureRect PortraitTextureRect { get; private set; } = null!;
		[Export] public VBoxContainer ChoicesContainer { get; private set; } = null!;
		[Export] public Button ContinueButton { get; private set; } = null!;
		[Export] public Label InstructionLabel { get; private set; } = null!;
		[Export] public Label GoldLabel { get; private set; } = null!;
		
		[ExportCategory("Settings")]
		[Export] public float TextSpeed { get; set; } = 0.05f; // 文字显示速度（秒/字符）
		[Export] public bool ShowPortrait { get; set; } = true;
		
		// 当前对话数据
		private DialogueData? _currentDialogue;
		private DialogueEntry? _currentEntry;
		private int _currentEntryIndex = -1;
		
		// 防止重复结束对话
		private bool _isEndingDialogue = false;
		
		// 文字显示状态
		private bool _isDisplayingText = false;
		private string _fullText = "";
		private double _textDisplayTimer = 0.0;
		private int _displayedCharCount = 0;
		
		// 选项按钮列表
		private readonly List<Button> _choiceButtons = new();
		
		// 玩家引用（用于监听金币变化）
		private SamplePlayer? _player;
		
		// 信号
		[Signal] public delegate void DialogueEndedEventHandler();
		[Signal] public delegate void ChoiceSelectedEventHandler(int choiceIndex, int nextEntryIndex);
		[Signal] public delegate void DialogueActionTriggeredEventHandler(string actionId);
		
		public override void _Ready()
		{
			base._Ready();
			
			// 自动查找节点引用
			CacheNodeReferences();
			
			// 初始化UI
			InitializeUI();
			
			// 默认隐藏
			HideWindow();
		}
		
		private void CacheNodeReferences()
		{
			MainPanel ??= GetNodeOrNull<Panel>("MainPanel");
			SpeakerNameLabel ??= GetNodeOrNull<Label>("MainPanel/Header/SpeakerNameLabel");
			DialogueTextLabel ??= GetNodeOrNull<RichTextLabel>("MainPanel/Body/VBoxContainer/DialogueTextLabel");
			PortraitTextureRect ??= GetNodeOrNull<TextureRect>("MainPanel/Body/PortraitTextureRect");
			ChoicesContainer ??= GetNodeOrNull<VBoxContainer>("MainPanel/Body/VBoxContainer/ChoicesContainer");
			
			if (ChoicesContainer == null)
			{
				GD.PrintErr("DialogueWindow: 无法找到ChoicesContainer节点！");
				GD.PrintErr("DialogueWindow: 尝试的路径: MainPanel/Body/VBoxContainer/ChoicesContainer");
				// 尝试备用路径
				ChoicesContainer = GetNodeOrNull<VBoxContainer>("MainPanel/Body/ChoicesContainer");
			}
			ContinueButton ??= GetNodeOrNull<Button>("MainPanel/Footer/ContinueButton");
			InstructionLabel ??= GetNodeOrNull<Label>("MainPanel/Footer/InstructionLabel");
			GoldLabel ??= GetNodeOrNull<Label>("MainPanel/Header/GoldLabel");
			
			// 连接按钮信号
			if (ContinueButton != null)
			{
				ContinueButton.Pressed += OnContinueButtonPressed;
			}
		}
		
		private void InitializeUI()
		{
			// 设置RichTextLabel的显示模式
			if (DialogueTextLabel != null)
			{
				DialogueTextLabel.BbcodeEnabled = true;
				DialogueTextLabel.FitContent = true;
			}
			
			// 初始化指令标签
			if (InstructionLabel != null)
			{
				InstructionLabel.Text = "按 [Space] 继续 / [Esc] 跳过";
			}
			
			// 设置处理模式，确保在暂停时也能接收输入
			ProcessMode = ProcessModeEnum.Always;
		}
		
		/// <summary>
		/// 开始显示对话
		/// </summary>
		public void StartDialogue(DialogueData dialogue)
		{
			if (dialogue == null)
			{
				GD.PrintErr("DialogueWindow: 对话数据为空！");
				return;
			}
			
			// 重置结束标志
			_isEndingDialogue = false;
			
			_currentDialogue = dialogue;
			_currentEntryIndex = dialogue.StartEntryIndex;
			
			ShowWindow();
			DisplayEntry(_currentEntryIndex);
		}
		
		/// <summary>
		/// 显示指定索引的对话条目
		/// </summary>
		private void DisplayEntry(int entryIndex)
		{
			try
			{
				if (_currentDialogue == null)
				{
					GD.PrintErr("DialogueWindow: 对话数据为空，无法显示条目");
					EndDialogue();
					return;
				}
				
				// 验证索引范围
				if (entryIndex < 0 || entryIndex >= _currentDialogue.Entries.Count)
				{
					GD.PrintErr($"DialogueWindow: 条目索引 {entryIndex} 超出范围 (0-{_currentDialogue.Entries.Count - 1})");
					EndDialogue();
					return;
				}
					
				var entry = _currentDialogue.GetEntry(entryIndex);
				if (entry == null)
				{
					GD.PrintErr($"DialogueWindow: 无法获取索引 {entryIndex} 的对话条目");
					EndDialogue();
					return;
				}
				
				_currentEntry = entry;
				_currentEntryIndex = entryIndex;
				
				// 更新说话者名称
				if (SpeakerNameLabel != null)
				{
					SpeakerNameLabel.Text = entry.SpeakerName;
				}
				
				// 更新头像
				if (PortraitTextureRect != null)
				{
					if (entry.SpeakerPortrait != null && ShowPortrait)
					{
						PortraitTextureRect.Texture = entry.SpeakerPortrait;
						PortraitTextureRect.Visible = true;
					}
					else
					{
						PortraitTextureRect.Visible = false;
					}
				}
				
				// 清除选项（确保之前的选项被清除）
				ClearChoices();
				
				// 重置文本显示状态
				_isDisplayingText = false;
				_displayedCharCount = 0;
				_textDisplayTimer = 0.0;
				
				// 清空文本标签（确保旧文本被清除）
				if (DialogueTextLabel != null)
				{
					DialogueTextLabel.Text = "";
				}
				
				// 显示对话文本
				_fullText = entry.Text ?? "";
				if (!string.IsNullOrEmpty(_fullText))
				{
					StartTextDisplay(_fullText);
				}
				else
				{
					GD.PrintErr("DialogueWindow: 对话文本为空");
				}
				
				// 如果有选项，显示选项按钮
				if (entry.Choices != null && entry.Choices.Count > 0)
				{
					// 确保继续按钮隐藏
					if (ContinueButton != null)
					{
						ContinueButton.Visible = false;
					}
					ShowChoices(entry.Choices);
				}
				else
				{
					// 没有选项，显示继续按钮
					if (ContinueButton != null)
					{
						ContinueButton.Visible = true;
					}
					// 确保选项容器隐藏
					if (ChoicesContainer != null)
					{
						ChoicesContainer.Visible = false;
					}
				}
				
				// 如果设置了自动推进
				if (entry.AutoAdvance)
				{
					var timer = GetTree().CreateTimer(entry.AutoAdvanceDelay);
					timer.Timeout += () =>
					{
						if (IsInstanceValid(this) && _currentEntry == entry)
						{
							AdvanceToNextEntry();
						}
					};
				}
			}
			catch (System.Exception e)
			{
				GD.PrintErr($"DialogueWindow: 显示对话条目时发生异常: {e.GetType().Name} - {e.Message}");
				GD.PrintErr($"DialogueWindow: 堆栈: {e.StackTrace}");
				EndDialogue();
			}
		}
		
		/// <summary>
		/// 开始逐字显示文本
		/// </summary>
		private void StartTextDisplay(string text)
		{
			_fullText = text;
			_displayedCharCount = 0;
			_textDisplayTimer = 0.0;
			_isDisplayingText = true;
			
			if (DialogueTextLabel != null)
			{
				DialogueTextLabel.Text = "";
			}
		}
		
		/// <summary>
		/// 立即完成文本显示
		/// </summary>
		private void CompleteTextDisplay()
		{
			if (DialogueTextLabel != null)
			{
				DialogueTextLabel.Text = _fullText;
			}
			_displayedCharCount = _fullText.Length;
			_isDisplayingText = false;
		}
		
		/// <summary>
		/// 显示选项按钮
		/// </summary>
		private void ShowChoices(Godot.Collections.Array<DialogueChoice> choices)
		{
			if (ChoicesContainer == null)
			{
				GD.PrintErr("DialogueWindow: ChoicesContainer为空，无法显示选项");
				return;
			}
			
			if (choices == null || choices.Count == 0)
			{
				GD.PrintErr("DialogueWindow: 选项数组为空");
				return;
			}
			
			ClearChoices();
			
			// 隐藏继续按钮
			if (ContinueButton != null)
			{
				ContinueButton.Visible = false;
			}
			
			// 创建选项按钮
			for (int i = 0; i < choices.Count; i++)
			{
				var choice = choices[i];
				if (choice == null)
				{
					GD.PrintErr($"DialogueWindow: 选项 {i} 为空");
					continue;
				}
				
				var button = new Button();
				button.Name = $"ChoiceButton_{i}";
				button.Text = choice.Text ?? $"选项 {i + 1}";
				button.CustomMinimumSize = new Vector2(400, 40);
				button.ProcessMode = ProcessModeEnum.Always; // 确保在暂停时也能接收输入
				
				// 使用闭包捕获索引
				int choiceIndex = i;
				button.Pressed += () => OnChoiceSelected(choiceIndex);
				
				ChoicesContainer.AddChild(button);
				_choiceButtons.Add(button);
			}
			
			ChoicesContainer.Visible = true;
		}
		
		/// <summary>
		/// 清除所有选项按钮
		/// </summary>
		private void ClearChoices()
		{
			foreach (var button in _choiceButtons)
			{
				if (IsInstanceValid(button))
				{
					button.QueueFree();
				}
			}
			_choiceButtons.Clear();
			
			if (ChoicesContainer != null)
			{
				ChoicesContainer.Visible = false;
			}
		}
		
		/// <summary>
		/// 选项被选择
		/// </summary>
		private void OnChoiceSelected(int choiceIndex)
		{
			if (_currentEntry == null)
			{
				GD.PrintErr("DialogueWindow: 当前对话条目为空");
				return;
			}
			
			if (_currentEntry.Choices == null)
			{
				GD.PrintErr("DialogueWindow: 当前对话条目的选项为空");
				return;
			}
			
			if (choiceIndex < 0 || choiceIndex >= _currentEntry.Choices.Count)
			{
				GD.PrintErr($"DialogueWindow: 选项索引超出范围: {choiceIndex}, 总数: {_currentEntry.Choices.Count}");
				return;
			}
			
			var choice = _currentEntry.Choices[choiceIndex];
			if (choice == null)
			{
				GD.PrintErr($"DialogueWindow: 选项 {choiceIndex} 为空");
				return;
			}
			
			// 触发选择行为
			if (!string.IsNullOrEmpty(choice.OnSelectedAction))
			{
				EmitSignal(SignalName.DialogueActionTriggered, choice.OnSelectedAction);
			}
			
			// 发送选择信号
			EmitSignal(SignalName.ChoiceSelected, choiceIndex, choice.NextEntryIndex);
			
			// 立即清除选项按钮（在显示新条目之前）
			ClearChoices();
			
			// 移动到下一个条目
			if (choice.NextEntryIndex >= 0)
			{
				// 验证索引是否有效
				if (_currentDialogue != null && choice.NextEntryIndex < _currentDialogue.Entries.Count)
				{
					// 使用CallDeferred确保UI更新完成后再显示新条目
					CallDeferred(MethodName.DisplayEntry, choice.NextEntryIndex);
				}
				else
				{
					GD.PrintErr($"DialogueWindow: 无效的条目索引 {choice.NextEntryIndex}, 对话总条目数: {_currentDialogue?.Entries.Count ?? 0}");
					EndDialogue();
				}
			}
			else
			{
				CallDeferred(MethodName.EndDialogue);
			}
		}
		
		/// <summary>
		/// 继续按钮被按下
		/// </summary>
		private void OnContinueButtonPressed()
		{
			// 如果当前条目有选项，不应该显示继续按钮
			if (_currentEntry != null && _currentEntry.Choices != null && _currentEntry.Choices.Count > 0)
			{
				GD.PrintErr("DialogueWindow: 警告 - 有选项的条目不应该显示继续按钮！");
				return;
			}
			
			if (_isDisplayingText)
			{
				// 如果正在显示文本，立即完成显示
				CompleteTextDisplay();
			}
			else
			{
				// 文本已显示完成，推进到下一句
				AdvanceToNextEntry();
			}
		}
		
		/// <summary>
		/// 推进到下一个对话条目
		/// </summary>
		private void AdvanceToNextEntry()
		{
			if (_currentDialogue == null)
			{
				EndDialogue();
				return;
			}
			
			// 如果当前条目有选项，不应该通过继续按钮推进
			if (_currentEntry != null && _currentEntry.Choices != null && _currentEntry.Choices.Count > 0)
			{
				return;
			}
			
		// 确定下一个条目的索引
		int nextIndex = -1;
		
		if (_currentEntry != null)
		{
			if (_currentEntry.NextEntryIndex == -2)
			{
				// 默认：继续下一个（线性流程）
				nextIndex = _currentEntryIndex + 1;
			}
			else if (_currentEntry.NextEntryIndex >= 0)
			{
				// 跳转到指定条目
				nextIndex = _currentEntry.NextEntryIndex;
			}
			else
			{
				// -1 或其他负数：结束对话
				nextIndex = -1;
			}
		}
		else
		{
			// 异常情况，尝试线性推进
			nextIndex = _currentEntryIndex + 1;
		}
		
		var nextEntry = (nextIndex >= 0 && _currentDialogue != null) ? _currentDialogue.GetEntry(nextIndex) : null;
		
		if (nextEntry != null)
		{
			// 触发当前条目的结束行为（只在有下一条目时触发，最终条目由EndDialogue处理）
			if (_currentEntry != null && !string.IsNullOrEmpty(_currentEntry.OnDialogueEndAction))
			{
				EmitSignal(SignalName.DialogueActionTriggered, _currentEntry.OnDialogueEndAction);
			}
			DisplayEntry(nextIndex);
		}
		else
		{
			// 没有下一条目或显式结束，让EndDialogue负责触发最终条目的结束行为
			EndDialogue();
		}
		}
		
		/// <summary>
		/// 结束对话
		/// </summary>
		public void EndDialogue()
		{
			// 防止重复调用
			if (_isEndingDialogue)
			{
				return;
			}
			
			// 如果对话已经结束（数据已清理），直接返回
			if (_currentDialogue == null && !Visible)
			{
				return;
			}
			
			_isEndingDialogue = true;
			
			try
			{
				// 触发结束行为
				if (_currentEntry != null && !string.IsNullOrEmpty(_currentEntry.OnDialogueEndAction))
				{
					EmitSignal(SignalName.DialogueActionTriggered, _currentEntry.OnDialogueEndAction);
				}
				
				// 清除所有UI元素
				ClearChoices();
				_isDisplayingText = false;
				
			// 清理对话数据（在隐藏窗口之前）
			_currentDialogue = null;
			_currentEntry = null;
			_currentEntryIndex = -1;
			
			// 隐藏窗口
				HideWindow();
				
				// 发送结束信号（必须在清理数据之后，但在隐藏窗口之后）
				EmitSignal(SignalName.DialogueEnded);
			}
			catch (System.Exception e)
			{
				GD.PrintErr($"DialogueWindow: 结束对话时发生异常: {e.GetType().Name} - {e.Message}");
				GD.PrintErr($"DialogueWindow: 堆栈: {e.StackTrace}");
				
				// 强制清理
				_currentDialogue = null;
				_currentEntry = null;
				_currentEntryIndex = -1;
				HideWindow();
				
				// 尝试发送结束信号
				try
				{
					EmitSignal(SignalName.DialogueEnded);
				}
				catch
				{
					// 忽略信号发送错误
				}
			}
			finally
			{
				// 重置标志，允许下次对话
				_isEndingDialogue = false;
			}
		}
		
		/// <summary>
		/// 显示窗口
		/// </summary>
		public void ShowWindow()
		{
			Visible = true;
			
			// 连接玩家金币变化信号
			ConnectPlayerGoldSignal();
			
			// 更新金币显示
			UpdateGoldDisplay();
			
			// 确保输入处理启用
			SetProcessInput(true);
			SetProcessUnhandledInput(true);
			SetProcessUnhandledKeyInput(true);
			
			// 设置窗口优先级，确保输入处理优先级
			SetWindowPriority();
			
			// 尝试将窗口移到场景树前面，确保输入处理优先级
			var parent = GetParent();
			if (parent != null)
			{
				// 移动到父节点的最后，这样在输入处理时会最后被调用（但我们可以通过 _Input 优先处理）
				parent.MoveChild(this, parent.GetChildCount() - 1);
			}
		}
		
		/// <summary>
		/// 连接玩家金币变化信号
		/// </summary>
		private void ConnectPlayerGoldSignal()
		{
			// 断开之前的连接
			if (_player != null)
			{
				_player.GoldChanged -= OnPlayerGoldChanged;
			}
			
			// 获取玩家引用
			_player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
			
			// 连接信号（先取消订阅再订阅，确保不重复连接）
			if (_player != null)
			{
				_player.GoldChanged -= OnPlayerGoldChanged;
				_player.GoldChanged += OnPlayerGoldChanged;
			}
		}
		
		/// <summary>
		/// 玩家金币变化回调
		/// </summary>
		private void OnPlayerGoldChanged(int gold)
		{
			UpdateGoldDisplay();
		}
		
		/// <summary>
		/// 更新金币显示
		/// </summary>
		private void UpdateGoldDisplay()
		{
			// 尝试从场景中获取玩家
			if (_player == null)
			{
				_player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
			}
			
			if (_player != null && GoldLabel != null)
			{
				int gold = _player.GetGold();
				GoldLabel.Text = $"金币: {gold}";
			}
		}
		
		/// <summary>
		/// 隐藏窗口
		/// </summary>
		public void HideWindow()
		{
			Visible = false;
			SetProcessInput(false);
			SetProcessUnhandledInput(false);
			ClearChoices();
			_isDisplayingText = false;
			
			// 断开玩家金币变化信号
			if (_player != null)
			{
				_player.GoldChanged -= OnPlayerGoldChanged;
			}
			_player = null;
		}
		
		/// <summary>
		/// 设置窗口优先级，确保输入处理优先级
		/// </summary>
		private void SetWindowPriority()
		{
			// 设置很高的 z_index，确保窗口在最上层
			ZIndex = 1000;
			
			// 确保处理模式为 Always，即使在暂停时也能接收输入
			ProcessMode = ProcessModeEnum.Always;
		}
		
		public override void _Process(double delta)
		{
			// 逐字显示文本
			if (_isDisplayingText && DialogueTextLabel != null && !string.IsNullOrEmpty(_fullText))
			{
				_textDisplayTimer += delta;
				
				if (_textDisplayTimer >= TextSpeed)
				{
					_textDisplayTimer = 0.0;
					_displayedCharCount++;
					
					if (_displayedCharCount <= _fullText.Length)
					{
						try
						{
							DialogueTextLabel.Text = _fullText.Substring(0, _displayedCharCount);
						}
						catch (System.Exception e)
						{
							GD.PrintErr($"DialogueWindow: Substring错误: {e.Message}");
							GD.PrintErr($"DialogueWindow: _fullText长度: {_fullText.Length}, _displayedCharCount: {_displayedCharCount}");
							_isDisplayingText = false;
							DialogueTextLabel.Text = _fullText;
						}
					}
					else
					{
						_isDisplayingText = false;
					}
				}
			}
		}
		
		/// <summary>
		/// 处理输入事件 - 使用 _Input 确保优先级
		/// </summary>
		public override void _Input(InputEvent @event)
		{
			// 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
			var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
			if (itemPopup != null && itemPopup.Visible)
			{
				// 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
				// 直接返回，让弹窗处理（禁用）
				return;
			}
			
			// 首先检查 action（这是最可靠的方式）
			if (@event.IsActionPressed("ui_cancel"))
			{
				// 检查对话是否激活
				if (IsDialogueActive())
				{
					HandleEscKey();
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			
			// 备用检查：直接检查 Keycode（不依赖 action）
			if (@event is InputEventKey keyEvent && keyEvent.Pressed)
			{
				// 使用 Key 枚举进行比较（更可靠）
				bool isEscapeKey = keyEvent.Keycode == Key.Escape;
				
				// 如果 action 检查失败，但确实是 ESC 键，也处理
				if (isEscapeKey)
				{
					// 检查对话是否激活
					if (IsDialogueActive())
					{
						HandleEscKey();
						GetViewport().SetInputAsHandled();
						return;
					}
					else
					{
						GetViewport().SetInputAsHandled();
						return;
					}
				}
			}
			
			// 空格键继续/跳过
			if (@event.IsActionPressed("attack") || @event.IsActionPressed("ui_accept"))
			{
				// 如果对话已激活，处理Space键
				if (IsDialogueActive())
				{
					HandleSpaceKey();
					GetViewport().SetInputAsHandled();
					AcceptEvent();
					return;
				}
				else
				{
					// 即使对话已结束，也要处理Space键，防止它传播到玩家角色
					// 这可以防止在对话结束时按下Space键触发攻击
					GetViewport().SetInputAsHandled();
					AcceptEvent();
					return;
				}
			}
		}
		
		/// <summary>
		/// 处理GUI输入 - 作为备用方法
		/// </summary>
		public override void _GuiInput(InputEvent @event)
		{
			// 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
			var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
			if (itemPopup != null && itemPopup.Visible)
			{
				// 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
				// 直接返回，让弹窗处理（禁用）
				return;
			}
			
			if (@event.IsActionPressed("ui_cancel"))
			{
				if (IsDialogueActive())
				{
					HandleEscKey();
					AcceptEvent();
				}
			}
		}
		
		/// <summary>
		/// 处理未处理的输入 - 作为备用
		/// </summary>
		public override void _UnhandledInput(InputEvent @event)
		{
			// 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
			var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
			if (itemPopup != null && itemPopup.Visible)
			{
				// 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
				// 直接返回，让弹窗处理（禁用）
				return;
			}
			
			// 检查对话是否激活
			if (!IsDialogueActive())
			{
				// 即使对话已结束，也要处理Space键，防止它传播到玩家角色
				// 这可以防止在对话结束时按下Space键触发攻击
				if (@event.IsActionPressed("attack") || @event.IsActionPressed("ui_accept"))
				{
					GetViewport().SetInputAsHandled();
					return;
				}
				return;
			}
			
			// ESC键跳过对话
			if (@event.IsActionPressed("ui_cancel"))
			{
				HandleEscKey();
				GetViewport().SetInputAsHandled();
				return;
			}
			
			// 空格键继续/跳过
			if (@event.IsActionPressed("attack") || @event.IsActionPressed("ui_accept"))
			{
				HandleSpaceKey();
				GetViewport().SetInputAsHandled();
				return;
			}
		}
		
		/// <summary>
		/// 检查对话是否激活
		/// </summary>
		private bool IsDialogueActive()
		{
			bool visible = Visible;
			bool managerExists = DialogueManager.Instance != null;
			bool managerActive = DialogueManager.Instance?.IsDialogueActive ?? false;
			bool hasDialogue = _currentDialogue != null;
			
			return visible && managerExists && managerActive && hasDialogue;
		}
		
		/// <summary>
		/// 处理ESC键
		/// </summary>
		private void HandleEscKey()
		{
			if (_currentDialogue == null)
			{
				GD.PrintErr("DialogueWindow: 当前对话数据为空，无法处理ESC键");
				return;
			}
			
			if (_currentDialogue.CanSkip)
			{
				EndDialogue();
			}
			// 即使不允许跳过，也确保输入被处理，防止其他系统响应
		}
		
		/// <summary>
		/// 处理空格键
		/// </summary>
		private void HandleSpaceKey()
		{
			if (_isDisplayingText)
			{
				// 如果正在显示文本，立即完成显示
				CompleteTextDisplay();
			}
			else
			{
				// 文本已显示完成，继续到下一句
				OnContinueButtonPressed();
			}
		}
	}
}

