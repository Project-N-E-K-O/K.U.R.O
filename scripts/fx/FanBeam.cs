using System.Collections.Generic;
using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 扇形 N 束激光（N = <see cref="BeamCount"/>，1/3/5…都行）：所有束从**同一原点**出发、
	/// 末端落在**同一条底边**上（三角形顶点→底边的类比）——中束沿 <see cref="AngleDegrees"/>，
	/// 全体沿 [−S, +S] 均分（S = <see cref="SpreadDegrees"/> = **最外侧束的半角**，与 N 无关），
	/// 长度全部由几何推导：Lᵢ = BaseDistance / cos θᵢ（θᵢ = 当前夹角）。
	/// 于是中间的束最短（= BaseDistance）、外侧更长，但所有端点共线于"过原点、垂直于中束、距原点 D"的底边。
	///
	/// 时间轴（本节点自己的时钟，子束只是零件）：**每段各自带 {时长, 距离, 结束角}**——
	/// 底边"已推进基距"由三段接力推出（段内匀速），所以**总量全部推导**：
	/// 基距 D = 三段距离之和、各段速度 = 该段距离 ÷ 该段时长、角度 = 四个折点的折线。
	/// **没有"整体变量"**（不再有总距离/总速度字段）→ 各段互不冲突，任何配置下三段都恰好推满 D。
	///   BeamDelay（前摇）→ 段 1（S1 秒推 S1 距离，半角张到 S1 结束角）→ 停 1（P1 秒，进度冻结）
	///   → 段 2（→ 段 2 结束角）→ 停 2（P2 秒）→ 段 3（推完剩下的，半角到 S3 结束角 = 终态）
	///   → 完全张开 → FadeDuration 淡出。
	///   · 夹角按**已推进基距**四点折线映射（0 → 段1结束角 → 段2结束角 → 段3结束角），
	///     映射出来的是**半角 S(t)**，各束再按 N 均分到 [−S(t), +S(t)]；停顿期间进度冻结 → 角度自动冻结；
	///   · **伤害**：每段有自己的开关（<see cref="DamageInStage1"/> / <see cref="DamageInStage2"/> /
	///     <see cref="DamageInStage3"/>，停顿时段沿用其前一段的开关；默认段 3 关 = 只做视觉）；
	///   · **伪体积**：<see cref="Pseudo3DFromStage"/> 段起点之前恒 0（平面），之后在
	///     <see cref="Pseudo3DGrowSeconds"/> 内从 0 线性长到 1（默认从段 3 起、截至张开结束）。
	///
	/// 实现方式：子束自身的生长时钟被让位——子束 GrowDuration 强制 0、BeamDuration 下发为"整段张开"
	/// （含停顿），长度/宽度由本节点每帧下发的 <see cref="LaserBeamVisualBase.ExternalGrowProgress"/> 驱动。
	/// 因为伤害窗口 = "生长完成 → 全亮结束"，grow 归零就立刻开窗、全亮段 = 整段张开，
	/// 于是"张开多久就打多久"自动成立，不需要另开一条伤害通道。
	/// （所以本节点不导出 GrowDuration / BeamDuration——张开的时间轴就是速度 + 两段时长这一套。）
	///
	/// 结构：场景里**只放一条** <see cref="RogueAIOverloadBeam"/> 当模板（摆在中束方向即可，它的几何/共享值
	/// 只是编辑器预览值），运行时按 <see cref="BeamCount"/> 复制/裁剪出 N 条——用 `Duplicate()` 复制，
	/// 连编辑器里调过的覆盖值（z_index 等）一起带过去，所以不需要另配 BeamScene、也不用逐属性复制；
	/// 材质与判定带形状由子束自己的 _Ready 按实例再复制一遍（多实例不会串）。本脚本另外负责：
	///   1. 对齐数量与布局（按 N 均分角度 + 推导长度 + 每帧下发张开进度）；
	///   2. 把根上的**一套**共享参数（时序 / 伤害 / 宽度 / 判定带）转发给全部束，并把
	///      AttackEffectEntry 注入的 IFollowAnchor / IAttackerProvider 转发下去
	///      （注入发生在 AddChild(本节点) 之前——那时副本还不存在，所以复制完会补推一次）；
	///   3. 锚点跟随可用 <see cref="SyncToAnchor"/> 关掉（默认跟随）。
	/// 运行时改根上的值后调 <see cref="PushConfig"/> 重新下发（数量/布局也在那时对齐）。
	///
	/// 伤害语义：**每条束各自判定、各自 tick 账本**——束与束之间的空隙不结算（与视觉一致，
	/// 符合"看得见才挨打"）。N 越大，重叠区（贴脸 / 远端带重叠处）会被多条束同时结算，
	/// 总伤害随之上升：这是有意保留的口径（要"整个扇束对同一目标每个 tick 只结算一次"需另加共享账本）。
	///
	/// 生命周期：所有束同相位 → 先后到点自毁；全部销毁后本节点自毁。
	/// 兜底时长 <see cref="Lifetime"/> ≤ 0 时自动推导，正数按手动值（过短会告警）。
	/// </summary>
	public partial class FanBeam : Node2D, IFollowAnchor, IAttackerProvider
	{
		/// <summary>两侧束相对中束的夹角上限（度）：cos 趋于 0 时推导长度发散。导出 Range 与运行时钳制都用它。</summary>
		private const float MaxSpreadDegrees = 75f;

		/// <summary>自动兜底时长的余量（秒）：光点/光束收尾各留一点，避免刚好卡在边界。</summary>
		private const float LifetimeAutoMargin = 0.2f;

		[ExportCategory("Nodes")]
		/// <summary>光束条数 N（≥1）：场景里**只放一条**模板束，运行时按这个数字复制/裁剪（Duplicate 会连编辑器里
		/// 调过的覆盖值一起复制）。N=1 单束在中束方向（此时 <see cref="SpreadDegrees"/> 无效）；N=2 在 ±S；
		/// 偶数 N 没有正中束。运行时改它后调 <see cref="PushConfig"/> 重新对齐数量与布局。</summary>
		[Export(PropertyHint.Range, "1,32,1")] public int BeamCount { get; set; } = 3;

		[ExportCategory("Geometry")]
		/// <summary>基准方向（度）= 正中束方向，90 = 垂直向下；其余束在它两侧均分。</summary>
		[Export(PropertyHint.Range, "-180,180,1")] public float AngleDegrees { get; set; } = 90f;
		/// <summary>**最外侧束的半角** S（度，与 N 无关，终态值）：N 条束沿 [−S, +S] 均分，长度由此推导
		/// （L = BaseDistance / cos θ），不要逐条手配。</summary>
		[Export(PropertyHint.Range, "0,75,1")] public float SpreadDegrees { get; set; } = 20f;
		/// <summary>张开起点长度（px，按正中束口径）：各束按 1/cos 推导（保持相似三角形）；0 = 从原点长出。</summary>
		[Export(PropertyHint.Range, "0,3000,10")] public float MinLength { get; set; }

		[ExportCategory("Follow")]
		/// <summary>是否跟随生成方注入的锚点（`IFollowAnchor`：`SpawnMarkerPaths` 里实际用到的 marker，没配就退回敌人根节点）。
		/// 开启：每条束每帧把世界位置**硬对齐**到锚点（无平滑），敌人转身/骨骼浮动都跟得上；
		/// 关闭：停在生成时被摆的位置。注意这个开关只管"锚点跟随"——挂在会动的父节点下时仍会被父节点带着走。
		/// 运行时改它后调 <see cref="PushConfig"/> 重新下发。</summary>
		[Export] public bool SyncToAnchor { get; set; } = true;

		[ExportCategory("Opening")]
		/// <summary>前摇（秒）：等待这段时间后开始张开。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float BeamDelay { get; set; }

		/// <summary>段 1 时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float Stage1Seconds { get; set; } = 0.4f;
		/// <summary>段 1 把底边往外推的距离（px，基距口径）。**三段距离之和 = 基距 D**（总量不再单独导出）。</summary>
		[Export(PropertyHint.Range, "0,8000,10")] public float Stage1Distance { get; set; } = 400f;
		/// <summary>段 1 结束时的张开半角（度）。</summary>
		[Export(PropertyHint.Range, "0,75,1")] public float Stage1EndDegrees { get; set; } = 25f;
		/// <summary>停 1 时长（秒）：0 = 不停。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float Pause1Seconds { get; set; } = 1f;

		/// <summary>段 2 时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float Stage2Seconds { get; set; } = 0.5f;
		/// <summary>段 2 把底边往外推的距离（px）。</summary>
		[Export(PropertyHint.Range, "0,8000,10")] public float Stage2Distance { get; set; } = 800f;
		/// <summary>段 2 结束时的张开半角（度）。</summary>
		[Export(PropertyHint.Range, "0,75,1")] public float Stage2EndDegrees { get; set; } = 50f;
		/// <summary>停 2 时长（秒）：0 = 不停。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float Pause2Seconds { get; set; } = 2f;

		/// <summary>段 3 时长（秒）：收尾段——把剩余基距推完（= 完全张开）。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float Stage3Seconds { get; set; } = 1f;
		/// <summary>段 3 把底边往外推的距离（px）。</summary>
		[Export(PropertyHint.Range, "0,8000,10")] public float Stage3Distance { get; set; } = 800f;
		/// <summary>段 3 结束时的张开半角（度）= **终态半角**。</summary>
		[Export(PropertyHint.Range, "0,75,1")] public float Stage3EndDegrees { get; set; } = 75f;

		/// <summary>完全张开后的淡出时长（秒）。</summary>
		[Export] public float FadeDuration { get; set; } = 0.15f;

		[ExportCategory("Pseudo3D")]
		/// <summary>从第几段开始拉伪体积（1/2/3）；0 = 关闭。段起点之前恒 0（保持平面），之后在增长窗口内
		/// 从 0 线性长到 1。子束不是伪体积光束（没实现 <see cref="IPseudo3DBeam"/>）就自然忽略。</summary>
		[Export(PropertyHint.Range, "0,3,1")] public int Pseudo3DFromStage { get; set; } = 3;
		/// <summary>伪体积增长窗口（秒）：从该段起点起算；≤ 0 = 用"从该段起点到张开结束"的剩余时长。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float Pseudo3DGrowSeconds { get; set; }
		/// <summary>透视锥化倾斜角（度）：光束朝镜头倾多少——0 = 平面；越大远端张得越猛
		/// （实际下发 = 本值 × 权重，权重在增长窗口内从 0 长到 1）。</summary>
		[Export(PropertyHint.Range, "0,89,1")] public float Pseudo3DTiltDegrees { get; set; } = 35f;
		/// <summary>透视强度（= shader 的 fov，度）：越小越接近正交（1 ≈ 平面），越大透视越强。
		/// 与倾斜角共同决定远端倍率：1/(1 − clamp(2·tan(fov/2)·sin(tilt), 0, 0.95))。</summary>
		[Export(PropertyHint.Range, "1,179,1")] public float Pseudo3DFovDegrees { get; set; } = 45f;
		/// <summary>转动高光强度（× 权重）：0 = 不转。</summary>
		[Export(PropertyHint.Range, "0,3,0.05")] public float Pseudo3DSpinStrength { get; set; } = 0.7f;

		[ExportCategory("Beam")]
		/// <summary>核心光束宽度（px，基准值；实际宽度 = 本值 × 当前段的宽度系数）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float BeamWidth { get; set; } = 100f;
		/// <summary>光晕宽度（px，基准值；同样乘当前段的宽度系数）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float GlowWidth { get; set; } = 200f;
		/// <summary>段 1 的宽度系数（× BeamWidth / GlowWidth）。</summary>
		[Export(PropertyHint.Range, "0,4,0.05")] public float Stage1WidthScale { get; set; } = 1f;
		/// <summary>段 2 的宽度系数。</summary>
		[Export(PropertyHint.Range, "0,4,0.05")] public float Stage2WidthScale { get; set; } = 1f;
		/// <summary>段 3 的宽度系数。</summary>
		[Export(PropertyHint.Range, "0,4,0.05")] public float Stage3WidthScale { get; set; } = 1f;
		/// <summary>段 1 的光束**视觉层**绘制层级（写进子束的 Visual 子节点；默认 2 = 现在子束上写的值）。
		/// 只改画出来的那棵子树——根节点与判定带不动（判定带不参与绘制）。
		/// z 是整数、**换段即切换**（不做平滑）；填一样就是全程不变。</summary>
		[Export(PropertyHint.Range, "-64,64,1")] public int Stage1ZIndex { get; set; } = 2;
		/// <summary>段 2 的光束绘制层级。</summary>
		[Export(PropertyHint.Range, "-64,64,1")] public int Stage2ZIndex { get; set; } = 2;
		/// <summary>段 3 的光束绘制层级。</summary>
		[Export(PropertyHint.Range, "-64,64,1")] public int Stage3ZIndex { get; set; } = 2;
		/// <summary>宽度跟随平滑（1/s）：实际系数按 `1 − exp(−S·dt)` **持续逼近**当前段的目标系数
		/// （不是瞬间到位，与 P2/伞/浮空炮跟随玩家同一套手感）；≤ 0 = 瞬间到位。
		/// 时间常数 = 1/S：S=4 ≈ 0.25 秒走完约 63%。</summary>
		[Export(PropertyHint.Range, "0,30,0.1")] public float WidthSmoothing { get; set; } = 4f;

		[ExportCategory("Scorch 焦痕")]
		/// <summary>**线型焦痕**场景（连续拖尾；留空 = 不画）。纯视觉、不参与伤害；
		/// 形状/寿命参数在线型拖尾自己的场景上（LaserScorchTrail.tscn）。</summary>
		[Export] public PackedScene? ScorchTrailScene { get; set; }
		/// <summary>只在第几段留焦痕（1/2/3；0 = 关）。默认 2：停顿沿用段 2，但尖端不动就不会继续延长。</summary>
		[Export(PropertyHint.Range, "0,3,1")] public int ScorchStage { get; set; } = 2;

		[ExportCategory("Spotlight")]
		/// <summary>光点延迟时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float SpotlightDelay { get; set; }
		/// <summary>光点淡入时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeIn { get; set; } = 0.15f;
		/// <summary>光点独立存活时长（秒）。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float SpotlightDuration { get; set; } = 0.6f;
		/// <summary>光点淡出时长（秒）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeOut { get; set; } = 0.25f;

		[ExportCategory("Lifetime")]
		/// <summary>兜底总时长（秒）：**≤ 0 = 自动推导**（前摇 + 张开总时长 + 淡出，与光点寿命取大，再加余量）；
		/// 填正数按手动值，短于实际需要时告警。</summary>
		[Export] public float Lifetime { get; set; }

		[ExportCategory("Detection")]
		/// <summary>判定带垂直半高基准（px；带长随光束生长，末端同样停在底边上）。
		/// 实际半高 = 本值 × 当前段的判定系数（<see cref="Stage1DetectionScale"/> 等），**与视觉宽度系数相互独立**。</summary>
		[Export(PropertyHint.Range, "10,500,1")] public float DetectionRadius { get; set; } = 150f;
		/// <summary>段 1 的判定带半高系数（× DetectionRadius）。**独立于视觉宽度系数**——判定宽度按段单独配，
		/// 不再跟着光束视觉宽度走；过渡与视觉宽度同款一阶平滑（停顿期间也在逼近）。</summary>
		[Export(PropertyHint.Range, "0,4,0.05")] public float Stage1DetectionScale { get; set; } = 1f;
		/// <summary>段 2 的判定带半高系数。</summary>
		[Export(PropertyHint.Range, "0,4,0.05")] public float Stage2DetectionScale { get; set; } = 1f;
		/// <summary>段 3 的判定带半高系数。</summary>
		[Export(PropertyHint.Range, "0,4,0.05")] public float Stage3DetectionScale { get; set; } = 1f;

		[ExportCategory("Damage")]
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")] public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.All;
		[Export] public bool AllowSelfDamage { get; set; }
		/// <summary>段 1（含停 1）是否结算伤害。</summary>
		[Export] public bool DamageInStage1 { get; set; } = true;
		/// <summary>段 2（含停 2）是否结算伤害。</summary>
		[Export] public bool DamageInStage2 { get; set; } = true;
		/// <summary>段 3（含完全张开后的保持）是否结算伤害——默认关：段 3 只做视觉（可见但不打人）。</summary>
		[Export] public bool DamageInStage3 { get; set; }
		[Export(PropertyHint.Range, "0,500,1")] public int Damage { get; set; } = 50;
		/// <summary>重复伤害间隔（秒）：每过这段时间清空一次已伤害账本；0 = 每束只打一次。</summary>
		[Export(PropertyHint.Range, "0,3,0.05")] public float DamageTickInterval { get; set; } = 0.25f;
		/// <summary>命中目标后截断（不可穿透，像 LaserBeamA）：**每条束各自**截到最近可命中目标的近边，
		/// 伤害也只结算到首个目标（连同排）；判定带保持全长。默认关（贯穿）。</summary>
		[Export] public bool TruncateOnHit { get; set; }

		[ExportCategory("Knockback")]
		/// <summary>击退距离（沿各自光束轴向）。</summary>
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance { get; set; } = 100f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.28f;

		/// <summary>运行时布局：树序 + 相对系数（−1..+1，按 N 均分）。</summary>
		private readonly List<(RogueAIOverloadBeam Beam, float RelFactor)> _beams = new();
		/// <summary>模板束（场景里树序最前的那条）：复制它的节点树与覆盖值来造副本。</summary>
		private RogueAIOverloadBeam? _templateBeam;
		private float _elapsed;
		/// <summary>最近一次已下发的张开进度/夹角：都没变就不重复写全部束。</summary>
		private float _appliedProgress = float.NaN;
		private float _appliedSpread = float.NaN;
		/// <summary>最近一次已下发的伤害闸状态（每段自己的 DamageInStage*）。</summary>
		private bool _appliedDamageEnabled;
		/// <summary>平滑后的宽度系数（跨帧记忆，一阶滞后用）；NaN = 还没初始化（首次下发直接吸附到当前段目标）。</summary>
		private float _widthScale = float.NaN;
		/// <summary>平滑后的判定带半高系数（与 <see cref="_widthScale"/> 相互独立、同样一阶滞后）。</summary>
		private float _detectionScale = float.NaN;
		/// <summary>每条束的线型焦痕（懒创建；下标与 _beams 对齐）。</summary>
		private readonly List<LaserScorchTrail> _trails = new();
		/// <summary>最近一次已下发的伪 3D 权重（段 3 内 0→1）。</summary>
		private float _appliedPseudo3D = -1f;
		private Node2D? _followAnchor;
		private GameActor? _attacker;

		/// <summary>跟随锚点（生成方注入）：转发给全部束，各束用自带的 SyncToAnchor 各自跟随。
		/// 注入发生在 AddChild 之前（那时副本还不存在）→ setter 只存值，复制完在 ResetBeams 里补推。</summary>
		public Node2D? FollowAnchor
		{
			get => _followAnchor;
			set { _followAnchor = value; ForwardTo(beam => beam.FollowAnchor = value); }
		}

		/// <summary>攻击来源（生成方注入）：转发给全部束（自伤保护 / 阵营过滤 / 击退来源）。</summary>
		public GameActor? Attacker
		{
			get => _attacker;
			set { _attacker = value; ForwardTo(beam => beam.Attacker = value); }
		}

		public override void _Ready()
		{
			if (!ResolveTemplate())
			{
				GD.PushWarning($"{Name}: 子节点里没有光束模板（RogueAIOverloadBeam），扇束不生效");
				QueueFree();
				return;
			}

			PushConfig();
		}

		public override void _Process(double delta)
		{
			if (_beams.Count == 0) return;

			// 所有束同相位 → 到点先后自毁；全部销毁后本节点自毁（自身不另开生命周期时钟）
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

			// 宽度/判定系数：各自持续逼近当前段的目标（1 − exp(−S·dt)，与 P2/伞/浮空炮同款手感）。
			// 放在下面"进度冻结就不重启下发"的判据**之前**——停顿期间进度冻结，但两者仍要继续逼近。
			float targetScale = ResolveStageScale();
			float targetDetection = ResolveStageDetectionScale();
			_widthScale = WidthSmoothing > 0f
				? Mathf.Lerp(_widthScale, targetScale, 1f - Mathf.Exp(-WidthSmoothing * (float)delta))
				: targetScale;
			_detectionScale = WidthSmoothing > 0f
				? Mathf.Lerp(_detectionScale, targetDetection, 1f - Mathf.Exp(-WidthSmoothing * (float)delta))
				: targetDetection;
			foreach (var (beam, _) in _beams)
				if (GodotObject.IsInstanceValid(beam)) ApplyWidth(beam);

			if (float.IsNaN(_widthScale)) _widthScale = ResolveStageScale();   // 首次下发：直接吸附到当前段目标
			if (float.IsNaN(_detectionScale)) _detectionScale = ResolveStageDetectionScale();

			float progress = ResolveOpeningProgress();
			TickScorches();   // 焦痕拖尾：纯视觉，独立于下面的"变化才下发"判据

			float spread = ResolveSpreadDegrees(progress);
			bool damageEnabled = ResolveDamageEnabled();
			float pseudo3D = ResolvePseudo3DWeight();
			if (Mathf.IsEqualApprox(progress, _appliedProgress) && Mathf.IsEqualApprox(spread, _appliedSpread)
				&& damageEnabled == _appliedDamageEnabled && Mathf.IsEqualApprox(pseudo3D, _appliedPseudo3D))
				return;

			_appliedProgress = progress;
			_appliedSpread = spread;
			_appliedDamageEnabled = damageEnabled;
			_appliedPseudo3D = pseudo3D;

			foreach (var (beam, relFactor) in _beams)
			{
				if (!GodotObject.IsInstanceValid(beam)) continue;
				ApplyOpening(beam, relFactor, progress, spread);
				ApplyDamageGate(beam, damageEnabled);
				ApplyPseudo3D(beam, pseudo3D);
			}
		}

		/// <summary>把根上的几何与共享参数下发给全部束（_Ready 自动调用一次；运行时改根上的值后手动再调）。
		/// 会先按 <see cref="BeamCount"/> 对齐数量与布局（复制/裁剪）。</summary>
		public void PushConfig()
		{
			ResetBeams();

			float spreadMax = Mathf.Clamp(SpreadDegrees, 0f, MaxSpreadDegrees);
			if (!Mathf.IsEqualApprox(spreadMax, SpreadDegrees))
				GD.PushWarning($"{Name}: SpreadDegrees={SpreadDegrees} 超出 0~{MaxSpreadDegrees}（cos→0 长度发散），已按 {spreadMax} 下发");
			if (ResolveBaseDistance() <= 0f)
				GD.PushWarning($"{Name}: 三段距离之和为 0（基距 D = 0），几何不会下发");
			if (Mathf.Max(Stage1Seconds, 0f) <= 0f && Mathf.Max(Stage1Distance, 0f) > 0f)
				GD.PushWarning($"{Name}: 段 1 时长为 0 但距离 > 0——该段距离会被瞬间跳过（建议每段都留 > 0 的时长）");
			if (Mathf.Max(Stage2Seconds, 0f) <= 0f && Mathf.Max(Stage2Distance, 0f) > 0f)
				GD.PushWarning($"{Name}: 段 2 时长为 0 但距离 > 0——该段距离会被瞬间跳过（建议每段都留 > 0 的时长）");
			if (Mathf.Max(Stage3Seconds, 0f) <= 0f && Mathf.Max(Stage3Distance, 0f) > 0f)
				GD.PushWarning($"{Name}: 段 3 时长为 0 但距离 > 0——该段距离会被瞬间跳过（建议每段都留 > 0 的时长）");

			float opening = ResolveOpeningSeconds();
			float need = Mathf.Max(BeamDelay, 0f) + opening + FadeDuration;
			if (Lifetime > 0f && Lifetime < need)
				GD.PushWarning($"{Name}: Lifetime={Lifetime:F2} 短于前摇+张开+淡出（需 {need:F2}）——光束会被兜底提前回收（填 ≤ 0 可自动推导）");

			if (float.IsNaN(_widthScale)) _widthScale = ResolveStageScale();   // 首次下发：直接吸附到当前段目标
			if (float.IsNaN(_detectionScale)) _detectionScale = ResolveStageDetectionScale();

			float progress = ResolveOpeningProgress();
			TickScorches();   // 焦痕拖尾：纯视觉，独立于下面的"变化才下发"判据

			float spread = ResolveSpreadDegrees(progress);
			bool damageEnabled = ResolveDamageEnabled();
			float pseudo3D = ResolvePseudo3DWeight();
			_appliedProgress = progress;
			_appliedSpread = spread;
			_appliedDamageEnabled = damageEnabled;
			_appliedPseudo3D = pseudo3D;

			foreach (var (beam, relFactor) in _beams)
			{
				if (!GodotObject.IsInstanceValid(beam)) continue;

				ApplyOpening(beam, relFactor, progress, spread);
				PushShared(beam);
				ApplyWidth(beam);                       // 宽度 = 基准 × 平滑系数（由本处统一写，PushShared 不碰）
				ApplyDamageGate(beam, damageEnabled);   // 必须排在 PushShared 之后（后者会把伤害写回根上的值）
				ApplyPseudo3D(beam, pseudo3D);
			}
		}

		/// <summary>伤害闸：本帧该段不开伤害时，把共享的伤害/击退写成 0——判定带与伤害窗口照旧（亮着但不打人）。
		/// 开关来自 <see cref="ResolveDamageEnabled"/>（每段自己的 <c>DamageInStage*</c>）。</summary>
		private void ApplyDamageGate(RogueAIOverloadBeam beam, bool damageEnabled)
		{
			beam.Damage = damageEnabled ? Damage : 0;
			beam.KnockbackDistance = damageEnabled ? KnockbackDistance : 0f;
		}

		/// <summary>伪体积权重下发（<see cref="IPseudo3DBeam"/> 能力接口）：0 = 平面，1 = 全量。
		/// 子束不是伪体积光束（没实现接口）就自然忽略。</summary>
		private void ApplyPseudo3D(RogueAIOverloadBeam beam, float weight)
		{
			if (beam is not IPseudo3DBeam p3d) return;
			p3d.Pseudo3DWeight = weight;
			p3d.Pseudo3DTiltDegrees = Pseudo3DTiltDegrees;
			p3d.Pseudo3DFovDegrees = Pseudo3DFovDegrees;
			p3d.Pseudo3DSpinStrength = Pseudo3DSpinStrength;
		}

		/// <summary>伪体积权重 0~1：<see cref="Pseudo3DFromStage"/> 段起点之前恒 0（保持平面），
		/// 起点之后在增长窗口内从 0 线性长到 1（= 边延伸边越来越宽、转动越来越明显），完全张开后保持 1。</summary>
		private float ResolvePseudo3DWeight()
		{
			if (Pseudo3DFromStage <= 0) return 0f;

			float start = StageStartSeconds(Pseudo3DFromStage);
			if (start == float.MaxValue) return 0f;

			float phase = Mathf.Max(_elapsed - Mathf.Max(BeamDelay, 0f), 0f);
			if (phase < start) return 0f;

			// 增长窗口：显式值 > "从该段起点到张开结束"的剩余时长；剩余为 0（前面就把时间用完）才退化成淡出段。
			// 注意不能写成 max(剩余, 淡出)——淡出配长一点就会把窗口拉长。
			float window = Pseudo3DGrowSeconds > 0f
				? Pseudo3DGrowSeconds
				: ResolveOpeningSeconds() - start;
			if (window <= 0f) window = Mathf.Max(FadeDuration, 0.05f);
			return Mathf.Clamp((phase - start) / window, 0f, 1f);
		}

		/// <summary>把张开状态落到一条束上：长度/宽度走外部进度；角度与长度按**当前夹角**推导
		/// （末端因此任意时刻都共线于"距顶点 = 已推进基距"的底边）。</summary>
		private void ApplyOpening(RogueAIOverloadBeam beam, float relFactor, float progress, float spread)
		{
			beam.ExternalGrowProgress = progress;
			float d = ResolveBaseDistance();
			if (d <= 0f) return;

			float rel = relFactor * spread;
			float invCos = 1f / Mathf.Cos(Mathf.DegToRad(rel));
			beam.AngleDegrees = AngleDegrees + rel;
			beam.MaxLength = d * invCos;
			beam.MinLength = Mathf.Max(MinLength, 0f) * invCos;
		}

		/// <summary>共享参数（所有束必须同值的那一套）。</summary>
		private void PushShared(RogueAIOverloadBeam beam)
		{
			beam.BeamDelay = BeamDelay;
			// 子束自身的生长时钟让位：张开进度由本节点每帧下发（ExternalGrowProgress）
			beam.GrowDuration = 0f;
			// 子束的"全亮段" = 整段张开（含停顿）→ 伤害窗口正好覆盖张开全程（可见即挨打），淡出在其后
			beam.BeamDuration = ResolveOpeningSeconds();
			beam.SyncToAnchor = SyncToAnchor;   // 跟随开关也走"根上单点调参"（子束每帧自己对齐锚点）
			beam.FadeDuration = FadeDuration;
			beam.SpotlightDelay = SpotlightDelay;
			beam.SpotlightFadeIn = SpotlightFadeIn;
			beam.SpotlightDuration = SpotlightDuration;
			beam.SpotlightFadeOut = SpotlightFadeOut;
			// 兜底总时长已由子束 _Ready 按场景值初始化过 → 走 SetTotalLifetime 重算倒计时
			beam.SetTotalLifetime(ResolveLifetime());
			// 宽度不随张开进度从 0 长出来：张开进度是"推到哪儿了"，宽度由 ApplyWidth 按段系数下发（淡出收窄照旧）
			beam.WidthFollowsGrow = false;
			beam.TargetableFactions = TargetableFactions;
			beam.AllowSelfDamage = AllowSelfDamage;
			beam.Damage = Damage;
			beam.DamageTickInterval = DamageTickInterval;
			beam.TruncateOnHit = TruncateOnHit;   // 命中截断：每条束各自按自己的轴截（扇束侧只转发）
			beam.KnockbackDistance = KnockbackDistance;
			beam.KnockbackDuration = KnockbackDuration;
			beam.Attacker = _attacker;
		}

		/// <summary>基距 D（px，推导）= 三段距离之和。每段推多少由各段自己给，总和恒等于 D——
		/// 任何配置都不会再出现"某段没距离"（旧的"速度×时长 与 总量 打架"就是问题根源）。
		/// 几何（各束长度 = D / cosθ）与共底边都以此为准。</summary>
		private float ResolveBaseDistance()
			=> Mathf.Max(Stage1Distance, 0f) + Mathf.Max(Stage2Distance, 0f) + Mathf.Max(Stage3Distance, 0f);

		/// <summary>某段起点的相位（秒，不含前摇）：1 → 0、2 → 段1+停1、3 → 段1+停1+段2+停2。</summary>
		private float StageStartSeconds(int stage)
		{
			float s1 = Mathf.Max(Stage1Seconds, 0f), p1 = Mathf.Max(Pause1Seconds, 0f);
			float s2 = Mathf.Max(Stage2Seconds, 0f), p2 = Mathf.Max(Pause2Seconds, 0f);
			return stage switch
			{
				1 => 0f,
				2 => s1 + p1,
				3 => s1 + p1 + s2 + p2,
				_ => float.MaxValue,
			};
		}

		/// <summary>张开进度 0~1 = 已推进基距 / D。</summary>
		private float ResolveOpeningProgress()
		{
			float d = ResolveBaseDistance();
			if (d <= 0f) return 0f;

			float phase = Mathf.Max(_elapsed - Mathf.Max(BeamDelay, 0f), 0f);
			return Mathf.Clamp(ResolveCoveredDistance(phase) / d, 0f, 1f);
		}

		/// <summary>已推进的基距（px）：三段各自"在自段时长内匀速推完自段距离"，两次停顿冻结。
		/// 某段时长为 0 时该段距离瞬间跳过（配置异常，下发时会告警）。</summary>
		private float ResolveCoveredDistance(float phase)
		{
			float d1 = Mathf.Max(Stage1Distance, 0f);
			float d2 = d1 + Mathf.Max(Stage2Distance, 0f);
			float d3 = d2 + Mathf.Max(Stage3Distance, 0f);

			float end1 = Mathf.Max(Stage1Seconds, 0f);
			if (phase < end1) return d1 * Mathf.Clamp(phase / Mathf.Max(end1, 0.0001f), 0f, 1f);

			float pause1End = end1 + Mathf.Max(Pause1Seconds, 0f);
			if (phase < pause1End) return d1;                                          // 停 1：冻结

			float end2 = pause1End + Mathf.Max(Stage2Seconds, 0f);
			if (phase < end2)
				return Mathf.Lerp(d1, d2, Mathf.Clamp((phase - pause1End) / Mathf.Max(Stage2Seconds, 0.0001f), 0f, 1f));

			float pause2End = end2 + Mathf.Max(Pause2Seconds, 0f);
			if (phase < pause2End) return d2;                                          // 停 2：冻结

			float end3 = pause2End + Mathf.Max(Stage3Seconds, 0f);
			if (phase < end3)
				return Mathf.Lerp(d2, d3, Mathf.Clamp((phase - pause2End) / Mathf.Max(Stage3Seconds, 0.0001f), 0f, 1f));

			return d3;
		}

		/// <summary>张开总时长（秒，不含前摇与淡出）= 三段时长 + 两次停顿时长。</summary>
		private float ResolveOpeningSeconds()
			=> Mathf.Max(Stage1Seconds, 0f) + Mathf.Max(Pause1Seconds, 0f)
			   + Mathf.Max(Stage2Seconds, 0f) + Mathf.Max(Pause2Seconds, 0f)
			   + Mathf.Max(Stage3Seconds, 0f);

		/// <summary>当前落在第几段（1/2/3；**停顿沿用其前一段**，完全张开后的保持沿用段 3）。</summary>
		private int ResolveStageIndex()
		{
			float phase = Mathf.Max(_elapsed - Mathf.Max(BeamDelay, 0f), 0f);
			float end1 = Mathf.Max(Stage1Seconds, 0f) + Mathf.Max(Pause1Seconds, 0f);
			float end2 = end1 + Mathf.Max(Stage2Seconds, 0f) + Mathf.Max(Pause2Seconds, 0f);
			if (phase < end1) return 1;
			if (phase < end2) return 2;
			return 3;
		}

		/// <summary>本帧该不该结算伤害：读**当前段自己的开关**。</summary>
		private bool ResolveDamageEnabled()
			=> ResolveStageIndex() switch
			{
				1 => DamageInStage1,
				2 => DamageInStage2,
				_ => DamageInStage3,
			};

		/// <summary>当前段的**目标**宽度系数（视觉）。</summary>
		private float ResolveStageScale()
			=> ResolveStageIndex() switch
			{
				1 => Stage1WidthScale,
				2 => Stage2WidthScale,
				_ => Stage3WidthScale,
			};

		/// <summary>当前段的**判定带半高系数**（× DetectionRadius）。与视觉宽度系数独立。</summary>
		private float ResolveStageDetectionScale()
			=> ResolveStageIndex() switch
			{
				1 => Stage1DetectionScale,
				2 => Stage2DetectionScale,
				_ => Stage3DetectionScale,
			};

		/// <summary>当前段的光束绘制层级（换段即切换）。</summary>
		private int ResolveStageZIndex()
			=> ResolveStageIndex() switch
			{
				1 => Stage1ZIndex,
				2 => Stage2ZIndex,
				_ => Stage3ZIndex,
			};

		/// <summary>段 <see cref="ScorchStage"/> 期间沿尖端拖出**线型焦痕**：每帧把该束的尖端压给拖尾
		/// （拖尾自己按 PointStepPx 节流、按点龄淡出）。尖端 = 束位置 + 朝向 × 当前视觉长度
		/// （<see cref="LaserBeamVisualBase.CurrentLength"/>，**含命中截断**——束被目标截住时焦痕不再穿过目标）。
		/// 该值由子束在各自 _Process 里写好、而扇束先于子束处理 → 扫射中最多滞后一帧；
		/// 截断粘住目标时位置不动，无差异。
		/// 拖尾挂到扇束的父节点且自带 top_level → 世界坐标定格（敌人/滑槽移动不会拖着它跑）。</summary>
		private void TickScorches()
		{
			if (ScorchStage <= 0 || ScorchTrailScene == null) return;
			if (ResolveStageIndex() != ScorchStage) return;

			for (int i = 0; i < _beams.Count; i++)
			{
				var beam = _beams[i].Beam;
				if (!GodotObject.IsInstanceValid(beam)) continue;

				Vector2 tip = beam.GlobalPosition + Vector2.FromAngle(Mathf.DegToRad(beam.AngleDegrees))
					* beam.CurrentLength;

				while (_trails.Count <= i) _trails.Add(null!);
				var trail = _trails[i];
				if (trail == null || !GodotObject.IsInstanceValid(trail))
				{
					trail = ScorchTrailScene.Instantiate<LaserScorchTrail>();
					GetParent()?.AddChild(trail);
					_trails[i] = trail;
				}
				trail.PushTip(tip);
			}
		}

		/// <summary>把"基准宽度 × 当前平滑系数"下发（每帧调用——系数在逼近目标的过程中是连续变化的）。
		/// 视觉宽度与**判定带半高各自一套系数、相互独立**：判定带 = DetectionRadius × 当前段判定系数
		/// （Stage*DetectionScale），可以单独按段配判定宽窄（不跟光束视觉宽度走）。</summary>
		private void ApplyWidth(RogueAIOverloadBeam beam)
		{
			beam.BeamWidth = BeamWidth * _widthScale;
			beam.GlowWidth = GlowWidth * _widthScale;
			beam.DetectionRadius = DetectionRadius * _detectionScale;   // 判定带半高：与视觉宽度独立的一套系数
			beam.VisualZIndex = ResolveStageZIndex();   // 只改视觉层（Visual）：判定带/根节点不碰
		}

		/// <summary>当前半角 S(t)（度）：按**已推进基距**的四点折线——0 → 段1结束角 → 段2结束角 → 段3结束角（终态）。
		/// 折点进度 = 累计距离 ÷ D（推导）；停顿期间进度冻结 → 角度自动冻结。</summary>
		private float ResolveSpreadDegrees(float progress)
		{
			float d = ResolveBaseDistance();
			if (d <= 0f) return 0f;

			float t1 = Mathf.Clamp(Mathf.Max(Stage1EndDegrees, 0f), 0f, MaxSpreadDegrees);
			float t2 = Mathf.Clamp(Mathf.Max(Stage2EndDegrees, 0f), 0f, MaxSpreadDegrees);
			float t3 = Mathf.Clamp(Mathf.Max(Stage3EndDegrees, 0f), 0f, MaxSpreadDegrees);

			float d1 = Mathf.Max(Stage1Distance, 0f);
			float p1 = Mathf.Clamp(d1 / d, 0f, 1f);
			float p2 = Mathf.Clamp((d1 + Mathf.Max(Stage2Distance, 0f)) / d, 0f, 1f);

			if (p1 <= 0.0001f) return t3 * progress;                                   // 段 1 无距离 → 单一角度随进度
			if (progress <= p1) return t1 * (progress / p1);
			if (p2 <= p1 + 0.0001f)                                                    // 段 2 无距离 → 退化成两段
				return t1 + (t3 - t1) * ((progress - p1) / Mathf.Max(1f - p1, 0.0001f));
			if (progress <= p2) return t1 + (t2 - t1) * ((progress - p1) / (p2 - p1));
			return t2 + (t3 - t2) * ((progress - p2) / Mathf.Max(1f - p2, 0.0001f));
		}

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

		/// <summary>模板 = 场景里树序最前的那条子束（复制它的节点树与覆盖值来造副本）。</summary>
		private bool ResolveTemplate()
		{
			if (_templateBeam != null && GodotObject.IsInstanceValid(_templateBeam)) return true;

			foreach (var child in GetChildren())
				if (child is RogueAIOverloadBeam beam)
				{
					_templateBeam = beam;
					return true;
				}
			return false;
		}

		/// <summary>按 <see cref="BeamCount"/> 对齐子束数量（少了按模板复制、多了裁掉），并按树序重排布局：
		/// 相对系数 fᵢ 均分 [−1, +1]（N=1 → 0），角度/长度在下发时按 fᵢ 推导。</summary>
		private void ResetBeams()
		{
			if (!ResolveTemplate())
			{
				_beams.Clear();
				return;
			}

			int want = Mathf.Max(BeamCount, 1);

			// 现有（树序；跳过已排队的，避免同一帧重复算进布局）
			var existing = new List<RogueAIOverloadBeam>();
			foreach (var child in GetChildren())
				if (child is RogueAIOverloadBeam beam && !beam.IsQueuedForDeletion()) existing.Add(beam);

			// 多了裁掉（含运行时调小）
			for (int i = existing.Count - 1; i >= want; i--)
			{
				existing[i].QueueFree();
				existing.RemoveAt(i);
			}

			// 少了按模板复制：Duplicate 连子节点与覆盖值一起复制；材质/判定带形状由子束 _Ready 按实例再复制一遍
			for (int i = existing.Count; i < want; i++)
			{
				var dup = (RogueAIOverloadBeam)_templateBeam!.Duplicate();
				dup.Name = $"{_templateBeam.Name}_{i}";
				AddChild(dup);
				existing.Add(dup);
			}

			_beams.Clear();
			int count = existing.Count;
			for (int i = 0; i < count; i++)
			{
				float factor = count <= 1 ? 0f : -1f + 2f * i / (count - 1);
				_beams.Add((existing[i], factor));
			}

			// 新副本要补上生成方注入的锚点（注入发生在 AddChild(本节点) 之前，那时副本还不存在）——
			// Attacker 由 PushShared 每次下发时写入，不在这里补
			foreach (var (beam, _) in _beams)
			{
				if (!GodotObject.IsInstanceValid(beam)) continue;
				beam.FollowAnchor = _followAnchor;
			}
		}

		/// <summary>把接口值转发给当前所有子束。注入发生在 AddChild(本节点) 之前（那时副本还不存在）→
		/// setter 只存值，复制完由 <see cref="ResetBeams"/> 补推。</summary>
		private void ForwardTo(System.Action<RogueAIOverloadBeam> apply)
		{
			foreach (var (beam, _) in _beams)
				if (GodotObject.IsInstanceValid(beam)) apply(beam);
		}
	}
}
