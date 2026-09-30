using Godot;
using Kuros.Core;
using Kuros.Core.Events;

/// <summary>
/// 带轮子的滑槽（SlideRail_wheel 专用）：在 <see cref="SlideRailMount"/> 之上只加一件事——
/// **轮子接触判定区命中目标时：造成伤害 + 对该目标击退 + 把滑槽自己顶回去（回弹）**。
///
/// 基类还被井道升降 / 龙门吊 / 磁铁臂滑槽复用（那些轨不该多出一个可能被误配的伤害通道）。
///
/// 三条与项目准则对齐的约定：
///   1. **阵营**：默认 `Player | WorldItem`——与敌侧攻击特效一致（EnemyBullet / LaserBeamA / RotatingCube…）。
///      一次性家具（`RigidBodyWorldItemEntity`：入 `world_items` 组 + 实现 `TakeDamage(float)`，且**没有**
///      DestructibleObject 那种 `damage_receivable` 豁免）必须被显式列进来才打得动。
///   2. **接触体原样交给伤害管线**：家具的阻挡体是它 `RigidBody2D` 下的 `StaticBody2D`（不是 GameActor），
///      而 `DamageDispatcher.DealDamage` 会自己沿父链往上走到家具根命中 `TakeDamage`——这里不做归属解析，
///      只有"击退目标"那一步才要求 `GameActor`。
///   3. **回弹的触发 = `DealDamage` 返回 true：所以"打不动的东西不会顶住轮子"。
///   4. **轮子是机械的工具，不是独立危险物**：判定区挂在轨道上（`Visual/HitArea`），机械被销毁
///      （满进度归位后 QueueFree / 被击杀）后它仍然存在——所以每次接触与每帧回弹都要先问
///      <see cref="HasLiveMachine"/>：Mount 下没有活着的机械时，轨道只剩布景，**不再伤害任何目标**
///      （连一次性家具也不打）、不击退、不回弹、不顶住。
///
/// 回弹本体是**一维冲量叠加**（不是贝塞尔曲线）：参数只有 初速 BounceSpeed / 回落加速度 BounceGravity，
/// 方向在撞击那一刻按"当时的下压方向取反"锁定（避免回弹期间来回翻符号抖动）。
/// 被顶住期间用 <see cref="SlideRailMount.HoldTargetDrive"/> 压住目标驱动——否则命令会一直往下推、
/// 把轮子从家具里"钻"过去；家具一碎（重叠消失）立刻自动恢复下压。
/// </summary>
public partial class SlideRailWheelMount : SlideRailMount
{
	[ExportCategory("Wheel Hazard")]
	/// <summary>轮子的接触判定区（相对本节点）。默认就是场景里 Visual 下的 HitArea；
	/// 以后让轮子自己滚动/旋转时，把它挂到 RailWheel 下、这里改路径即可。</summary>
	[Export] public NodePath HitAreaPath { get; set; } = new("Visual/HitArea");
	/// <summary>接触伤害（0 = 只击退不伤害，同时也**不会**把轮子顶住——回弹的触发就是"这一下真的造成了伤害"）。</summary>
	[Export(PropertyHint.Range, "0,500,1")] public float ContactDamage { get; set; } = 10f;
	/// <summary>接触击退距离（px，0 = 不击退）。只对 <see cref="GameActor"/> 目标生效（家具不会被击退）。</summary>
	[Export(PropertyHint.Range, "0,500,10")] public float KnockbackDistance { get; set; } = 150f;
	[Export(PropertyHint.Range, "0.1,5,0.1")] public float KnockbackDuration { get; set; } = 0.3f;
	/// <summary>哪些阵营会被轮子伤到（也因此会顶住轮子）。默认与敌侧特效一致：玩家 + 世界物（一次性家具）。</summary>
	[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
	public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.Player | TargetableFactions.WorldItem;
	/// <summary>叠加到判定区上的物理查询掩码。默认 5 = 层1(HitArea/静态体) + 层3(玩家 body)。</summary>
	[Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask { get; set; } = 5u;
	/// <summary>伤害来源（必须是 GameActor——伤害管线对 GameActor 目标要求非空攻击者）。
	/// **留空 = 自动用挂在本轨 Mount 下的机械**（如轮子轨的磁铁臂）；拿不到时家具照打（走 TakeDamage 通道），
	/// 但对玩家的伤害不会结算，并警告一次。</summary>
	[Export] public NodePath AttackerPath { get; set; } = new();
	[Export] public bool EnableDebugLogs { get; set; } = false;

	[ExportCategory("Wheel Bounce")]
	/// <summary>被顶住时的回弹初速（px/s，沿滑槽轴、与当时的行进方向相反；0 = 不回弹只停住）。</summary>
	[Export(PropertyHint.Range, "0,3000,10")] public float BounceSpeed { get; set; } = 400f;
	/// <summary>回落加速度（px/s²）：把回弹拉回原来的下压方向。
	/// 回弹高度 ≈ BounceSpeed² / (2×本值)，一次回弹耗时 ≈ 2×BounceSpeed / 本值。</summary>
	[Export(PropertyHint.Range, "10,20000,10")] public float BounceGravity { get; set; } = 1600f;

	private Area2D? _hitArea;
	private GameActor? _attacker;
	private bool _attackerWarned;
	/// <summary>正在接触（且这一下真的造成了伤害）的物理体：既是"伤害只结算一次"的记账，也是"被顶住"的判据。</summary>
	private readonly System.Collections.Generic.HashSet<Node> _contactBodies = new();
	// 回弹：沿滑槽轴的叠加速度 + 撞击那一刻锁定的"下压方向"
	private float _bounceVelocity;
	private float _bounceAxisSign;
	/// <summary>本趟回弹累计的偏移量（相对挡住它的那个接触点）。速度过零是**顶点**、偏移过零才是**落回**——
	/// 用后者判定"又撞上了"，每趟都把残余偏移归位，否则每个循环残留一点、轮子会一路往上爬。</summary>
	private float _bounceOffset;

	public override void _Ready()
	{
		base._Ready();   // 基类：外观镜像 + 出生锚点 + CarriagePrefab 入驻 Mount

		_hitArea = HitAreaPath.IsEmpty ? null : GetNodeOrNull<Area2D>(HitAreaPath);
		if (_hitArea == null)
		{
			GD.PushWarning($"{Name}: 未找到轮子判定区（{HitAreaPath}），接触伤害与回弹都不会生效");
			return;
		}

		if (TargetCollisionMask != 0)
			_hitArea.CollisionMask |= TargetCollisionMask;

		_hitArea.BodyEntered += OnBodyEntered;
		_hitArea.AreaEntered += OnAreaEntered;
		_hitArea.BodyExited += OnBodyExited;
		_hitArea.AreaExited += OnAreaExited;
	}

	public override void _ExitTree()
	{
		if (_hitArea != null && GodotObject.IsInstanceValid(_hitArea))
		{
			_hitArea.BodyEntered -= OnBodyEntered;
			_hitArea.AreaEntered -= OnAreaEntered;
			_hitArea.BodyExited -= OnBodyExited;
			_hitArea.AreaExited -= OnAreaExited;
		}
		base._ExitTree();
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);   // 基类：朝目标恒速滑动 + 两端硬钳（被顶住时压住）
		TickBlockAndBounce((float)delta);
	}

	// ── 接触 → 伤害 / 击退 / 回弹 ─────────────────────────────────────────

	private void OnBodyEntered(Node body) => TryContact(body);

	private void OnAreaEntered(Area2D area)
	{
		// 只认目标的受击判定区（HitArea），避免敌人的攻击判定区之类误触发
		if ((string)area.Name != "HitArea") return;

		var actor = area.Owner as GameActor
			?? area.GetParent() as GameActor
			?? area.GetParent()?.GetParent() as GameActor;
		if (actor != null) TryContact(actor);
	}

	private void OnBodyExited(Node body) => RemoveContact(body);

	private void OnAreaExited(Area2D area)
	{
		var actor = area.Owner as GameActor
			?? area.GetParent() as GameActor
			?? area.GetParent()?.GetParent() as GameActor;
		if (actor != null) RemoveContact(actor);
	}

	/// <summary>接触一次：走一次伤害管线；**真的造成了伤害**才记录为"顶住者"并击退 + 回弹。
	/// 同一目标在离开前只结算一次（离开后重新武装；回弹则在此期间持续循环，见 <see cref="TickBlockAndBounce"/>）。</summary>
	private void TryContact(Node hitNode)
	{
		if (hitNode is GameActor ga && (ga.IsDeathSequenceActive || ga.IsDead)) return;
		if (!HasLiveMachine()) return;   // 机械没了 = 纯布景：不伤害、不击退、也不回弹
		if (!_contactBodies.Add(hitNode)) return;

		if (!ApplyHit(hitNode))
		{
			// 没打到（阵营不匹配 / 被拦截）→ 不顶住轮子
			_contactBodies.Remove(hitNode);
			return;
		}

		StartBounce();
	}

	/// <summary>对目标结算一次撞击：伤害 + 击退（只对角色）+ 打点。
	/// 首次接触与之后**每一次弹回去撞上**都走这里——否则家具永远打不碎、轮子会无限弹下去。</summary>
	private bool ApplyHit(Node hitNode)
	{
		Vector2 dir = ResolvePushDirection();
		var attacker = ResolveAttacker();

		bool dealt = false;
		if (ContactDamage > 0f)
		{
			// bypassDirectionCheck：接触伤害是"全方位区域效果"，不该被方向性屏障的方向限制挡掉。
			// 接触体原样传入：家具的 StaticBody2D 会被管线沿父链走到根的 TakeDamage 通道。
			dealt = DamageDispatcher.DealDamage(hitNode, ContactDamage, _hitArea!.GlobalPosition, attacker,
				DamageSource.AreaEffect, TargetableFactions, false, null, dir, true);
		}

		// 击退：只对角色生效（家具是被撞碎的，不会被"击退"）。
		// **不能事后判断无敌帧**：这一下命中本身就会给玩家无敌帧，"打完之后再看 IsHitInvincible" 会把
		// 刚命中的这一击也判成已无敌 → 表现就是"掉血但不弹"。玩家侧放行由
		// GameActor.ApplyKnockbackDisplacement 内部统一处理（MainCharacter 覆写 AllowsKnockback，
		// 读一次 ConsumePendingHitKnockback：无敌帧/护盾格挡/IgnoreHitStateOnDamage 时为 false）。
		// 这里只读不消费，避免和内部那道门重复消费。
		if (dealt && hitNode is GameActor target && KnockbackDistance > 0f)
		{
			bool allowKnock = target is not Kuros.Actors.Heroes.MainCharacter mc
				|| mc.HasPendingHitKnockback;

			if (allowKnock && !target.ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement))
				target.ApplyKnockbackDisplacement(dir, KnockbackDistance, KnockbackDuration);
		}

		if (EnableDebugLogs && dealt)
			GD.Print($"{Name}: 轮子撞击 → {hitNode.Name}：伤害 {ContactDamage}（来源 {attacker?.Name ?? "(无)"}），"
				+ $"击退 {dir} × {KnockbackDistance}");

		return dealt;
	}

	private void RemoveContact(Node body)
	{
		if (body != null && GodotObject.IsInstanceValid(body))
			_contactBodies.Remove(body);
	}

	// ── 被顶住 → 回弹循环 ────────────────────────────────────────────────

	/// <summary>每帧：维护"被顶住"状态（压住目标驱动）+ 推进回弹冲量。
	/// 回弹结束时若仍被压着 → 再弹一次（连续撞击）；家具被打碎/移开 → 重叠消失 → 恢复正常下压。</summary>
	private void TickBlockAndBounce(float delta)
	{
		// 机械没了（被销毁 / 满进度退场）→ 轮子只是布景：不顶住、不回弹、也不再结算撞击
		bool blocked = HasLiveMachine() && HasLiveBlocker();
		HoldTargetDrive = blocked;

		if (!blocked) return;

		// 被压着但当前没有回弹在跑 → 起弹。这条也覆盖"接触那一刻还没有下压方向"（例如轮子停着被家具压住，
		// 之后才收到下压命令）：那种情况不会再有接触事件来触发起弹，只能在这里补。
		if (_bounceVelocity == 0f)
		{
			StartBounce();
			if (_bounceVelocity == 0f) return;   // 仍然没有下压方向 → 就停住不动
		}

		float step = _bounceVelocity * delta;
		OffsetAxis(step);
		_bounceOffset += step;
		_bounceVelocity += _bounceAxisSign * BounceGravity * delta;

		// 偏移回到出发点（越过 0）= 这一趟回弹走完 = 轮子又砸在挡路的东西上（注意不是"速度过零"= 顶点）
		if (_bounceOffset * _bounceAxisSign >= 0f)
		{
			OffsetAxis(-_bounceOffset);   // 归位：不让每趟残留偏移（否则会一路向上爬）
			_bounceOffset = 0f;
			_bounceVelocity = 0f;
			SlamBlockers();   // 每撞一次结算一次伤害（家具的 DamageCooldown / 玩家无敌帧会自然节流）
			StartBounce();    // 还压着 → 接着弹，直到它被打碎/让开
		}
	}

	/// <summary>起一次回弹：方向 = 撞击那一刻**下压方向的反向**（锁定，避免回弹期间来回翻符号）。
	/// 没有下压方向（已到位/被停止）时只停住不回弹。</summary>
	private void StartBounce()
	{
		if (BounceSpeed <= 0f) return;
		if (_bounceVelocity != 0f) return;   // 回弹进行中不重复起踢（接触在判定区边缘时防抖）

		float commanded = CommandedAxisSign;
		if (commanded == 0f)
		{
			_bounceVelocity = 0f;
			return;
		}

		_bounceAxisSign = commanded;
		_bounceVelocity = -commanded * BounceSpeed;
		_bounceOffset = 0f;
	}

	/// <summary>对当前仍压着的目标各结算一次撞击（快照遍历——ApplyHit 不修改表，但保持安全习惯）。</summary>
	private void SlamBlockers()
	{
		foreach (var node in new System.Collections.Generic.List<Node>(_contactBodies))
		{
			if (node != null && GodotObject.IsInstanceValid(node))
				ApplyHit(node);
		}
	}

	/// <summary>表里是否还有"活着且仍与判定区重叠"的顶住者（顺带剔除已失效/已移开的）。</summary>
	private bool HasLiveBlocker()
	{
		if (_contactBodies.Count == 0) return false;

		bool any = false;
		_contactBodies.RemoveWhere(node =>
		{
			if (node == null || !GodotObject.IsInstanceValid(node)) return true;
			if (node is GameActor ga && (ga.IsDeathSequenceActive || ga.IsDead)) return true;
			if (node is CollisionObject2D body && _hitArea != null && GodotObject.IsInstanceValid(_hitArea)
				&& _hitArea.OverlapsBody(body))
			{
				any = true;
				return false;
			}
			return true;   // 不再重叠（被打碎 / 被炸飞 / 玩家走开）→ 剔除
		});
		return any;
	}

	// ── 工具 ──────────────────────────────────────────────────────────────

	/// <summary>击退方向 = 判定区自己的 +X 轴（Visual 的镜像已经把它翻好了）；退化时用世界 +X。</summary>
	private Vector2 ResolvePushDirection()
	{
		if (_hitArea == null || !GodotObject.IsInstanceValid(_hitArea)) return Vector2.Right;

		Vector2 x = _hitArea.GlobalTransform.X;
		return x == Vector2.Zero ? Vector2.Right : x.Normalized();
	}

	/// <summary>本轨是否还有"活着的机械"：显式 <see cref="AttackerPath"/> &gt; Mount 下第一个活着的 GameActor。
	/// 轮子的接触伤害 / 击退 / 回弹 / 顶住全部由它驱动——机械被销毁（满进度归位 QueueFree、被击杀）后，
	/// 轨道只是布景：不再伤害任何目标（连一次性家具也不打）、不击退、不回弹、不顶住。
	/// 每帧/每次接触都现查（不缓存布尔），这样"机械没了又来了一台"能自动恢复。</summary>
	private bool HasLiveMachine()
	{
		if (!AttackerPath.IsEmpty)
			return IsLiveMachine(GetNodeOrNull<GameActor>(AttackerPath));

		var mount = GetNodeOrNull<Node2D>("Mount");
		if (mount == null) return false;

		foreach (var child in mount.GetChildren())
			if (IsLiveMachine(child as GameActor)) return true;
		return false;
	}

	private static bool IsLiveMachine(GameActor? machine)
		=> machine != null && GodotObject.IsInstanceValid(machine)
			&& !machine.IsDead && !machine.IsDeathSequenceActive;

	/// <summary>伤害来源：显式 AttackerPath &gt; 挂在本轨 Mount 下的机械（第一条 GameActor 子节点）。
	/// 只在 <see cref="HasLiveMachine"/> 放行后才调用，所以这里的警告只会在"机械恰好在这次结算前一刻消失"时出现。</summary>
	private GameActor? ResolveAttacker()
	{
		if (_attacker != null && GodotObject.IsInstanceValid(_attacker)) return _attacker;

		if (!AttackerPath.IsEmpty)
			_attacker = GetNodeOrNull<GameActor>(AttackerPath);

		if (_attacker == null)
		{
			var mount = GetNodeOrNull<Node2D>("Mount");
			if (mount != null)
				foreach (var child in mount.GetChildren())
					if (child is GameActor ga) { _attacker = ga; break; }
		}

		if (_attacker == null && !_attackerWarned)
		{
			_attackerWarned = true;
			GD.PushWarning($"{Name}: 找不到伤害来源（AttackerPath 为空且 Mount 下没有机械）——"
				+ "家具照打，但对角色的伤害不会结算");
		}

		return _attacker;
	}
}
