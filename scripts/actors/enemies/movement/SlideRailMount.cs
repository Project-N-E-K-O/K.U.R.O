using Godot;

/// <summary>
/// 通用单轴滑座（滑槽）：一个轴、偏移限位、恒速滑动、每帧硬钳。
/// 由被挂载的子节点（机械）通过 <see cref="SetTarget"/> 驱动；自身不含相位/玩家逻辑，供多台滑轨机械复用
/// （Enemy_F1_rogueAI_Magnet / _MachineGun / _Cannon）。
/// 滑动轴由 Axis 指定（默认 Y）。**限位是"相对本节点摆放位置的两个带符号偏移"**（过去是 Marker 节点的局部坐标，
/// 现在直接写成数字：生成实例用 PropertyOverrides 覆盖一个 float 即可，不必再造节点、不必写 NodePath）。
/// 无行程（两端偏移相等，含 0/0）时滑槽静止，退化成"只有机械在轨道上移动"的单轴形态。
/// 限位解析放在首个物理帧（而不是 _Ready）：生成器是 AddChild 之后才写 GlobalPosition，_Ready 时节点还没摆到位。
/// </summary>
public partial class SlideRailMount : Node2D
{
	public enum RailAxis { X, Y }

	/// <summary>机械出生锚点：Far/Near 是**角色语义**（勾了 FlipCarriageEnds 时跟着端点一起镜像到另一侧）。</summary>
	public enum SpawnAnchor { Far, Near, Origin }

	[ExportCategory("Rail")]
	/// <summary>滑槽自身滑动的轴（默认 Y = 纵向升降；设 X 则为横向滑槽）。</summary>
	[Export] public RailAxis Axis { get; set; } = RailAxis.Y;
	/// <summary>滑槽自身行程的两个端点：沿 <see cref="Axis"/> 的**带符号局部偏移**（相对本节点的摆放位置）。
	/// 两端相等（含 0/0）= 无行程 → 滑槽静止（<see cref="HasSlotLimits"/> = false）。</summary>
	[Export] public float SlotStartOffset { get; set; } = -1000f;
	[Export] public float SlotEndOffset { get; set; } = 500f;
	/// <summary>机械行程的两个端点：沿 <see cref="CarriageAxis"/> 的**带符号局部偏移**（相对本节点的摆放位置）。
	/// Near = 贴玩家侧（场内/内侧）端，Far = 场外端；勾了 <see cref="FlipCarriageEnds"/> 时两个偏移**一起取负**（沿轴镜像）。
	/// 两端相等（含 0/0）= 机械锁死在该偏移处。</summary>
	[Export] public float CarriageNearOffset { get; set; } = 2600f;
	[Export] public float CarriageFarOffset { get; set; } = -2600f;
	/// <summary>
	/// 把这条轨**沿轴镜像**：两个行程偏移一起取负（+2600 / −1300 → −2600 / +1300），
	/// 同时把 <see cref="VisualPath"/> 的 scale.x 取负，**连整块外观一起镜像**。
	/// 角色语义不变——Near 仍是"贴玩家侧"、Far 仍是"场外端"，只是它们落到镜像后的那一侧；
	/// 左右两条镜像轨因此能共用同一份配置（机械侧一律"退场去 Far"即可）。
	///
	/// 注意它**不是**"把两个角色对调"：两端偏移不对称时（如 +2600 / −1300），对调只会让两端换个名字、
	/// 行程仍留在原地（镜像轨的行程就会跑到错误的一侧）；取负才是真正的镜像。
	/// 两端对称时（±2600）两种做法结果相同，所以老配置不受影响。
	/// </summary>
	[Export] public bool FlipCarriageEnds { get; set; }
	/// <summary>外观子节点：只放 Sprite 之类，**里面不能有 Marker / Mount / 物理体**（负缩放不保证碰撞结果）。
	/// 勾了 <see cref="FlipCarriageEnds"/> 时它的 scale.x 被写成负值 = 整块外观镜像；逻辑侧（root/Marker/Mount）
	/// 始终保持单位变换，坐标判定不受影响。</summary>
	[Export] public NodePath VisualPath { get; set; } = new("Visual");
	/// <summary>被挂载的机械场景：配了就自动实例化进 Mount（"机械固定在滑槽内"的父子结构）。</summary>
	[Export] public PackedScene? CarriagePrefab { get; set; }
	/// <summary>机械出生锚点：Far = 场外/待命端（默认）、Near = 贴玩家端、Origin = 滑槽原点（关卡摆放点/轨道中点）。
	/// Far/Near 与行程端点同源，勾了 <see cref="FlipCarriageEnds"/> 时跟着一起镜像到另一侧。
	/// 过场生成的 PropertyOverrides 会在入树前写好这个值，正好赶得上 <see cref="_Ready"/> 里的摆位。</summary>
	[Export] public SpawnAnchor CarriageSpawn { get; set; } = SpawnAnchor.Far;
	/// <summary>滑槽自身滑动速度（px/s），也是匀速上限。</summary>
	[Export(PropertyHint.Range, "10,2000,1")] public float Speed { get; set; } = 200f;
	[Export(PropertyHint.Range, "0,64,1")] public float ArriveDeadzone { get; set; } = 4f;

	/// <summary>跟随平滑度（1/s）：与 <c>ShieldUmbrellaEffect</c> / <c>BunnySwardFloatingCannon</c> / P2 的
	/// `FollowSmoothing` **同源同值（8.5）**——目标坐标按 `1 − exp(−Smoothing·dt)` 指数趋近，
	/// 稳态落后 = 目标速度 ÷ Smoothing（玩家 500 → 落后 ≈ 59px、1000 → ≈ 118px），
	/// 随速度连续变化，不会"追得上就锁死、追不上就爬坡"。
	/// **≤ 0 = 关闭平滑**（直接吸附到目标，旧行为）。
	/// 只作用于普通 <see cref="SetTarget"/>；<see cref="SetTargetInTime"/> 按时长锁定速度、不受影响。</summary>
	[Export(PropertyHint.Range, "0,30,0.1")] public float FollowSmoothing { get; set; } = 8.5f;

	private bool _resolved;
	private float _slotStart;
	private float _slotEnd;
	private float _carriageNear;
	private float _carriageFar;
	private float _target;
	private bool _hasTarget;
	/// <summary>本次目标的速度覆盖（&gt; 0 时用它，否则用自身 <see cref="Speed"/>）：见 <see cref="SetTargetInTime"/>。
	/// 每次 <see cref="SetTarget"/>（常规跟随）会清零——时长只对"设目标那一次"负责。</summary>
	private float _targetSpeed;

	/// <summary>限位已解析（首个物理帧之后）。</summary>
	public bool IsResolved => _resolved;
	/// <summary>滑槽是否可动（两端点偏移不等）。</summary>
	public bool HasSlotLimits { get; private set; }
	public float SlotStart => _slotStart;
	public float SlotEnd => _slotEnd;
	/// <summary>
	/// 两个行程端点的坐标（世界坐标 = 本节点摆放位置 + 偏移，沿 <see cref="CarriageAxis"/>），**已按角色解释**：
	/// Near = 贴玩家侧端、Far = 场外端；勾了 <see cref="FlipCarriageEnds"/> 时两个端点**整体镜像**到轴的另一侧（角色不变）。
	/// 机械直接用这两个选端点（退场去 Far），用下面的区间做夹取/钳位。
	/// </summary>
	public float CarriageNear => _carriageNear;
	public float CarriageFar => _carriageFar;
	/// <summary>行程区间（已排序，与角色无关）：夹取/钳位用这两个，别拿 Near/Far 当 Clamp 的上下界。</summary>
	public float CarriageLo => Mathf.Min(_carriageNear, _carriageFar);
	public float CarriageHi => Mathf.Max(_carriageNear, _carriageFar);
	/// <summary>机械沿其移动的轴 = 滑动轴的正交轴。</summary>
	public RailAxis CarriageAxis => Axis == RailAxis.Y ? RailAxis.X : RailAxis.Y;
	/// <summary>滑槽当前在滑动轴上的世界坐标。</summary>
	public float CurrentRailCoordinate => GetCoordinate(GlobalPosition);

	/// <summary>滑槽是否已滑到目标（无 Slot 限位/无目标 = 视为到位）。</summary>
	public bool Arrived => !HasSlotLimits || !_hasTarget
		|| Mathf.Abs(_target - CurrentRailCoordinate) <= ArriveDeadzone;

	/// <summary>机械的出生点（**局部坐标**，Mount 坐标系）：机械可以据此"回生成点"
	/// （例如满进度后收工归位）。与出生摆放同源，FlipCarriageEnds/CarriageSpawn 都算在内。</summary>
	public Vector2 SpawnLocalPosition { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		ApplyVisualMirror();
		SpawnLocalPosition = ResolveSpawnPosition();

		// 挂载点：CarriagePrefab 自动入驻 Mount，构成"机械是滑槽子节点"的结构约束
		var mount = EnsureMount();
		if (CarriagePrefab != null && mount.GetChildCount() == 0)
		{
			var carriage = CarriagePrefab.Instantiate<Node2D>();
			mount.AddChild(carriage);
			carriage.Position = SpawnLocalPosition;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint()) return;

		ResolveNow();
		if (!HasSlotLimits) return;   // 无行程：滑槽静止

		if (!_hasTarget || HoldTargetDrive)
		{
			// 待命 / 被阻挡：不朝目标推进，但两端硬钳照旧（外部推力、叠加位移都压不出去）
			SetCoordinate(Mathf.Clamp(CurrentRailCoordinate, _slotStart, _slotEnd));
			return;
		}

		float step = _target - CurrentRailCoordinate;
		float distance = Mathf.Abs(step);

		// 推进方式二选一：
		//   ① _targetSpeed > 0（SetTargetInTime）→ 速度按时长锁定，不做平滑（精确计时），死区收口
		//   ② 普通 SetTarget → **与伞/浮游炮/P2 同款的位置一阶滞后**（FollowSmoothing ≤ 0 时直接吸附）
		if (_targetSpeed > 0f)
		{
			if (distance <= ArriveDeadzone)
			{
				SetCoordinate(_target);
				return;
			}
			SetCoordinate(CurrentRailCoordinate + Mathf.Sign(step) * _targetSpeed * (float)delta);
		}
		else if (FollowSmoothing > 0f)
		{
			// 平滑档**不做"到点吸附"**：目标缓慢移动时每帧位移都可能小于死区，
			// 吸附会把轨道逐帧直接拽到目标上 = 逐帧锁步，看起来"完全没有惯性"
			//（参考实现 伞/浮游炮/P2 也没有吸附；到位判定仍由 Arrived 按死区容差给出）。
			float blend = 1f - Mathf.Exp(-FollowSmoothing * (float)delta);
			SetCoordinate(Mathf.Lerp(CurrentRailCoordinate, _target, blend));
		}
		else
		{
			SetCoordinate(_target);   // 关闭平滑：直接到位
		}
		// 硬钳兜底：外部系统（爆炸/黑洞直接写位置）也推不出滑槽
		SetCoordinate(Mathf.Clamp(CurrentRailCoordinate, _slotStart, _slotEnd));
	}

	/// <summary>把"角色翻转"同步到外观：<see cref="FlipCarriageEnds"/> 时 <see cref="VisualPath"/> 的 scale.x 写负
	/// ——整块美术（含 Visual 下每个精灵的局部位置）一起镜像。
	/// 用负缩放而不是 flip_h：flip_h 只是"这一个绘制节点的贴图怎么画"，不沿节点树传递（Node2D 上根本没有这个属性，
	/// Sprite2D 上也只有它自己受影响）；scale 属于变换链，才会带着子树一起翻。
	/// 符号由本开关**唯一决定**（写 ±|x|，不累乘）：在编辑器里手摆的镜像会在运行时被规范化掉。
	/// 只在 _Ready 应用一次——过场的 PropertyOverrides 是在入树前写好的，正好赶得上。</summary>
	private void ApplyVisualMirror()
	{
		var visual = VisualPath.IsEmpty ? null : GetNodeOrNull<Node2D>(VisualPath);
		if (visual == null)
		{
			if (FlipCarriageEnds)
				GD.PushWarning($"{Name}: FlipCarriageEnds=true 但未找到外观节点（{VisualPath}），外观不会被镜像");
			return;
		}

		var scale = visual.Scale;
		float absX = Mathf.Abs(scale.X);
		visual.Scale = new Vector2(FlipCarriageEnds ? -absX : absX, scale.Y);
	}

	/// <summary>机械出生位置（**局部坐标**——与滑槽在关卡里的摆放位置无关，_Ready 阶段就能算准）。
	/// 默认 Far（待命位/场外端），避免从轨道中间冒出来再滑走。
	/// 角色解释与 <see cref="ResolveNow"/> 同源：FlipCarriageEnds 对"出生点"和"行程端点"同时生效。</summary>
	private Vector2 ResolveSpawnPosition()
	{
		if (CarriageSpawn == SpawnAnchor.Origin) return Vector2.Zero;

		// 角色 → 偏移一一对应（不再对调）；镜像只体现在符号上
		bool wantFar = CarriageSpawn == SpawnAnchor.Far;
		float offset = wantFar ? CarriageFarOffset : CarriageNearOffset;
		if (FlipCarriageEnds) offset = -offset;
		return CarriageAxis == RailAxis.X ? new Vector2(offset, 0f) : new Vector2(0f, offset);
	}

	/// <summary>被阻挡时压住"目标驱动"：true 期间不朝目标推进（坐标与两端硬钳照旧，叠加位移仍然生效）。
	/// 由子类设置（如轮子撞上一次性家具）；清掉后自动继续朝原目标推进——"家具被打碎就继续下压"。</summary>
	public bool HoldTargetDrive { get; set; }

	/// <summary>当前下发的滑动方向（沿 <see cref="Axis"/> 的符号）：无行程 / 无目标 / 已到位 = 0。</summary>
	public float CommandedAxisSign => !HasSlotLimits || !_hasTarget
		? 0f
		: (Mathf.Abs(_target - CurrentRailCoordinate) <= ArriveDeadzone
			? 0f
			: Mathf.Sign(_target - CurrentRailCoordinate));

	/// <summary>沿滑动轴额外偏移一段（相对当前坐标，带两端硬钳）。
	/// 供子类做"叠加式"运动（轮子的回弹偏移）：与 <see cref="SetTarget"/> 的目标驱动互不干扰——
	/// 一个是命令（被阻挡时可被 <see cref="HoldTargetDrive"/> 压住），一个是叠加量。</summary>
	public void OffsetAxis(float delta)
	{
		if (delta == 0f) return;

		float next = CurrentRailCoordinate + delta;
		if (HasSlotLimits) next = Mathf.Clamp(next, _slotStart, _slotEnd);
		SetCoordinate(next);
	}

	/// <summary>设置滑槽滑动目标（世界坐标，滑动轴）。无 Slot 限位时忽略（滑槽静止）。
	/// 常规跟随用它（速度 = 自身 <see cref="Speed"/>）。</summary>
	public void SetTarget(float coordinate)
	{
		SetTargetInternal(coordinate);
		_targetSpeed = 0f;
	}

	/// <summary>设置目标并**按固定时长到达**：速度 = 本次距离 / <paramref name="duration"/>。
	/// 语义：调用方（满进度退场等）要"总耗时固定"，不关心距离有多远。
	/// 只对**这一次**目标负责——常规的每帧 <see cref="SetTarget"/> 跟随会把速度交还给自己 <see cref="Speed"/>，
	/// 所以用它的那段时间里不要再每帧重设目标（否则等于没设过）。
	/// 无 Slot 限位（滑槽静止）/ duration ≤ 0 / 已在目标上 → 退回自身 Speed。</summary>
	public void SetTargetInTime(float coordinate, float duration)
	{
		float before = CurrentRailCoordinate;
		SetTargetInternal(coordinate);
		_targetSpeed = duration > 0f && _hasTarget
			? Mathf.Abs(_target - before) / duration
			: 0f;
	}

	private void SetTargetInternal(float coordinate)
	{
		if (!HasSlotLimits) return;
		_target = Mathf.Clamp(coordinate, _slotStart, _slotEnd);
		_hasTarget = true;
	}

	/// <summary>解析限位（幂等；机械侧可主动调用以避开节点处理顺序假设）。
	/// 偏移是"相对本节点摆放位置"的，所以世界坐标 = **解析那一刻本节点的坐标 + 偏移**，只算一次——
	/// 滑槽随后自己滑动、机械随后被钳位，都不会再改变这两个端点（与过去读 Marker 世界坐标完全等价）。</summary>
	public void ResolveNow()
	{
		if (_resolved) return;
		_resolved = true;

		// 滑槽自身行程（沿 Axis）：两端相等 = 无行程 → 静止
		HasSlotLimits = !Mathf.IsEqualApprox(SlotStartOffset, SlotEndOffset);
		if (HasSlotLimits)
		{
			float origin = CurrentRailCoordinate;
			_slotStart = origin + Mathf.Min(SlotStartOffset, SlotEndOffset);
			_slotEnd = origin + Mathf.Max(SlotStartOffset, SlotEndOffset);
			SetCoordinate(Mathf.Clamp(CurrentRailCoordinate, _slotStart, _slotEnd));
		}
		else
		{
			_slotStart = _slotEnd = CurrentRailCoordinate;
		}

		// 机械行程（沿 CarriageAxis）：角色约定（Near = 玩家侧、Far = 场外）不变；FlipCarriageEnds = 沿轴镜像
		// （两个偏移一起取负，不是对调角色——不对称偏移时两者结果完全不同）
		float carriageOrigin = GetCarriageCoordinate(GlobalPosition);
		float mirror = FlipCarriageEnds ? -1f : 1f;
		_carriageNear = carriageOrigin + CarriageNearOffset * mirror;
		_carriageFar = carriageOrigin + CarriageFarOffset * mirror;
	}

	/// <summary>把世界坐标投影到滑动轴（= 该轴分量）。</summary>
	public float GetCoordinate(Vector2 worldPosition)
		=> Axis == RailAxis.X ? worldPosition.X : worldPosition.Y;

	/// <summary>把世界坐标投影到机械的移动轴。</summary>
	public float GetCarriageCoordinate(Vector2 worldPosition)
		=> CarriageAxis == RailAxis.X ? worldPosition.X : worldPosition.Y;

	/// <summary>沿父链查找最近的滑槽（机械用：直接父节点可能是 Mount 挂载点）。</summary>
	public static SlideRailMount? FindFor(Node? node)
	{
		var current = node?.GetParent();
		for (int i = 0; i < 4 && current != null; i++)
		{
			if (current is SlideRailMount rail) return rail;
			current = current.GetParent();
		}
		return null;
	}

	private Node2D EnsureMount()
	{
		var mount = GetNodeOrNull<Node2D>("Mount");
		if (mount == null)
		{
			mount = new Node2D { Name = "Mount" };
			AddChild(mount);
		}
		return mount;
	}

	private void SetCoordinate(float coordinate)
	{
		var pos = GlobalPosition;
		if (Axis == RailAxis.X) pos.X = coordinate;
		else pos.Y = coordinate;
		GlobalPosition = pos;
	}
}
