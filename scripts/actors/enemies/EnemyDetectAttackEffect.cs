using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// 挂载在敌人节点上，默认禁用敌人 AI（与 EnemySpawnConsole.DisableEnemyAI 原理相同）。
	/// 
	/// 触发恢复 AI 的条件（二选一，满足其一即生效）：
	///   1. 玩家的 GrabArea 进入检测范围，且玩家处于攻击状态（Attack）
	///   2. 敌人受到任何伤害
	/// 
	/// 这是一次性功能：AI 恢复后此组件自动销毁。
	/// </summary>
	public partial class EnemyDetectAttackEffect : Node
	{
		/// <summary>玩家 GrabArea 检测半径（像素，基于父节点未缩放坐标）。</summary>
		[Export(PropertyHint.Range, "0,3000,10")] public float DetectionRadius { get; set; } = 800f;

		private GameActor? _parentActor;
		private Area2D? _detectionArea;
		private bool _aiEnabled = false;
		private bool _playerInRange = false;

		// 缓存三个 AI 检测区域
		private Area2D? _controllerDetectionArea;
		private Area2D? _attackDetectionArea;
		private Area2D? _attackArea;

		// 保存原始 CollisionMask，用于恢复
		private uint _savedControllerMask;
		private uint _savedAttackDetectionMask;
		private uint _savedAttackAreaMask;

		private static readonly StringName AttackStateName = new("Attack");

		public override void _Ready()
		{
			_parentActor = GetParent() as GameActor;
			if (_parentActor == null)
			{
				GD.PushWarning("[EnemyDetectAttackEffect] 父节点不是 GameActor，脚本禁用。");
				SetProcess(false);
				return;
			}

			// 缓存检测区域并保存原始 CollisionMask
			_controllerDetectionArea = _parentActor.GetNodeOrNull<Area2D>("Sprite2D/ControllerDetectionArea");
			_attackDetectionArea     = _parentActor.GetNodeOrNull<Area2D>("Sprite2D/AttackDetectionArea");
			_attackArea              = _parentActor.GetNodeOrNull<Area2D>("Sprite2D/AttackArea");

			if (_controllerDetectionArea != null)
				_savedControllerMask = _controllerDetectionArea.CollisionMask;
			if (_attackDetectionArea != null)
				_savedAttackDetectionMask = _attackDetectionArea.CollisionMask;
			if (_attackArea != null)
				_savedAttackAreaMask = _attackArea.CollisionMask;

			// 默认禁用 AI
			ApplyDisableAI();

			// 订阅受伤事件（敌人受伤立即恢复 AI）
			_parentActor.DamageTaken += OnDamageTaken;

			// 创建玩家 GrabArea 检测区域
			CreateDetectionArea();
		}

		public override void _ExitTree()
		{
			if (_parentActor != null)
				_parentActor.DamageTaken -= OnDamageTaken;
		}

		public override void _Process(double delta)
		{
			if (_aiEnabled || !_playerInRange) return;

			// 玩家在范围内时，逐帧检查是否处于攻击状态
			var players = GetTree().GetNodesInGroup("player");
			foreach (var node in players)
			{
				if (node is GameActor playerActor &&
				    playerActor.StateMachine?.CurrentState?.Name == AttackStateName)
				{
					EnableAI();
					return;
				}
			}
		}

		// ── 事件处理 ──────────────────────────────────────────────

		private void OnDamageTaken(int damage)
		{
			if (_aiEnabled) return;
			EnableAI();
		}

		private void OnAreaEntered(Area2D area)
		{
			if (area.Name == "GrabArea" && area.GetParent()?.IsInGroup("player") == true)
				_playerInRange = true;
		}

		private void OnAreaExited(Area2D area)
		{
			if (area.Name == "GrabArea" && area.GetParent()?.IsInGroup("player") == true)
				_playerInRange = false;
		}

		// ── AI 控制 ──────────────────────────────────────────────

		/// <summary>
		/// 将三个检测区域的 CollisionMask 设为 0，使敌人无法感知任何物体。
		/// </summary>
		private void ApplyDisableAI()
		{
			if (_controllerDetectionArea != null)
				_controllerDetectionArea.CollisionMask = 0;
			if (_attackDetectionArea != null)
				_attackDetectionArea.CollisionMask = 0;
			if (_attackArea != null)
				_attackArea.CollisionMask = 0;
		}

		/// <summary>
		/// 恢复三个检测区域的原始 CollisionMask，使敌人恢复正常 AI 行为。
		/// 恢复后通知 Dialogic 推进时间轴，销毁此组件（一次性功能）。
		/// </summary>
		private void EnableAI()
		{
			_aiEnabled = true;

			if (_controllerDetectionArea != null && GodotObject.IsInstanceValid(_controllerDetectionArea))
				_controllerDetectionArea.CollisionMask = _savedControllerMask;
			if (_attackDetectionArea != null && GodotObject.IsInstanceValid(_attackDetectionArea))
				_attackDetectionArea.CollisionMask = _savedAttackDetectionMask;
			if (_attackArea != null && GodotObject.IsInstanceValid(_attackArea))
				_attackArea.CollisionMask = _savedAttackAreaMask;

			// 通知 Dialogic 推进时间轴到下一句话
			NotifyDialogicProgress();

			QueueFree();
		}

		/// <summary>
		/// 向 Dialogic 发送信号，通知时间轴推进到下一句话。
		/// 前提：时间轴中已配置 "Wait for Signal" 事件，监听 "dialogic_ai_enabled" 信号。
		/// </summary>
		private void NotifyDialogicProgress()
		{
			try
			{
				var dialogic = GetTree().Root.GetNodeOrNull("/root/Dialogic");
				if (dialogic == null)
					return;

				// 方式 1：发出自定义信号（需要在 Dialogic 时间轴中配置 "Wait for Signal"）
				if (dialogic.HasSignal("dialogic_ai_enabled"))
				{
					dialogic.EmitSignal("dialogic_ai_enabled");
					GD.Print("[EnemyDetectAttackEffect] Dialogic 信号已发送：dialogic_ai_enabled");
				}

				// 方式 2：直接调用 Dialogic 的推进方法（如果有的话）
				// 如果上面的方式不工作，可尝试直接推进：
				// if (dialogic.HasMethod("next_event"))
				// {
				// 	dialogic.Call("next_event");
				// 	GD.Print("[EnemyDetectAttackEffect] Dialogic 已推进到下一句话");
				// }
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"[EnemyDetectAttackEffect] 通知 Dialogic 时出错：{ex.Message}");
			}
		}

		// ── 检测区域创建 ──────────────────────────────────────────

		/// <summary>
		/// 动态创建玩家 GrabArea 检测的 Area2D。
		/// 该 Area2D 会通过场景树继承最近 Node2D 祖先（即敌人）的变换，随敌人移动。
		/// </summary>
		private void CreateDetectionArea()
		{
			_detectionArea = new Area2D
			{
				Name = "PlayerDetectArea",
				CollisionLayer = 0,
				CollisionMask  = 4   // Layer 3（玩家 GrabArea 所在碰撞层，与 Dialogic_Interaction 一致）
			};

			var shape = new CollisionShape2D
			{
				Shape = new CircleShape2D { Radius = DetectionRadius }
			};

			_detectionArea.AddChild(shape);
			AddChild(_detectionArea);

			_detectionArea.AreaEntered += OnAreaEntered;
			_detectionArea.AreaExited  += OnAreaExited;
		}
	}
}
