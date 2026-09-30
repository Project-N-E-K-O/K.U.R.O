using System;
using Godot;
using Kuros.Core;
using Kuros.Systems.Cutscene;

namespace Kuros.Environments
{
	/// <summary>
	/// F_begin「上升井道」的关卡侧进度载体（挂在房间根节点上），一件事两面用途：
	///   1) **击杀统计**：静态事件 <see cref="GameActor.DeathFinalized"/> + "enemies" 组过滤。
	///      常态免伤的本体/磁铁臂走不到 FinalizeDeath，天然不计入；生成的敌人挂在世界节点下（不在本房间子树里），
	///      所以只能按组统计、不能按子树。
	///   2) **上升视觉**：进度 0..1 → AnimationPlayer.SpeedScale（越接近满进度越快）。**只调速、不当刹车**：
	///      进度满只是交还速度控制，上升循环继续跑到到站过场接手；**停住**发生在到站过场开始那一刻
	///      （AnimationPlayer.Pause，保持姿态）。不靠 SpeedScale 归零/渐减来刹车：否则残留会给随后在同一播放器上
	///      播的到站动画（up_to_top）留下"时间不走 / 速度非 1"的假播放，把过场卡死。
	///   3) **进度是公开数据**：消费者（Boss 的大招阈值、按进度的形态升级…）自己读
	///      <see cref="Progress"/> / <see cref="ProgressChanged"/> 并**各自定义阈值**——与血量阈值敌人
	///      （UltimateHealthThresholds 放在敌人自己的控制器里）同构。本控制器不替消费者定义阈值，
	///      也不广播阈值事件（轮询式判断天然幂等：晚生成、一次跨多个阈值都不会漏/重）。
	/// 到站（进度满）：**交还上升速度控制**（循环继续跑）+ 武装当前段的 ArrivalTrigger（触发区覆盖平台 = 立刻播；
	/// 挪到出口 = 走过去才播；可再用该段的 ArrivalTriggerDelay 延后几秒）；到站过场一开始**暂停上升循环**并释放速度接管
	/// ——停在这里而不是进度满那一刻，因为两者之间可能隔着一段延迟，电梯在那段时间里应该还在往上走。
	///
	/// **分段**（<see cref="Phases"/>）：同一个场景里"两段击杀进度"串行使用同一个进度条——
	/// 每段带自己的需求击杀数 / 到站触发器 / 上升动画（二阶段没有上升动画就把路径留空 = 不驱动）。
	/// 本段到站过场**一开始**就自动 <see cref="BeginNextPhase"/>（复位计数与状态、换成下一段的参数），
	/// 敌人则由各段的过场自行生成/清理（EffectGroupSpawnStep / EffectDespawnStep），进度这边不掺和。
	/// Phases 是唯一配置入口（单段关卡填 1 项）；留空 = 本关不用进度。
	///
	/// 时序铁律：loop 动画真正挂上（CurrentAnimation == LoopAnimationName）之前**一个字节都不写 SpeedScale**——
	/// 过场的 `up` 是 WaitForCompletion 阻塞式，写成 0 会把整段过场冻死（后续 Boss/滑槽都不会出场）。
	/// </summary>
	[GlobalClass]
	public partial class KillProgressAscendController : Node2D
	{
		/// <summary>组名：Boss 侧与其它消费者用 Find/GetFirstNodeInGroup 找本控制器。
		/// 注意不要再加进 player/enemies 组——CameraFollow 的局部顿帧会冻结组内节点。</summary>
		public const string ProgressGroup = "kill_progress";

		[ExportCategory("Phases 分段")]
		/// <summary>分段列表：一项 = 一段"击杀进度 → 到站"，段与段**串行**接续（典型：一段上升井道、二段最终平台，
		/// 两段的需求击杀数 / 到站触发器 / 上升动画都不同，而场景不用拆成两个）。
		/// **本列表是唯一配置入口**：需求击杀数 / 到站触发器 / 武装延迟 / 上升动画全部取自当前段
		/// （单段关卡就填 1 项）；**留空 = 本关不用进度**（不计数、不武装、不驱动，并警告一次）。
		/// 换段入口：<see cref="BeginNextPhase"/>（默认由"本段到站过场开始"自动触发）。</summary>
		[Export] public Godot.Collections.Array<KillProgressPhase> Phases { get; set; } = new();

		[ExportCategory("Ascend Visual")]
		/// <summary>总闸：false = 整关不驱动上升视觉（进度照常统计/武装，只是不动 SpeedScale）。
		/// 这是唯一的节点级视觉开关——"某一**段**不驱动"请用该段把 AnimationPlayerPath 留空表达（不要两个开关管同一件事）。</summary>
		[Export] public bool DriveAscendVisual { get; set; } = true;

		[ExportCategory("Debug")]
		[Export] public bool EnableDebugLogs { get; set; } = false;

		[Signal] public delegate void AscendArrivedEventHandler();

		public int KillCount { get; private set; }
		/// <summary>0..1，连续。消费者（Boss 阈值等）自行取用。</summary>
		public float Progress { get; private set; }
		/// <summary>是否已接管 loop 动画的速度控制。</summary>
		public bool VisualDriveActive { get; private set; }
		/// <summary>进度是否已满（到站）。</summary>
		public bool HasArrived { get; private set; }
		/// <summary>当前段索引（未配置分段时恒 0）。</summary>
		public int CurrentPhaseIndex => _phaseIndex;
		/// <summary>分段总数（未配置分段时 = 0）。</summary>
		public int PhaseCount => ProgressEnabled ? Phases.Count : 0;
		/// <summary>所有分段都已走完（最后一段的到站过场结束后为 true）。</summary>
		public bool AllPhasesDone => _allPhasesDone;
		/// <summary>当前段配置；未配置分段时为 null。</summary>
		public KillProgressPhase? CurrentPhase => ProgressEnabled ? Phases[Mathf.Clamp(_phaseIndex, 0, Phases.Count - 1)] : null;

		/// <summary>(killCount, progress)</summary>
		public event Action<int, float>? KillCountChanged;
		public event Action<float>? ProgressChanged;

		private AnimationPlayer? _animPlayer;
		private float _speedScaleBeforeDrive = 1f;
		private CutsceneManager? _cutsceneManager;
		private bool _controlReleased;
		// 到站触发器的延迟武装（ArrivalTriggerDelay）
		private float _arrivalArmRemaining;
		private bool _arrivalTriggerArmed;
		// 到站判定是否已补查过（"开局即满"没有击杀事件来触发 CheckArrival，见 _PhysicsProcess）
		private bool _arrivalChecked;
		// 分段：当前段索引 + 是否已全部走完
		private int _phaseIndex;
		private bool _allPhasesDone;

		/// <summary>是否配了分段 = 本关启用进度。false 时 Progress 恒 0、不武装、不驱动。</summary>
		private bool ProgressEnabled => Phases != null && Phases.Count > 0;

		// 当前段的取值（唯一来源：KillProgressPhase）。未配置分段时这些值都不会被用到（见 ProgressEnabled）
		private int PhaseTotalKillsForFull => CurrentPhase?.TotalKillsForFull ?? 0;
		private NodePath PhaseArrivalTriggerPath => CurrentPhase?.ArrivalTriggerPath ?? new NodePath();
		private float PhaseArrivalTriggerDelay => CurrentPhase?.ArrivalTriggerDelay ?? 0f;
		private NodePath PhaseAnimationPlayerPath => CurrentPhase?.AnimationPlayerPath ?? new NodePath();
		private string PhaseLoopAnimationName => CurrentPhase?.LoopAnimationName ?? string.Empty;
		private float PhaseMinSpeedScale => CurrentPhase?.MinSpeedScale ?? 1f;
		private float PhaseMaxSpeedScale => CurrentPhase?.MaxSpeedScale ?? 1f;
		private float PhaseSpeedScaleLerpSpeed => CurrentPhase?.SpeedScaleLerpSpeed ?? 0f;

		public override void _EnterTree()
		{
			AddToGroup(ProgressGroup);
		}

		public override void _ExitTree()
		{
			// 静态事件必须成对退订（GameMemoryService 只退订了实例事件，别照抄它那段）
			GameActor.DeathFinalized -= OnActorDeathFinalized;
			UnsubscribeCutsceneManager();
		}

		public override void _Ready()
		{
			if (!ProgressEnabled)
			{
				// 没配分段 = 本关不用进度：不计数、不武装、不驱动（消费者读到的 Progress 恒 0，与"场景里没有进度源"同语义）
				_animPlayer = null;
				GD.PushWarning($"{Name}: 未配置 Phases（分段），进度与到站都不会生效");
				return;
			}

			// 分段模式：第 0 段的参数（含上升动画播放器）在 _Ready 就位，消费者的首帧就能读到正确进度
			BeginPhase(0);

			GameActor.DeathFinalized += OnActorDeathFinalized;

			// 需求数为 0 = 开局即满：状态先亮出来（消费者首帧就该读到 1），到站判定延到首个物理帧
			// ——_Ready 里就武装触发器可能当场开始放过场，而彼时别的节点/玩家还没就绪。
			if (PhaseTotalKillsForFull <= 0)
			{
				Progress = ComputeProgress();
				if (EnableDebugLogs) GD.Print($"{Name}: 需求击杀数为 0 → 开局即满进度");
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			// 到站判定补一次：正常路径由 AddKill → CheckArrival 触发，"开局即满"没有击杀事件来触发它
			if (!_arrivalChecked)
			{
				_arrivalChecked = true;
				if (Progress >= 1f) CheckArrival();
			}

			TickArrivalArm((float)delta);
			if (!DriveAscendVisual || _controlReleased) return;
			if (_animPlayer == null || !GodotObject.IsInstanceValid(_animPlayer)) return;

			if (!VisualDriveActive)
			{
				// 只记数、不碰 SpeedScale：过场可能正在播 up/opening（阻塞式，写 0 会卡死过场）
				if (!IsLoopAnimationActive()) return;

				VisualDriveActive = true;
				_speedScaleBeforeDrive = _animPlayer.SpeedScale; // 以过场设定的值为起点，避免速度跳变
				if (EnableDebugLogs)
					GD.Print($"{Name}: 接管 {PhaseLoopAnimationName} 速度控制（起点 {_speedScaleBeforeDrive:F2}）");
			}

			if (!IsLoopAnimationActive())
			{
				// 循环被换掉/停掉（例如过场切了别的动画）→ 归还控制权
				_animPlayer.SpeedScale = _speedScaleBeforeDrive;
				VisualDriveActive = false;
				return;
			}

			// 到站（进度满）：**只交还速度控制，不暂停**——上升循环继续以接管前的速度跑，
			// 直到到站过场用它自己的动画（up_to_top）把循环顶掉；"停住"发生在过场开始那一刻
			// （OnCutsceneStarted 里 Pause），不是进度满那一刻（两者之间可能还隔着 ArrivalTriggerDelay）。
			// 速度**还原**成接管前的值、不渐减到 0：否则会把一个接近 0 / 非 1 的速度残留给随后在
			// 同一个播放器上播出的到站动画（up_to_top 会几乎不动或跑成两倍速）。
			if (HasArrived)
			{
				_animPlayer.SpeedScale = _speedScaleBeforeDrive;
				VisualDriveActive = false;
				_controlReleased = true;
				if (EnableDebugLogs)
					GD.Print($"{Name}: 进度已满 → 交还速度控制（还原为 {_speedScaleBeforeDrive:F2}，上升循环继续跑到过场接手）");
				return;
			}

			float target = Mathf.Lerp(PhaseMinSpeedScale, PhaseMaxSpeedScale, Progress);
			float lerpSpeed = PhaseSpeedScaleLerpSpeed;
			float current = _animPlayer.SpeedScale;
			_animPlayer.SpeedScale = lerpSpeed <= 0f
				? target
				: Mathf.Lerp(current, target, Mathf.Clamp(lerpSpeed * (float)delta, 0f, 1f));
		}

		/// <summary>当前挂的是不是那支后台循环——**不看 IsPlaying()**（SpeedScale = 0 时各版本语义不一致）。</summary>
		private bool IsLoopAnimationActive()
			=> _animPlayer != null && GodotObject.IsInstanceValid(_animPlayer)
			   && _animPlayer.CurrentAnimation == PhaseLoopAnimationName;

		// ── 分段 ──────────────────────────────────────────────────────────

		/// <summary>按当前段解析上升动画播放器并复位速度接管状态。
		/// **空路径 = 本段不驱动上升视觉**（合法配置，如没有上升动画的阶段）；只在"配了路径却找不到"时警告。</summary>
		private void ApplyPhaseConfig()
		{
			VisualDriveActive = false;
			_speedScaleBeforeDrive = 1f;

			var path = PhaseAnimationPlayerPath;
			_animPlayer = path.IsEmpty ? null : GetNodeOrNull<AnimationPlayer>(path);
			if (DriveAscendVisual && !path.IsEmpty && _animPlayer == null)
				GD.PushWarning($"{Name}: 未找到 AnimationPlayer（{path}），本段的上升视觉不会被驱动");
		}

		/// <summary>切到指定段：复位本段状态 + 按该段参数重配（需求击杀数 / 到站触发器 / 武装延迟 / 上升动画）。
		/// （<see cref="ResetProgress"/> 即它的别名。）
		/// 到站判定**不在这里同步补查**（首个物理帧的 _arrivalChecked 通道负责），避免在 _Ready / 过场信号里当场开过场。</summary>
		public void BeginPhase(int index)
		{
			_phaseIndex = ProgressEnabled ? Mathf.Clamp(index, 0, Phases.Count - 1) : 0;
			_allPhasesDone = false;
			KillCount = 0;
			HasArrived = false;
			_controlReleased = false;
			_arrivalTriggerArmed = false;
			_arrivalArmRemaining = 0f;
			_arrivalChecked = false;

			ApplyPhaseConfig();

			float prev = Progress;
			Progress = ComputeProgress();
			KillCountChanged?.Invoke(KillCount, Progress);
			if (!Mathf.IsEqualApprox(prev, Progress))
				ProgressChanged?.Invoke(Progress);

			if (EnableDebugLogs)
			{
				string phaseId = CurrentPhase?.PhaseId ?? string.Empty;
				GD.Print($"{Name}: 进入第 {_phaseIndex + 1}/{PhaseCount} 段"
					+ (string.IsNullOrEmpty(phaseId) ? "" : $"（{phaseId}）")
					+ $"：需求击杀 {PhaseTotalKillsForFull}，到站触发器 {(PhaseArrivalTriggerPath.IsEmpty ? "(未配)" : PhaseArrivalTriggerPath.ToString())}"
					+ $"，上升动画 {(PhaseAnimationPlayerPath.IsEmpty ? "(不驱动)" : PhaseAnimationPlayerPath.ToString())}");
			}
		}

		/// <summary>进入下一段。分段全部走完 → 只置 <see cref="AllPhasesDone"/> 停手（不再武装/驱动）。
		/// 供：本控制器在"本段到站过场开始"时自动调用（见 <see cref="OnCutsceneStarted"/>）、
		/// 过场动画的 method 轨道、控制台/调试手动调。未配置分段时是空操作。</summary>
		public void BeginNextPhase()
		{
			if (!ProgressEnabled) return;

			int next = _phaseIndex + 1;
			if (next >= Phases.Count)
			{
				_allPhasesDone = true;
				if (EnableDebugLogs) GD.Print($"{Name}: 分段已全部走完（{Phases.Count} 段）");
				return;
			}

			BeginPhase(next);
		}

		// ── 击杀统计 ──────────────────────────────────────────────────────

		private void OnActorDeathFinalized(GameActor actor)
		{
			if (actor == null || !GodotObject.IsInstanceValid(actor)) return;
			if (!actor.IsInGroup("enemies")) return;
			AddKill(1);
		}

		/// <summary>记一次击杀（正常由 DeathFinalized 驱动；无头测试/调试/控制台清场可直接调）。</summary>
		public void AddKill(int amount = 1)
		{
			if (amount <= 0) return;

			KillCount += amount;
			float prev = Progress;
			Progress = ComputeProgress();

			KillCountChanged?.Invoke(KillCount, Progress);
			if (!Mathf.IsEqualApprox(prev, Progress))
				ProgressChanged?.Invoke(Progress);

			CheckArrival();

			if (EnableDebugLogs)
				GD.Print($"{Name}: kill={KillCount} progress={Progress:F3}");
		}

		/// <summary>进度 = 击杀数 / 本段需求数（钳 0..1）；本段需求数为 0 时视为**一直满进度**。
		/// 没配分段（ProgressEnabled=false）= 本关不用进度 → 恒 0（注意与"需求数为 0"是两件事）。</summary>
		private float ComputeProgress()
		{
			if (!ProgressEnabled) return 0f;

			int required = PhaseTotalKillsForFull;
			return required > 0
				? Mathf.Clamp((float)KillCount / required, 0f, 1f)
				: 1f;
		}

		// ── 到站 ──────────────────────────────────────────────────────────

		private void CheckArrival()
		{
			if (HasArrived || Progress < 1f) return;

			HasArrived = true;
			EmitSignal(SignalName.AscendArrived);

			// 先订阅再武装：Arm() 可能立刻触发过场（玩家已在区内），信号在那次调用里同步发出
			SubscribeCutsceneManager();

			_arrivalArmRemaining = Mathf.Max(0f, PhaseArrivalTriggerDelay);
			if (_arrivalArmRemaining <= 0f)
			{
				ArmArrivalTrigger();
			}
			else if (EnableDebugLogs)
			{
				GD.Print($"{Name}: 进度已满 → 交还上升速度控制，{_arrivalArmRemaining:F1}s 后武装到站触发器");
			}
		}

		/// <summary>延迟武装倒计时（每帧跑，不受上升驱动/速度接管影响的早退影响）。</summary>
		private void TickArrivalArm(float delta)
		{
			if (!HasArrived || _arrivalTriggerArmed) return;

			_arrivalArmRemaining -= delta;
			if (_arrivalArmRemaining > 0f) return;

			ArmArrivalTrigger();
		}

		/// <summary>武装到站触发器（幂等）。触发区覆盖平台 = 立刻播；放到出口 = 玩家走过去才播。</summary>
		private void ArmArrivalTrigger()
		{
			if (_arrivalTriggerArmed) return;
			_arrivalTriggerArmed = true;

			if (PhaseArrivalTriggerPath.IsEmpty)
			{
				GD.PushWarning($"{Name}: 未配置到站触发器（ArrivalTriggerPath），进度满后不会播到站过场");
				return;
			}

			GetNodeOrNull<CutsceneTrigger>(PhaseArrivalTriggerPath)?.Arm();
			if (EnableDebugLogs) GD.Print($"{Name}: 到站触发器已武装");
		}

		private void SubscribeCutsceneManager()
		{
			if (_cutsceneManager != null && GodotObject.IsInstanceValid(_cutsceneManager)) return;

			_cutsceneManager = GetTree()?.GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
			if (_cutsceneManager != null)
			{
				_cutsceneManager.CutsceneStarted += OnCutsceneStarted;
			}
		}

		private void UnsubscribeCutsceneManager()
		{
			if (_cutsceneManager != null && GodotObject.IsInstanceValid(_cutsceneManager))
			{
				_cutsceneManager.CutsceneStarted -= OnCutsceneStarted;
			}
			_cutsceneManager = null;
		}

		/// <summary>到站过场开始：**暂停上升循环**（保持电梯当前姿态），之后该播放器交给过场自由使用。
		/// 判据是"循环仍是当前动画"而不是"控制器还在驱动"——进度满时已经交还了速度控制，但那时**不该停**：
		/// 停的时机是过场开始（进度满与过场之间可能隔着 ArrivalTriggerDelay）。
		/// 反过来，过场若已经切了自己的动画（up / opening / up_to_top），这里就一个字节都不碰——
		/// Pause 会把过场正在播的动画冻住。
		/// 用 Pause 而不是把 SpeedScale 压到 0：后者会让"在同一播放器上播新动画"变成时间不走的假播放
		/// （IsPlaying 恒真 → WaitForCompletion 永久挂起，up_to_top 那类到站动画会卡死过场）。</summary>
		private void OnCutsceneStarted(string sequenceId)
		{
			// ① 先停上升循环（用的是"本段"的播放器；下一行换段后 _animPlayer 就被换成新段的那个了）
			if (_animPlayer != null && GodotObject.IsInstanceValid(_animPlayer) && IsLoopAnimationActive())
			{
				_animPlayer.SpeedScale = _speedScaleBeforeDrive;
				_animPlayer.Pause();
				VisualDriveActive = false;
				_controlReleased = true;
				if (EnableDebugLogs) GD.Print($"{Name}: 到站过场开始（{sequenceId}）→ 暂停上升循环（保持姿态）");
			}

			// ② 若正是**本段到站触发器上挂的那条序列** → 本段到此结束，**在过场开头就换段**：
			// 复位的 Progress=0 必须发生在"过场自己生成下一段敌人"之前，否则那些敌人（本体/机械臂读的是同一个
			// Progress）会在生成的第一帧就按"满进度收工"自毁（FreeOnFinishArrive 时直接消失）。
			// 判据用触发器上的序列 id 而不是另配字段：触发器 = 本段那条过场，它一开始即本段结束。
			if (!ProgressEnabled || _allPhasesDone || !HasArrived) return;

			string? expected = ResolvePhaseArrivalSequenceId();
			if (string.IsNullOrEmpty(expected) || !string.Equals(expected, sequenceId, StringComparison.Ordinal))
				return;

			if (EnableDebugLogs) GD.Print($"{Name}: 本段到站过场（{sequenceId}）开始 → 进入下一段");
			BeginNextPhase();
		}

		/// <summary>本段到站触发器上挂的序列 id（"过场开始即换段"的匹配依据）。
		/// 触发器/序列任一缺失 = 空 → 不自动换段（可改用 method 轨道显式调 <see cref="BeginNextPhase"/>）。</summary>
		private string? ResolvePhaseArrivalSequenceId()
		{
			var path = PhaseArrivalTriggerPath;
			if (path.IsEmpty) return null;

			var trigger = GetNodeOrNull<CutsceneTrigger>(path);
			return trigger?.Sequence?.SequenceId;
		}

		// ── 工具 ──────────────────────────────────────────────────────────

		/// <summary>复位并重新进入**当前这一段的开头**（等价于 <see cref="BeginPhase"/> 传当前索引）。
		/// 需求击杀数为 0 的段复位后仍是"满进度"，由 _PhysicsProcess 的首帧补查重新走到站。
		/// 进度回退也会被消费者看见（例如本体会据此解除收工）——那正是"重来一段"想要的语义。</summary>
		public void ResetProgress() => BeginPhase(_phaseIndex);

		/// <summary>按组查找（Boss 侧、UI 等都走这个入口）。</summary>
		public static KillProgressAscendController? Find(Node context)
			=> context?.GetTree()?.GetFirstNodeInGroup(ProgressGroup) as KillProgressAscendController;
	}
}
