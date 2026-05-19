using Godot;

namespace Kuros.Actors.Npc
{
	/// <summary>
	/// 使用 Dialogic 插件的 NPC 交互组件。
	/// 当玩家进入 Area2D 范围并按下交互键时启动 Dialogic Timeline。
	/// 当玩家离开范围时立即结束对话。
	/// </summary>
	public partial class DialogicInteraction : Node2D
	{
		/// <summary>
		/// 要播放的 Dialogic Timeline 路径
		/// 例如："res://dialogic/timeline/B_begin_timeline.dtl"
		/// 或直接填写名称："B_begin_timeline"
		/// </summary>
		[Export] public string TimelinePath { get; set; } = "";

		/// <summary>
		/// 对应的 Dialogic 角色资源路径（.dch 文件）
		/// 例如："res://dialogic/character/Enemy_Normal_guard2_B_begin_gate.dch"
		/// 填写后气泡对话框会跟随 BubbleAnchorPath 指定的节点位置显示
		/// 留空则气泡显示在默认位置
		/// </summary>
		[Export] public string CharacterPath { get; set; } = "";

		/// <summary>
		/// 气泡对话框跟随的锚点节点路径（默认为子节点 Marker2D）
		/// 需要配合 CharacterPath 一起使用
		/// </summary>
		[Export] public NodePath BubbleAnchorPath { get; set; } = "Marker2D";

		/// <summary>
		/// 气泡对话框的偏移量（相对于 BubbleAnchorPath 指定的节点）
		/// 例如：Vector2(0, -50) 会让气泡向上偏移 50 像素
		/// </summary>
		[Export] public Vector2 BubbleAnchorOffset { get; set; } = Vector2.Zero;

		/// <summary>
		/// 交互提示文字
		/// </summary>
		[Export] public string PromptText { get; set; } = "[E] 交互";   //留空时进入area2d范围自动触发对话

		/// <summary>
		/// 是否只触发一次（触发后禁用交互）
		/// </summary>
		[Export] public bool TriggerOnce { get; set; } = false;

		private Area2D? _area;
		private bool _playerInRange = false;
		private bool _hasTriggered = false;
		private Label? _promptLabel;

		public override void _Ready()
		{
			_area = GetNodeOrNull<Area2D>("Area2D");
			if (_area == null)
			{
				GD.PrintErr("DialogicInteraction: 找不到 Area2D 子节点！");
				return;
			}

			_area.AreaEntered += OnAreaEntered;
			_area.AreaExited += OnAreaExited;
			_area.BodyEntered += OnBodyEntered;
			_area.BodyExited += OnBodyExited;

			CreatePromptLabel();
			UpdatePrompt();
		}

		public override void _Process(double delta)
		{
			if (!string.IsNullOrEmpty(PromptText) && _playerInRange && Input.IsActionJustPressed("interact"))
			{
				StartDialogue();
			}
		}

		private void OnAreaEntered(Area2D area)
		{
			if (area.Name == "GrabArea" && area.GetParent()?.IsInGroup("player") == true)
			{
				_playerInRange = true;
				UpdatePrompt();
				if (string.IsNullOrEmpty(PromptText))
					StartDialogue();
			}
		}

		private void OnAreaExited(Area2D area)
		{
			if (area.Name == "GrabArea" && area.GetParent()?.IsInGroup("player") == true)
			{
				_playerInRange = false;
				UpdatePrompt();
				ForceEndDialogue();
			}
		}

		private void OnBodyEntered(Node2D body)
		{
			if (body.IsInGroup("player"))
			{
				_playerInRange = true;
				UpdatePrompt();
				if (string.IsNullOrEmpty(PromptText))
					StartDialogue();
			}
		}

		private void OnBodyExited(Node2D body)
		{
			if (body.IsInGroup("player"))
			{
				_playerInRange = false;
				UpdatePrompt();
				ForceEndDialogue();
			}
		}

		private void StartDialogue()
		{
			if (string.IsNullOrEmpty(TimelinePath))
			{
				GD.PrintErr("DialogicInteraction: TimelinePath 未设置！请在编辑器中填写 Timeline 路径。");
				return;
			}

			if (TriggerOnce && _hasTriggered)
				return;

			_hasTriggered = true;
			UpdatePrompt();

			var dialogic = GetNode("/root/Dialogic");
			if (dialogic == null)
			{
				GD.PrintErr("DialogicInteraction: 找不到 Dialogic autoload！请确认 project.godot 中已启用 Dialogic。");
				return;
			}

			// 连接 timeline_ended 信号
			if (!dialogic.IsConnected("timeline_ended", new Callable(this, MethodName.OnTimelineEnded)))
			{
				dialogic.Connect("timeline_ended", new Callable(this, MethodName.OnTimelineEnded));
			}

			// 启动对话，start() 返回布局节点（Style 的根场景）
			var layoutVariant = dialogic.Call("start", TimelinePath);

			// 将 Marker2D 注册到布局节点，让气泡对话框定位到 Marker2D
			// 必须在 start() 之后，且用 CallDeferred 等布局节点就绪后再注册
			if (!string.IsNullOrEmpty(CharacterPath) && layoutVariant.AsGodotObject() is Node layoutNode)
			{
				var anchor = GetNodeOrNull(BubbleAnchorPath);
				if (anchor != null)
				{
					// 如果有偏移，创建一个虚拟的偏移容器节点
					Node anchorWithOffset = anchor;
					if (BubbleAnchorOffset != Vector2.Zero && anchor is Node2D anchor2D)
					{
						var offsetContainer = new Node2D();
						offsetContainer.Name = "BubbleAnchorOffsetContainer";
						offsetContainer.GlobalPosition = anchor2D.GlobalPosition + BubbleAnchorOffset;
						AddChild(offsetContainer);
						anchorWithOffset = offsetContainer;
					}

					// register_character(角色资源路径, 锚点节点) 告诉 Dialogic 气泡在哪显示
					layoutNode.CallDeferred("register_character", CharacterPath, anchorWithOffset);
				}
				else
				{
					GD.PrintErr($"DialogicInteraction: 找不到锚点节点 '{BubbleAnchorPath}'，气泡将显示在默认位置。");
				}
			}
		}

		/// <summary>
		/// 强制结束当前对话（玩家离开范围时调用）
		/// </summary>
		private void ForceEndDialogue()
		{
			var dialogic = GetNode("/root/Dialogic");
			if (dialogic == null) return;

			// 只有对话正在进行时才调用 end_timeline
			var currentTimeline = dialogic.Get("current_timeline");
			if (currentTimeline.AsGodotObject() != null)
			{
				dialogic.Call("end_timeline");
			}

			// 清理虚拟偏移容器
			var offsetContainer = GetNodeOrNull("BubbleAnchorOffsetContainer");
			if (offsetContainer != null)
			{
				offsetContainer.QueueFree();
			}
		}

		private void OnTimelineEnded()
		{
			var dialogic = GetNode("/root/Dialogic");
			if (dialogic != null && dialogic.IsConnected("timeline_ended", new Callable(this, MethodName.OnTimelineEnded)))
			{
				dialogic.Disconnect("timeline_ended", new Callable(this, MethodName.OnTimelineEnded));
			}

			// 清理虚拟偏移容器
			var offsetContainer = GetNodeOrNull("BubbleAnchorOffsetContainer");
			if (offsetContainer != null)
			{
				offsetContainer.QueueFree();
			}

			if (TriggerOnce)
			{
				if (_area != null)
					_area.SetDeferred(Area2D.PropertyName.Monitoring, false);
			}
			else
			{
				_hasTriggered = false;
			}

			UpdatePrompt();
		}

		private void CreatePromptLabel()
		{
			_promptLabel = new Label();
			_promptLabel.Text = PromptText;
			_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_promptLabel.Visible = false;
			_promptLabel.Position = new Vector2(-60, -90);
			_promptLabel.AddThemeFontSizeOverride("font_size", 24);
			AddChild(_promptLabel);
		}

		private void UpdatePrompt()
		{
			if (_promptLabel == null) return;
			bool canInteract = _playerInRange && !(TriggerOnce && _hasTriggered);
			_promptLabel.Visible = canInteract;
		}
	}
}
