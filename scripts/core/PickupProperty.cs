using Godot;

namespace Kuros.Core
{
	/// <summary>
	/// 拾取属性基类 - 可以被角色拾取的物品
	/// </summary>
	public abstract partial class PickupProperty : Node2D
	{
		private Area2D? _triggerArea;
		private GameActor? _focusedActor;
		private bool _isPicked;
		
		/// <summary>
		/// 指示當前拾取是否是手動觸發（如按 F 鍵拾取）
		/// </summary>
		protected bool IsManualPickup { get; private set; }

		public override void _Ready()
		{
			base._Ready();
			SetupTriggerArea();
			SetProcess(true);
		}

		public override void _ExitTree()
		{
			base._ExitTree();
			if (_triggerArea != null)
			{
				_triggerArea.BodyEntered -= OnBodyEntered;
				_triggerArea.BodyExited -= OnBodyExited;
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			// 注意：拾取逻辑已移至 PlayerItemInteractionComponent 统一处理
			// 这里不再监听 take_up 输入，避免多个物品同时被拾取的问题
			// PickupProperty 只负责追踪哪个 Actor 在范围内（_focusedActor）
			
			if (_isPicked || _focusedActor == null)
			{
				return;
			}

			if (!GodotObject.IsInstanceValid(_focusedActor))
			{
				_focusedActor = null;
				return;
			}
		}
		
		/// <summary>
		/// 供外部调用的拾取方法（如 PlayerItemInteractionComponent）
		/// </summary>
		public bool TryPickupByActor(GameActor actor)
		{
			if (_isPicked)
			{
				return false;
			}
			
			_isPicked = true;
			IsManualPickup = true;
			OnPicked(actor);
			QueueFree();
			return true;
		}

		/// <summary>
		/// 设置触发区域并连接信号
		/// </summary>
		private void SetupTriggerArea()
		{
			_triggerArea = GetNodeOrNull<Area2D>("TriggerArea");
			if (_triggerArea == null)
			{
				GD.PrintErr($"{Name}: 未找到 TriggerArea 节点，无法进行拾取检测");
				return;
			}

			_triggerArea.BodyEntered += OnBodyEntered;
			_triggerArea.BodyExited += OnBodyExited;
		}

		/// <summary>
		/// 当物体进入触发区域时调用
		/// </summary>
		private void OnBodyEntered(Node2D body)
		{
			if (body is GameActor actor)
			{
				// 只有在没有聚焦 actor 时才设置新的，避免多人模式下焦点被抢夺
				if (_focusedActor == null)
				{
					_focusedActor = actor;
				}
			}
		}

		/// <summary>
		/// 当物体离开触发区域时调用
		/// </summary>
		private void OnBodyExited(Node2D body)
		{
			if (_focusedActor != null && body == _focusedActor)
			{
				_focusedActor = null;
			}
		}

		/// <summary>
		/// 当被拾取时调用
		/// </summary>
		protected virtual void OnPicked(GameActor actor)
		{
			GD.Print($"{Name} picked up by {actor.Name}");
		}
	}
}

