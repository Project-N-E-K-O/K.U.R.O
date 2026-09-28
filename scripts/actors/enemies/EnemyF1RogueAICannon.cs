using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// F1 炮台（Enemy_F1_rogueAI_Cannon 专用）：固定在机械桩（滑槽）上的炮台。
	///
	/// **朝向完全靠动画**：这套骨架的 `d_*` 是美术手绘的镜像孪生（朝向左），`turn`/`d_turn` 片子自己完成翻转，
	/// 所以引擎侧**绝不能写 `SpineSprite.scale.x`**（会双重镜像）——场景上的 `LockFacing = true` 是必要条件
	/// （它是"引擎不会去翻 scale"的唯一保证：`GameActor.FlipFacing` 会被它拦下）。本脚本因此只做四件事：
	///   1. 初始朝向：直接写 `FacingRight`（`ApplyFacing` 是私有的，而 `FlipFacing` 被 LockFacing 拦着）；
	///   2. 朝向提交时镜像"前向锚点"（开火判定形状 + 出膛特效 Marker 的 local X 符号）——
	///      否则转身后判定/出膛点还留在旧的一侧；
	///   3. 开火门：转身期间不起新招（主闸其实是那个前向矩形：转身中玩家按定义不在里面）；
	///   4. 钉在桩上：拒绝一切强制位移；Attack 全程启用"沿轨保持距离"。
	///
	/// 移动由滑槽负责：场景里挂 <see cref="RailChaseMovement"/> 但长期 `ExternalDrive = true`
	/// （只保留它的每帧硬钳，不许它自己追人），位移只在 Attack 状态由
	/// <see cref="RailKeepDistanceBehavior"/> 与攻击状态提交。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAICannon : SampleEnemy
	{
		[ExportCategory("Turret 炮台")]
		/// <summary>前向锚点：这些节点的 local X 符号会随朝向镜像（开火判定形状等）。
		/// **这是本类唯一的配置入口**——转身组件 / 沿轨保持距离组件 / 滑槽追击都按**类型**在子节点里找
		/// （改名、挪层级都不用同步路径）；开局朝向由转身组件第一帧按玩家侧直接提交，不需要配。
		/// 注意不要往清单里放：SpineSprite（d_* 是手绘镜像，引擎再翻就是双重镜像）、
		/// 炮口 marker（它挂在 SpineBoneNode2 下，由 <c>SpineBoneAnchor</c> 按骨骼矩阵自动镜像）。</summary>
		[Export] public Godot.Collections.Array<NodePath> ForwardAnchorPaths { get; set; } = new()
		{
			new("Sprite2D/AttackArea/CollisionShape2D"),
		};

		private readonly System.Collections.Generic.List<(Node2D Node, float OffsetX)> _forwardAnchors = new();
		private EnemyF1RogueAITurretFacingController? _facing;
		private RailKeepDistanceBehavior? _keepDistance;
		private RailChaseMovement? _railChase;

		public override void _Ready()
		{
			base._Ready();

			ResolveNodes();

			// ② 记录前向锚点的初始 X，并按当前朝向镜像一次
			foreach (var path in ForwardAnchorPaths)
			{
				var node = path.IsEmpty ? null : GetNodeOrNull<Node2D>(path);
				if (node == null)
				{
					GD.PushWarning($"{Name}: 前向锚点 {path} 未找到，转身后不会镜像");
					continue;
				}
				_forwardAnchors.Add((node, node.Position.X));
			}
			MirrorForwardAnchors(FacingRight);
		}

		public override void _ExitTree()
		{
			if (_facing != null && GodotObject.IsInstanceValid(_facing))
				_facing.FacingCommitted -= OnFacingCommitted;
			base._ExitTree();
		}

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			// 刷新基类的玩家引用：它只在"检测范围查询"里更新，而本炮台把 RailChaseMovement 的追击关掉了
			// （ExternalDrive=true，只留硬钳）→ 没有别的系统会去查 → 不在这里查一次，本炮台的攻击控制器/
			// 限位类逻辑会永远看不到玩家（PlayerTarget 恒 null）。
			IsPlayerWithinDetectionRange();

			// 沿轨维持间距：**不只是 Attack 期间**——玩家在攻击矩形外时也要自己靠过去，否则没人驱动它，
			// 它会原地站着既不开火也不追（磁铁臂是靠自己的相位机驱动才没有这个问题）。
			// 受击/冻结/眩晕/死亡期间不接管（与机械侧"被击不驱动移动"同一约定）。
			if (_keepDistance != null && GodotObject.IsInstanceValid(_keepDistance))
				_keepDistance.Active = !IsSpacingBlockedState();
		}

		/// <summary>转身期间不起新招（纵深防御；真正的主闸是"玩家不在前向矩形里"）。</summary>
		public override bool CanStartAttack()
			=> !(_facing?.IsTurning ?? false) && base.CanStartAttack();

		/// <summary>钉在机械桩上：击退/吸附等一切强制位移都推不动它。</summary>
		public override void ApplyKnockbackDisplacement(Vector2 direction, float distance, float duration)
		{
			// 有意空实现（炮台固定）
		}

		// ── 内部 ──────────────────────────────────────────────────────────

		/// <summary>三个组件都按**类型**在直接子节点里找：改名/换层级都不用同步路径配置（各只有一个实例，无歧义）。</summary>
		private void ResolveNodes()
		{
			_facing = FindChildByType<EnemyF1RogueAITurretFacingController>();
			if (_facing != null)
				_facing.FacingCommitted += OnFacingCommitted;
			else
				GD.PushWarning($"{Name}: 子节点里没有转身组件（EnemyF1RogueAITurretFacingController），本炮台不会转身");

			_keepDistance = FindChildByType<RailKeepDistanceBehavior>();

			// 滑槽的追击让位：只保留它的每帧硬钳（本炮台的位移自己提交）
			_railChase = FindChildByType<RailChaseMovement>();
			if (_railChase != null)
				_railChase.ExternalDrive = true;   // 不许它自己追人
		}

		private T? FindChildByType<T>() where T : Node
		{
			foreach (var child in GetChildren())
				if (child is T typed) return typed;
			return null;
		}

		/// <summary>转身组件提交朝向时：**由宿主写朝向**（`FacingRight` 的 setter 是 protected，平级组件写不了），
		/// 并镜像前向锚点。</summary>
		private void OnFacingCommitted(bool facingRight)
		{
			FacingRight = facingRight;
			MirrorForwardAnchors(facingRight);
		}

		/// <summary>把"前向锚点"的 local X 按朝向镜像（保留各自初始的绝对值）。
		/// 不镜像 SpineSprite：那套骨架的 d_* 是手绘镜像，引擎再翻就是双重镜像。</summary>
		private void MirrorForwardAnchors(bool facingRight)
		{
			foreach (var (node, offsetX) in _forwardAnchors)
			{
				if (node == null || !GodotObject.IsInstanceValid(node)) continue;

				var position = node.Position;
				position.X = facingRight ? Mathf.Abs(offsetX) : -Mathf.Abs(offsetX);
				node.Position = position;
			}

			// 低频状态变更（只在朝向提交时发生）→ 无条件打点，与 magnet/boss 的状态日志同一风格
			GD.Print($"{Name}: 前向锚点已镜像（朝向 = {(facingRight ? "右" : "左")}，{_forwardAnchors.Count} 个）");
		}

		private bool IsAttackStateActive()
			=> (StateMachine?.CurrentState?.Name ?? string.Empty) == "Attack";

		/// <summary>这些状态下不接管机械轴移动：受击/冻结/眩晕/死亡（与机械侧"被击不驱动移动"同一约定）。
		/// 其余状态（Idle/Walk/Attack…）都要维持间距——**这正是"持续追踪玩家"的驱动**：玩家在攻击矩形外
		/// 就靠过去，进矩形后由静止状态轮询开火，形成闭环。</summary>
		private bool IsSpacingBlockedState()
		{
			string current = StateMachine?.CurrentState?.Name ?? string.Empty;
			return current is "Hit" or "Frozen" or "CooldownFrozen" or "Dying" or "Dead";
		}
	}
}
