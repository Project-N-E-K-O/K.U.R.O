using Godot;
using Kuros.Data;
using Kuros.UI;
using Kuros.Managers;

namespace Kuros.Managers
{
	/// <summary>
	/// 对话管理器 - 负责管理对话系统的全局状态和逻辑
	/// 需要在project.godot中配置为autoload
	/// </summary>
	public partial class DialogueManager : Node
	{
		public static DialogueManager Instance { get; private set; } = null!;
		
		private const string DIALOGUE_UI_PATH = "res://scenes/ui/windows/DialogueWindow.tscn";
		
		private DialogueWindow? _dialogueWindow;
		private DialogueData? _currentDialogue;
		private bool _isDialogueActive = false;
		
		// 信号
		[Signal] public delegate void DialogueStartedEventHandler(string dialogueId);
		[Signal] public delegate void DialogueEndedEventHandler(string dialogueId);
		[Signal] public delegate void DialogueActionTriggeredEventHandler(string actionId);
		
		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}
			
			Instance = this;
			
			// 确保 DialogueManager 也能接收输入，作为备用
			SetProcessInput(true);
			SetProcessUnhandledInput(true);
		}
		
		/// <summary>
		/// 处理输入 - 作为备用，确保对话系统能接收ESC键
		/// </summary>
		public override void _Input(InputEvent @event)
		{
			// 检查物品获得弹窗是否打开（ESC键在弹窗显示时被完全禁用）
			var itemPopup = UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
			if (itemPopup != null && itemPopup.Visible)
			{
				// 物品获得弹窗打开时，ESC键被完全禁用，这里不处理
				// 直接返回，让弹窗处理（禁用）
				return;
			}
			
			// 只有在对话激活时才处理
			if (!_isDialogueActive)
			{
				return;
			}
			
			// ESC键跳过对话
			if (@event.IsActionPressed("ui_cancel"))
			{
				if (_currentDialogue != null && _currentDialogue.CanSkip)
				{
					EndDialogue();
					GetViewport().SetInputAsHandled();
				}
				else
				{
					GetViewport().SetInputAsHandled();
				}
			}
		}
		
		/// <summary>
		/// 开始对话
		/// </summary>
		/// <param name="dialogue">对话数据资源</param>
		public void StartDialogue(DialogueData dialogue)
		{
			if (dialogue == null)
			{
				GD.PrintErr("DialogueManager: 对话数据为空！");
				return;
			}
			
			if (_isDialogueActive)
			{
				EndDialogue();
			}
			
			_currentDialogue = dialogue;
			_isDialogueActive = true;
			
			// 加载对话UI
			LoadDialogueWindow();
			
			if (_dialogueWindow != null)
			{
				// 连接信号（确保不重复连接）
				if (!_dialogueWindow.IsConnected(DialogueWindow.SignalName.DialogueEnded, new Callable(this, MethodName.OnDialogueEnded)))
				{
					_dialogueWindow.DialogueEnded += OnDialogueEnded;
				}
				
				if (!_dialogueWindow.IsConnected(DialogueWindow.SignalName.DialogueActionTriggered, new Callable(this, MethodName.OnDialogueActionTriggered)))
				{
					_dialogueWindow.DialogueActionTriggered += OnDialogueActionTriggered;
				}
				
				// 开始显示对话
				_dialogueWindow.StartDialogue(dialogue);
				
				// 发送开始信号
				EmitSignal(SignalName.DialogueStarted, dialogue.DialogueId);
			}
		}
		
		/// <summary>
		/// 结束对话（由外部调用，如NPCInteraction）
		/// </summary>
		public void EndDialogue()
		{
			if (!_isDialogueActive)
			{
				return;
			}
			
			// 保存对话ID
			string dialogueId = _currentDialogue?.DialogueId ?? "";
			
			// 先标记为非激活状态，防止重复调用
			_isDialogueActive = false;
			
			// 隐藏对话窗口（窗口会发送信号，OnDialogueEnded会被调用）
			if (_dialogueWindow != null && IsInstanceValid(_dialogueWindow))
			{
				_dialogueWindow.EndDialogue();
				// 注意：不要在这里清理数据，让OnDialogueEnded来处理
				// 这样可以避免重复清理和重复发送信号
			}
			else
			{
				GD.PrintErr("DialogueManager: 对话窗口无效或为空，直接清理状态");
				// 如果窗口无效，直接清理
				_currentDialogue = null;
				EmitSignal(SignalName.DialogueEnded, dialogueId);
			}
		}
		
		/// <summary>
		/// 加载对话窗口
		/// </summary>
		private void LoadDialogueWindow()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("DialogueManager: UIManager未初始化！请在project.godot中将UIManager添加为autoload。");
				return;
			}
			
			// 如果窗口已加载，直接使用
			_dialogueWindow = UIManager.Instance.GetUI<DialogueWindow>("DialogueWindow");
			
			if (_dialogueWindow == null)
			{
				// 加载新窗口
				_dialogueWindow = UIManager.Instance.LoadUI<DialogueWindow>(
					DIALOGUE_UI_PATH,
					UILayer.Menu,
					"DialogueWindow"
				);
			}
		}
		
		/// <summary>
		/// 卸载对话窗口
		/// </summary>
		private void UnloadDialogueWindow()
		{
			if (UIManager.Instance != null && _dialogueWindow != null)
			{
				UIManager.Instance.UnloadUI("DialogueWindow");
				_dialogueWindow = null;
			}
		}
		
		/// <summary>
		/// 对话结束回调（由DialogueWindow调用）
		/// </summary>
		private void OnDialogueEnded()
		{
			// 如果对话仍然标记为激活，清理状态
			if (_isDialogueActive)
			{
				// 先保存对话ID，然后再清理
				string dialogueId = _currentDialogue?.DialogueId ?? "";
				
				// 标记为非激活状态
				_isDialogueActive = false;
				
				// 清理对话数据
				_currentDialogue = null;
				
				// 断开信号连接，避免重复调用
				if (_dialogueWindow != null && IsInstanceValid(_dialogueWindow))
				{
					if (_dialogueWindow.IsConnected(DialogueWindow.SignalName.DialogueEnded, new Callable(this, MethodName.OnDialogueEnded)))
					{
						_dialogueWindow.DialogueEnded -= OnDialogueEnded;
					}
				}
				
				// 发送结束信号（给外部监听者，如NPCInteraction）
				EmitSignal(SignalName.DialogueEnded, dialogueId);
			}
		}
		
		/// <summary>
		/// 对话行为触发回调
		/// </summary>
		private void OnDialogueActionTriggered(string actionId)
		{
			EmitSignal(SignalName.DialogueActionTriggered, actionId);
			
			// 可以在这里处理特定的行为
			HandleDialogueAction(actionId);
		}
		
		/// <summary>
		/// 处理对话行为
		/// </summary>
		private void HandleDialogueAction(string actionId)
		{
			// 这里可以添加具体的行为处理逻辑
			// 例如：给予物品、完成任务、触发事件等
			
			// 示例：处理一些常见行为
			if (actionId.StartsWith("give_item:"))
			{
				// 给予物品
				string itemId = actionId.Substring("give_item:".Length);
				// TODO: 实现物品给予逻辑
			}
			else if (actionId.StartsWith("complete_quest:"))
			{
				// 完成任务
				string questId = actionId.Substring("complete_quest:".Length);
				// TODO: 实现任务完成逻辑
			}
		}
		
		/// <summary>
		/// 检查是否有对话正在进行
		/// </summary>
		public bool IsDialogueActive => _isDialogueActive;
		
		/// <summary>
		/// 获取当前对话数据
		/// </summary>
		public DialogueData? CurrentDialogue => _currentDialogue;
	}
}

