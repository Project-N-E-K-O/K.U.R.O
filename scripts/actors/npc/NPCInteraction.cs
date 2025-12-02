using Godot;
using Kuros.Data;
using Kuros.Core;
using Kuros.Managers;

namespace Kuros.Actors.NPC
{
	/// <summary>
	/// NPC交互组件 - 处理玩家与NPC的交互逻辑
	/// 可以附加到任何GameActor节点上，使其成为可交互的NPC
	/// </summary>
	public partial class NPCInteraction : Node2D
	{
		[ExportCategory("Interaction")]
		[Export] public DialogueData? DialogueData { get; set; }
		[Export] public float InteractionRange { get; set; } = 100.0f;
		[Export] public bool ShowInteractionPrompt { get; set; } = true;
		
		[ExportCategory("Visual")]
		[Export] public NodePath? PromptLabelPath { get; set; }
		[Export] public string InteractionPromptText { get; set; } = "按 [E] 交互";
		
		private Area2D? _interactionArea;
		private Label? _promptLabel;
		private GameActor? _owner;
		private GameActor? _playerInRange;
		private bool _isInteracting = false;
		private bool _hasTriedLoadDialogue = false; // 避免重复尝试加载
		
		public override void _Ready()
		{
			_owner = GetParent<GameActor>();
			
			if (_owner == null)
			{
				GD.PrintErr("NPCInteraction: 父节点不是GameActor！");
				return;
			}
			
			// 延迟检查对话数据，确保场景完全加载
			CallDeferred(MethodName.InitializeNPCInteraction);
		}
		
		private void InitializeNPCInteraction()
		{
			// 如果对话数据为空，创建默认对话数据
			if (DialogueData == null && !_hasTriedLoadDialogue)
			{
				_hasTriedLoadDialogue = true;
				CreateDefaultDialogue();
			}
			
			if (DialogueData == null)
			{
				GD.PrintErr($"NPCInteraction: ⚠ 警告 - NPC {_owner?.Name} 没有设置对话数据！");
				GD.PrintErr("NPCInteraction: 请在Godot编辑器中为NPCInteraction组件设置DialogueData资源。");
			}
			
			// 创建交互区域
			CreateInteractionArea();
			
			// 创建提示标签
			CreatePromptLabel();
			
			// 如果指定了路径，使用指定的标签
			if (PromptLabelPath != null && PromptLabelPath.GetNameCount() > 0)
			{
				_promptLabel = GetNodeOrNull<Label>(PromptLabelPath);
			}
			
			// 初始化提示可见性
			UpdatePromptVisibility();
		}
		
		/// <summary>
		/// 创建交互区域
		/// </summary>
		private void CreateInteractionArea()
		{
			_interactionArea = new Area2D();
			_interactionArea.Name = "InteractionArea";
			_interactionArea.Monitoring = true;
			_interactionArea.Monitorable = false;
			_interactionArea.CollisionLayer = 0;
			_interactionArea.CollisionMask = 1; // 检测第1层（玩家层）
			
			// 创建碰撞形状
			var collisionShape = new CollisionShape2D();
			var circleShape = new CircleShape2D();
			circleShape.Radius = InteractionRange;
			collisionShape.Shape = circleShape;
			
			_interactionArea.AddChild(collisionShape);
			AddChild(_interactionArea);
			
			// 连接信号
			_interactionArea.BodyEntered += OnBodyEntered;
			_interactionArea.BodyExited += OnBodyExited;
		}
		
		/// <summary>
		/// 创建提示标签
		/// </summary>
		private void CreatePromptLabel()
		{
			if (!ShowInteractionPrompt)
				return;
			
			// 创建Control节点作为容器，用于在世界坐标中显示UI
			var control = new Control();
			control.Name = "PromptContainer";
			control.MouseFilter = Control.MouseFilterEnum.Ignore;
			control.ProcessMode = ProcessModeEnum.Always;
			
			_promptLabel = new Label();
			_promptLabel.Name = "InteractionPrompt";
			_promptLabel.Text = InteractionPromptText;
			_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_promptLabel.VerticalAlignment = VerticalAlignment.Center;
			_promptLabel.Visible = false;
			
			// 设置样式
			var styleBox = new StyleBoxFlat();
			styleBox.BgColor = new Color(0, 0, 0, 0.7f);
			styleBox.CornerRadiusTopLeft = 5;
			styleBox.CornerRadiusTopRight = 5;
			styleBox.CornerRadiusBottomLeft = 5;
			styleBox.CornerRadiusBottomRight = 5;
			styleBox.ContentMarginLeft = 10;
			styleBox.ContentMarginTop = 5;
			styleBox.ContentMarginRight = 10;
			styleBox.ContentMarginBottom = 5;
			_promptLabel.AddThemeStyleboxOverride("normal", styleBox);
			
			// 设置字体大小
			var fontSize = 16;
			_promptLabel.AddThemeFontSizeOverride("font_size", fontSize);
			
			// 设置标签大小和位置
			_promptLabel.CustomMinimumSize = new Vector2(150, 30);
			_promptLabel.Position = new Vector2(-75, 0); // 居中
			
			control.AddChild(_promptLabel);
			AddChild(control);
			
			// 设置容器位置（在NPC上方）
			control.Position = new Vector2(0, -80);
		}
		
		/// <summary>
		/// 有物体进入交互区域
		/// </summary>
		private void OnBodyEntered(Node2D body)
		{
			if (body is GameActor actor)
			{
				if (actor.IsInGroup("player"))
				{
					// 只有在没有聚焦玩家时才设置新的，避免多人模式下焦点被抢夺
					if (_playerInRange == null)
					{
						_playerInRange = actor;
						UpdatePromptVisibility();
					}
				}
			}
		}
		
		/// <summary>
		/// 有物体离开交互区域
		/// </summary>
		private void OnBodyExited(Node2D body)
		{
			if (body == _playerInRange)
			{
				_playerInRange = null;
				UpdatePromptVisibility();
			}
		}
		
		/// <summary>
		/// 更新提示标签的可见性
		/// </summary>
		private void UpdatePromptVisibility()
		{
			if (_promptLabel != null)
			{
				bool shouldShow = _playerInRange != null && !_isInteracting && DialogueData != null;
				_promptLabel.Visible = shouldShow;
			}
		}
		
		public override void _Process(double delta)
		{
			// 如果对话数据仍然为空，创建默认对话数据（可能在_Ready时场景属性还未覆盖）
			if (DialogueData == null && !_hasTriedLoadDialogue)
			{
				_hasTriedLoadDialogue = true;
				CreateDefaultDialogue();
				UpdatePromptVisibility();
			}
			
			// 检查玩家是否在范围内并按下交互键
			if (_playerInRange != null && !_isInteracting && DialogueData != null)
			{
				if (Input.IsActionJustPressed("interact"))
				{
					StartInteraction();
				}
			}
			
			// 提示标签位置已经在CreatePromptLabel中设置，不需要更新
		}
		
		
		/// <summary>
		/// 创建默认对话数据（当资源文件加载失败时使用）
		/// </summary>
		private void CreateDefaultDialogue()
		{
			try
			{
				// 创建对话数据
				var dialogueData = new DialogueData();
				dialogueData.DialogueId = "example_villager_dialogue";
				dialogueData.DialogueName = "村民对话";
				dialogueData.StartEntryIndex = 0;
				dialogueData.CanSkip = true;
				
				// 创建第一个对话条目
				var entry1 = new DialogueEntry();
				entry1.SpeakerName = "村民";
				entry1.Text = "你好，旅行者！欢迎来到我们的村庄。\n这里最近出现了一些怪物，请小心。";
				entry1.AutoAdvance = false;
				
				// 创建选项
				var choice1 = new DialogueChoice();
				choice1.Text = "了解更多信息";
				choice1.NextEntryIndex = 1;
				choice1.OnSelectedAction = "";
				
				var choice2 = new DialogueChoice();
				choice2.Text = "谢谢，再见";
				choice2.NextEntryIndex = -1;
				choice2.OnSelectedAction = "";
				
				// 使用非泛型 Array 避免导出时的序列化问题
				entry1.Choices = new Godot.Collections.Array { choice1, choice2 };
				
				// 创建第二个对话条目
				var entry2 = new DialogueEntry();
				entry2.SpeakerName = "村民";
				entry2.Text = "如果你需要帮助，随时可以来找我。\n我会告诉你一些有用的信息。";
				entry2.AutoAdvance = true;
				entry2.NextEntryIndex = 2;
				
				// 创建第三个对话条目
				var entry3 = new DialogueEntry();
				entry3.SpeakerName = "村民";
				entry3.Text = "这些怪物通常出现在村庄的东边。\n如果你要去那里，记得带上足够的装备。\n祝你好运！";
				entry3.AutoAdvance = false;
				
				// 使用非泛型 Array 避免导出时的序列化问题
				dialogueData.Entries = new Godot.Collections.Array { entry1, entry2, entry3 };
				
				DialogueData = dialogueData;
			}
			catch (System.Exception e)
			{
				GD.PrintErr($"NPCInteraction: 创建默认对话数据失败: {e.GetType().Name} - {e.Message}");
				GD.PrintErr($"NPCInteraction: 堆栈: {e.StackTrace}");
			}
		}
		
		/// <summary>
		/// 开始交互
		/// </summary>
		public void StartInteraction()
		{
			if (_isInteracting || DialogueData == null)
				return;
			
			if (DialogueManager.Instance == null)
			{
				GD.PrintErr("NPCInteraction: DialogueManager未初始化！请在project.godot中将DialogueManager添加为autoload。");
				return;
			}
			
			_isInteracting = true;
			UpdatePromptVisibility();
			
			// 注意：不需要手动禁用玩家输入
			// 玩家状态机会自动检查 DialogueManager.IsDialogueActive
			// 这样可以在阻止移动和攻击的同时，保留ESC和Space键给对话系统使用
			
			// 连接对话结束信号
			if (!DialogueManager.Instance.IsConnected(DialogueManager.SignalName.DialogueEnded, new Callable(this, MethodName.OnDialogueEnded)))
			{
				DialogueManager.Instance.DialogueEnded += OnDialogueEnded;
			}
			
			// 开始对话
			DialogueManager.Instance.StartDialogue(DialogueData);
		}
		
		/// <summary>
		/// 对话结束回调
		/// </summary>
		private void OnDialogueEnded(string dialogueId)
		{
			_isInteracting = false;
			UpdatePromptVisibility();
			
			// 注意：不需要手动恢复玩家输入
			// 玩家状态机会自动检查 DialogueManager.IsDialogueActive
			// 当对话结束时，状态机会自动恢复正常的输入处理
			
			// 断开信号连接
			if (DialogueManager.Instance != null)
			{
				if (DialogueManager.Instance.IsConnected(DialogueManager.SignalName.DialogueEnded, new Callable(this, MethodName.OnDialogueEnded)))
				{
					DialogueManager.Instance.DialogueEnded -= OnDialogueEnded;
				}
			}
		}
		
		/// <summary>
		/// 设置对话数据
		/// </summary>
		public void SetDialogueData(DialogueData dialogue)
		{
			DialogueData = dialogue;
			UpdatePromptVisibility();
		}
	}
}

