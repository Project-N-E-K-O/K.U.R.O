using System.Collections.Generic;
using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 扇形三束激光：三条束从**同一原点**出发、末端落在**同一条底边**上（三角形顶点→底边的类比）——
	/// 中束沿 <see cref="AngleDegrees"/>，两侧束相对中束 ±<see cref="SpreadDegrees"/>，
	/// 长度全部由几何推导：Lᵢ = BaseDistance / cos θᵢ（θᵢ = 当前夹角）。
	/// 于是中束最短（= BaseDistance）、两侧更长，但三条端点共线于"过原点、垂直于中束、距原点 D"的底边。
	///
	/// 时间轴（本节点自己的时钟，子束只是零件）：**底边推进速度**是这套时间轴的基本单位——
	/// 三条束共用同一个"已推进基距"，各束长度 = 已推进基距 / cos θᵢ，所以速度填的是扇形张开的速度，
	/// 两侧束会按 1/cos θ 自动更快（不是三条各调）。
	///   BeamDelay（前摇）→ 前段（速度 <see cref="SpreadSpeed"/>，时长 <see cref="SpreadPauseAfter"/>）
	///   → 停顿 <see cref="SpreadPauseDuration"/> 秒（进度冻结）→ 后段（速度 <see cref="SpreadResumeSpeed"/>，
	///     0 = 与前段同速）把剩下的基距推完 → 完全张开 → FadeDuration 淡出。
	///   · 停顿点 = 前段跑到的基距（推导，不需要另配）：<see cref="SpreadPauseAfter"/> 秒 × 前段速度；
	///   · 夹角按同一条"已推进基距"分两段线性映射：0 → <see cref="SpreadPauseDegrees"/>（停顿点角度）
	///     → <see cref="SpreadDegrees"/>（终态角度）——两段角度可以不同（"先张开一点、停一下、再全张"
	///     就把前者调小；后者留 ≤ 0 则与进度成正比 = 只用终态角度一个旋钮）；
	///   · 暂停期间"进度冻结"而不是"慢慢长"——要"缓张"就把后段速度调小，那是后段的事；
	///   · 前段就跑满基距时提前完全张开，剩下的停顿/后段时间变成"保持全开"（可当"张开→蓄力"用）；
	///   · **全程结算**：张开、停顿、继续张开期间都在打人（可见即挨打）。
	///
	/// 实现方式：子束自身的生长时钟被让位——子束 GrowDuration 强制 0、BeamDuration 下发为"整段张开"
	/// （含停顿），长度/宽度由本节点每帧下发的 <see cref="LaserBeamVisualBase.ExternalGrowProgress"/> 驱动。
	/// 因为伤害窗口 = "生长完成 → 全亮结束"，grow 归零就立刻开窗、全亮段 = 整段张开，
	/// 于是"张开多久就打多久"自动成立，不需要另开一条伤害通道。
	/// （所以本节点不导出 GrowDuration / BeamDuration——张开的时间轴就是速度 + 两段时长这一套。）
	///
	/// 结构：本节点 + 三个 <see cref="RogueAIOverloadBeam"/> 实例（BeamMinus / BeamCenter / BeamPlus）——
	/// 单束的视觉、判定带、周期 tick 结算全部原样复用；本脚本另外负责：
	///   1. 每帧推导并下发几何（方向 + 长度）与张开进度；
	///   2. 把根上的**一套**共享参数（时序 / 伤害 / 宽度 / 判定带）转发给三条束，并把
	///      AttackEffectEntry 注入的 IFollowAnchor / IAttackerProvider 转发下去——
	///      作者流程与单束完全一致（PropertyOverrides 打在根上就能生效），
	///      既不用进子场景挨个改，也不会出现三条束参数漂移。
	/// 因此三条束在 Inspector 里的几何/共享值只是编辑器预览值，运行时一律以根为准
	/// （运行时改根上的值后调 <see cref="PushConfig"/> 重新下发）。
	///
	/// 伤害语义：三条束各自判定、各自 tick 账本——束与束之间的空隙不结算（与视觉一致，
	/// 符合"看得见才挨打"）。代价是目标贴脸站在原点处会同时落在三条带里（三倍 tick）；
	/// 按"可见即可伤"保留，若要合并成一个三角形判定区另说。
	///
	/// 生命周期：三条束同相位 → 先后到点自毁；三条全部销毁后本节点自毁。
	/// 兜底时长 <see cref="Lifetime"/> ≤ 0 时自动推导，正数按手动值（过短会告警）。
	/// </summary>
	public partial class FanBeam : Node2D, IFollowAnchor, IAttackerProvider
	{
		/// <summary>两侧束相对中束的夹角上限（度）：cos 趋于 0 时推导长度发散。导出 Range 与运行时钳制都用它。</summary>
		private const float MaxSpreadDegrees = 75f;

		/// <summary>自动兜底时长的余量（秒）：光点/光束收尾各留一点，避免刚好卡在边界。</summary>
		private const float LifetimeAutoMargin = 0.2f;

		[ExportCategory("Nodes 节点")]
		/// <summary>相对角 −N 的那条束。</summary>
		[Export] public NodePath MinusBeamPath { get; set; } = new("BeamMinus");
		/// <summary>中束（相对角 0）。</summary>
		[Export] public NodePath CenterBeamPath { get; set; } = new("BeamCenter");
		/// <summary>相对角 +N 的那条束。</summary>
		[Export] public NodePath PlusBeamPath { get; set; } = new("BeamPlus");

		[ExportCategory("Geometry 几何")]
		/// <summary>基准方向（度）：中束方向，90 = 垂直向下。两侧束 = 基准 ∓ <see cref="SpreadDegrees"/>。</summary>
		[Export(PropertyHint.Range, "-180,180,1")] public float AngleDegrees { get; set; } = 90f;
		/// <summary>两侧束相对中束的**终态**夹角 N（度）：三条束长度由此推导（L = BaseDistance / cos θ），不要逐条手配。</summary>
		[Export(PropertyHint.Range, "0,75,1")] public float SpreadDegrees { get; set; } = 20f;
		/// <summary>顶点到底边的垂直距离 D（px）= 中束长度：两侧束更长（D / cos N），三条端点在完全张开时共线于底边。</summary>
		[Export(PropertyHint.Range, "0,8000,10")] public float BaseDistance { get; set; } = 4000f;
		/// <summary>张开起点长度（px，相对中束）：三条束各按 1/cos 推导（保持相似三角形）；0 = 从原点长出。</summary>
		[Export(PropertyHint.Range, "0,3000,10")] public float MinLength { get; set; }

		[ExportCategory("Opening 张开")]
		/// <summary>前摇（秒）：等待这段时间后开始张开。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float BeamDelay { get; set; }
		/// <summary>前段速度（px/s，作用在**基距**上：每秒把底边往外推多少像素）。
		/// 三条束各按 1/cos θ 折算成自己的延长速度（中束最慢、两侧更快），所以这里填的是"扇形张开的速度"。
		/// ≤ 0 = 自动取 BaseDistance px/s（1 秒张开完）并告警。</summary>
		[Export(PropertyHint.Range, "0,20000,10")] public float SpreadSpeed { get; set; } = 3000f;
		/// <summary>停顿起点 = **前段时长**（秒）：以 <see cref="SpreadSpeed"/> 张开这么多秒后开始停顿
		/// （0 = 不停顿；前段就跑满基距时提前完全张开，剩下的停顿变成"完全张开保持"）。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float SpreadPauseAfter { get; set; }
		/// <summary>停顿时长 Y（秒）：进度冻结 Y 秒——**额外加时**，不计入速度；0 = 不停顿。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float SpreadPauseDuration { get; set; }
		/// <summary>停顿点处的张开角度（度）：前段张开到这么多后停顿，之后继续张到 <see cref="SpreadDegrees"/>。
		/// 两者可以不同——"先张开一点、停一下、再全张"就设小；想"先张满、停一下、再收窄一点"就设大。
		/// ≤ 0 = 与进度成正比（= SpreadDegrees × 停顿点进度），即只用一个终态角度。</summary>
		[Export(PropertyHint.Range, "0,75,1")] public float SpreadPauseDegrees { get; set; }
		/// <summary>后段速度（px/s，与 <see cref="SpreadSpeed"/> 同口径）：停顿结束后用这个速度把剩下的基距推完。
		/// ≤ 0 = 与前段同速（只想插个停顿就留 0）。</summary>
		[Export(PropertyHint.Range, "0,20000,10")] public float SpreadResumeSpeed { get; set; }
		/// <summary>完全张开后的淡出时长（秒）。</summary>
		[Export] public float FadeDuration { get; set; } = 0.15f;

		[ExportCategory("Beam 光束")]
		/// <summary>核心光束宽度（px）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float BeamWidth { get; set; } = 100f;
		/// <summary>光晕宽度（px）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float GlowWidth { get; set; } = 200f;

		[ExportCategory("Spotlight 光点")]
		/// <summary>光点延迟时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float SpotlightDelay { get; set; }
		/// <summary>光点淡入时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeIn { get; set; } = 0.15f;
		/// <summary>光点独立存活时长（秒）。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float SpotlightDuration { get; set; } = 0.6f;
		/// <summary>光点淡出时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeOut { get; set; } = 0.25f;

		[ExportCategory("Lifetime 生命周期")]
		/// <summary>兜底总时长（秒）：**≤ 0 = 自动推导**（前摇 + 张开总时长 + 淡出，与光点寿命取大，再加余量）；
		/// 填正数按手动值，短于实际需要时告警。</summary>
		[Export] public float Lifetime { get; set; }

		[ExportCategory("Detection 判定")]
		/// <summary>判定带垂直半高（px）：带长随光束生长，末端同样停在底边上。</summary>
		[Export(PropertyHint.Range, "10,500,1")] public float DetectionRadius { get; set; } = 150f;

		[ExportCategory("Damage 伤害")]
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")] public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.All;
		[Export] public bool AllowSelfDamage { get; set; }
		[Export(PropertyHint.Range, "0,500,1")] public int Damage { get; set; } = 50;
		/// <summary>重复伤害间隔（秒）：每过这段时间清空一次已伤害账本；0 = 每束只打一次。</summary>
		[Export(PropertyHint.Range, "0,3,0.05")] public float DamageTickInterval { get; set; } = 0.25f;

		[ExportCategory("Knockback 击退")]
		/// <summary>击退距离（沿各自光束轴向）。</summary>
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance { get; set; } = 100f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.28f;

		private readonly List<(RogueAIOverloadBeam Beam, float RelSign)> _beams = new();
		private bool _resolved;
		private float _elapsed;
		/// <summary>最近一次已下发的张开进度/夹角：都没变就不重复写三条束。</summary>
		private float _appliedProgress = float.NaN;
		private float _appliedSpread = float.NaN;
		private Node2D? _followAnchor;
		private GameActor? _attacker;

		/// <summary>跟随锚点（生成方注入）：转发给三条束，各束用自带的 SyncToAnchor 各自跟随。</summary>
		public Node2D? FollowAnchor
		{
			get => _followAnchor;
			set { _followAnchor = value; ForwardTo(beam => beam.FollowAnchor = value); }
		}

		/// <summary>攻击来源（生成方注入）：转发给三条束（自伤保护 / 阵营过滤 / 击退来源）。</summary>
		public GameActor? Attacker
		{
			get => _attacker;
			set { _attacker = value; ForwardTo(beam => beam.Attacker = value); }
		}

		public override void _Ready()
		{
			ResolveBeams();
			if (_beams.Count == 0)
			{
				GD.PushWarning($"{Name}: 三条束一条都没找到，扇束不生效");
				QueueFree();
				return;
			}

			PushConfig();
		}

		public override void _Process(double delta)
		{
			if (_beams.Count == 0) return;

			// 三条束同相位 → 到点先后自毁；全部销毁后本节点自毁（自身不另开生命周期时钟）
			bool anyAlive = false;
			foreach (var (beam, _) in _beams)
				if (GodotObject.IsInstanceValid(beam) && !beam.IsQueuedForDeletion())
				{
					anyAlive = true;
					break;
				}
			if (!anyAlive)
			{
				QueueFree();
				return;
			}

			_elapsed += (float)delta;

			float progress = ResolveOpeningProgress();
			float spread = ResolveSpreadDegrees(progress);
			if (Mathf.IsEqualApprox(progress, _appliedProgress) && Mathf.IsEqualApprox(spread, _appliedSpread))
				return;

			_appliedProgress = progress;
			_appliedSpread = spread;

			foreach (var (beam, relSign) in _beams)
				if (GodotObject.IsInstanceValid(beam)) ApplyOpening(beam, relSign, progress, spread);
		}

		/// <summary>把根上的几何与共享参数下发给三条束（_Ready 自动调用一次；运行时改根上的值后手动再调）。</summary>
		public void PushConfig()
		{
			float spreadMax = Mathf.Clamp(SpreadDegrees, 0f, MaxSpreadDegrees);
			if (!Mathf.IsEqualApprox(spreadMax, SpreadDegrees))
				GD.PushWarning($"{Name}: SpreadDegrees={SpreadDegrees} 超出 0~{MaxSpreadDegrees}（cos→0 长度发散），已按 {spreadMax} 下发");
			if (BaseDistance <= 0f)
				GD.PushWarning($"{Name}: BaseDistance 必须 > 0（当前 {BaseDistance}），几何不会下发");
			if (SpreadSpeed <= 0f && BaseDistance > 0f)
				GD.PushWarning($"{Name}: SpreadSpeed ≤ 0 → 前段速度自动取 BaseDistance px/s（1 秒张开完，当前 {BaseDistance:F0}）");
			if (SpreadPauseDegrees > 0f && ResolvePauseSeconds() <= 0f)
				GD.PushWarning($"{Name}: 配了 SpreadPauseDegrees 但没有停顿（SpreadPauseAfter/SpreadPauseDuration）——该角度不会生效");

			float opening = ResolveOpeningSeconds();
			float need = Mathf.Max(BeamDelay, 0f) + opening + FadeDuration;
			if (Lifetime > 0f && Lifetime < need)
				GD.PushWarning($"{Name}: Lifetime={Lifetime:F2} 短于前摇+张开+淡出（需 {need:F2}）——光束会被兜底提前回收（填 ≤ 0 可自动推导）");

			float progress = ResolveOpeningProgress();
			float spread = ResolveSpreadDegrees(progress);
			_appliedProgress = progress;
			_appliedSpread = spread;

			foreach (var (beam, relSign) in _beams)
			{
				if (!GodotObject.IsInstanceValid(beam)) continue;

				ApplyOpening(beam, relSign, progress, spread);
				PushShared(beam);
			}
		}

		/// <summary>把张开状态落到一条束上：长度/宽度走外部进度；角度与长度按**当前夹角**推导
		/// （末端因此任意时刻都共线于"距顶点 = 已推进基距"的底边）。</summary>
		private void ApplyOpening(RogueAIOverloadBeam beam, float relSign, float progress, float spread)
		{
			beam.ExternalGrowProgress = progress;
			if (BaseDistance <= 0f) return;

			float rel = relSign * spread;
			float invCos = 1f / Mathf.Cos(Mathf.DegToRad(rel));
			beam.AngleDegrees = AngleDegrees + rel;
			beam.MaxLength = BaseDistance * invCos;
			beam.MinLength = Mathf.Max(MinLength, 0f) * invCos;
		}

		/// <summary>共享参数（三条束必须同值的那一套）。</summary>
		private void PushShared(RogueAIOverloadBeam beam)
		{
			beam.BeamDelay = BeamDelay;
			// 子束自身的生长时钟让位：张开进度由本节点每帧下发（ExternalGrowProgress）
			beam.GrowDuration = 0f;
			// 子束的"全亮段" = 整段张开（含停顿）→ 伤害窗口正好覆盖张开全程（可见即挨打），淡出在其后
			beam.BeamDuration = ResolveOpeningSeconds();
			beam.FadeDuration = FadeDuration;
			beam.SpotlightDelay = SpotlightDelay;
			beam.SpotlightFadeIn = SpotlightFadeIn;
			beam.SpotlightDuration = SpotlightDuration;
			beam.SpotlightFadeOut = SpotlightFadeOut;
			// 兜底总时长已由子束 _Ready 按场景值初始化过 → 走 SetTotalLifetime 重算倒计时
			beam.SetTotalLifetime(ResolveLifetime());
			beam.BeamWidth = BeamWidth;
			beam.GlowWidth = GlowWidth;
			// 宽度不随张开进度从 0 长出来：张开进度是"推到哪儿了"，宽度自始至终是配置值（淡出收窄照旧）
			beam.WidthFollowsGrow = false;
			beam.DetectionRadius = DetectionRadius;
			beam.TargetableFactions = TargetableFactions;
			beam.AllowSelfDamage = AllowSelfDamage;
			beam.Damage = Damage;
			beam.DamageTickInterval = DamageTickInterval;
			beam.KnockbackDistance = KnockbackDistance;
			beam.KnockbackDuration = KnockbackDuration;
			beam.Attacker = _attacker;
		}

		/// <summary>张开进度 0~1 = 已推进基距 / BaseDistance。</summary>
		private float ResolveOpeningProgress()
		{
			float d = Mathf.Max(BaseDistance, 0f);
			if (d <= 0f) return 0f;

			float phase = Mathf.Max(_elapsed - Mathf.Max(BeamDelay, 0f), 0f);
			return Mathf.Clamp(ResolveCoveredDistance(phase) / d, 0f, 1f);
		}

		/// <summary>已推进的基距（px）：前段 v₁·t → 停顿冻结（停顿点 = 前段跑到的基距，推导）→ 后段 v₂·t。
		/// 上限 BaseDistance——前段就跑满时提前完全张开，剩下的时间变成"保持全开"。</summary>
		private float ResolveCoveredDistance(float phase)
		{
			float d = Mathf.Max(BaseDistance, 0f);
			if (d <= 0f) return 0f;

			float pre = Mathf.Max(SpreadPauseAfter, 0f);
			float v1 = ResolveSpreadSpeed();
			if (phase <= pre) return Mathf.Min(Mathf.Max(phase, 0f) * v1, d);

			float covered = Mathf.Min(pre * v1, d);
			float pause = ResolvePauseSeconds();
			if (phase < pre + pause) return covered;                       // 停顿：冻结
			return Mathf.Min(covered + ResolveResumeSpeed() * (phase - pre - pause), d);
		}

		/// <summary>张开总时长（秒，不含前摇与淡出）= 前段 + 停顿 + 后段（前段跑满则提前进入保持）。</summary>
		private float ResolveOpeningSeconds()
		{
			float d = Mathf.Max(BaseDistance, 0f);
			if (d <= 0f) return 0f;

			float pre = Mathf.Max(SpreadPauseAfter, 0f);
			float preSeconds = Mathf.Min(pre, d / ResolveSpreadSpeed());
			float rest = Mathf.Max(d - preSeconds * ResolveSpreadSpeed(), 0f);
			float restSeconds = rest > 0f ? rest / ResolveResumeSpeed() : 0f;
			return preSeconds + ResolvePauseSeconds() + restSeconds;
		}

		/// <summary>前段速度（px/s，基距口径）：未配置时取 BaseDistance px/s（1 秒张开完）。</summary>
		private float ResolveSpreadSpeed()
			=> SpreadSpeed > 0f ? SpreadSpeed : Mathf.Max(BaseDistance, 1f);

		/// <summary>当前夹角（度）：按**已推进基距**分两段线性映射——前段 0 → 停顿点角度，
		/// 后段 停顿点角度 → 终态角度（<see cref="SpreadDegrees"/>）。停顿期间进度冻结 → 角度自动冻结。
		/// 没有停顿（或前段就跑满基距）时退化成单一角度随进度。</summary>
		private float ResolveSpreadDegrees(float progress)
		{
			float finalDeg = Mathf.Clamp(SpreadDegrees, 0f, MaxSpreadDegrees);
			float pauseProgress = ResolvePauseProgress();
			if (pauseProgress <= 0f || pauseProgress >= 1f)
				return finalDeg * progress;

			float pauseDeg = SpreadPauseDegrees > 0f
				? Mathf.Min(SpreadPauseDegrees, MaxSpreadDegrees)
				: finalDeg * pauseProgress;                       // 未配置 = 与进度成正比（只用一个终态角度）
			return progress <= pauseProgress
				? pauseDeg * (progress / pauseProgress)
				: pauseDeg + (finalDeg - pauseDeg) * ((progress - pauseProgress) / (1f - pauseProgress));
		}

		/// <summary>停顿点进度 0~1（推导 = 前段时长 × 前段速度 / BaseDistance）；无停顿或前段为空 → 0。</summary>
		private float ResolvePauseProgress()
		{
			float d = Mathf.Max(BaseDistance, 0f);
			if (d <= 0f || ResolvePauseSeconds() <= 0f) return 0f;
			return Mathf.Clamp(Mathf.Max(SpreadPauseAfter, 0f) * ResolveSpreadSpeed() / d, 0f, 1f);
		}

		/// <summary>后段速度（px/s）：未配置 = 与前段同速。</summary>
		private float ResolveResumeSpeed()
			=> SpreadResumeSpeed > 0f ? SpreadResumeSpeed : ResolveSpreadSpeed();

		/// <summary>有效停顿时长（秒）：Y > 0 且前段时长 N > 0 才停。</summary>
		private float ResolvePauseSeconds()
			=> SpreadPauseDuration > 0f && SpreadPauseAfter > 0f ? SpreadPauseDuration : 0f;

		/// <summary>兜底总时长（秒）：Lifetime > 0 用手动值，≤ 0 自动推导
		/// （光束寿命与光点寿命取大 + 余量）。</summary>
		private float ResolveLifetime()
		{
			if (Lifetime > 0f) return Lifetime;

			float beamLife = Mathf.Max(BeamDelay, 0f) + ResolveOpeningSeconds() + FadeDuration;
			float spotLife = Mathf.Max(SpotlightDelay, 0f) + Mathf.Max(SpotlightFadeIn, 0f)
				+ Mathf.Max(SpotlightDuration, 0f) + Mathf.Max(SpotlightFadeOut, 0f);
			return Mathf.Max(beamLife, spotLife) + LifetimeAutoMargin;
		}

		private void ResolveBeams()
		{
			if (_resolved) return;
			_resolved = true;

			AddBeam(MinusBeamPath, -1f, "Minus");
			AddBeam(CenterBeamPath, 0f, "Center");
			AddBeam(PlusBeamPath, 1f, "Plus");
		}

		private void AddBeam(NodePath path, float relSign, string role)
		{
			var beam = path != null && !path.IsEmpty ? GetNodeOrNull<RogueAIOverloadBeam>(path) : null;
			if (beam == null)
			{
				GD.PushWarning($"{Name}: {role} 束节点未找到（{path}），本条不会出现");
				return;
			}
			_beams.Add((beam, relSign));
		}

		/// <summary>生成方注入发生在 AddChild 之前（此时子节点已存在）→ setter 里惰性解析并按需转发。</summary>
		private void ForwardTo(System.Action<RogueAIOverloadBeam> apply)
		{
			ResolveBeams();
			foreach (var (beam, _) in _beams)
				if (GodotObject.IsInstanceValid(beam)) apply(beam);
		}
	}
}
