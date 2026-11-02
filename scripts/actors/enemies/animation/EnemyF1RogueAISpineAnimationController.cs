using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
	/// <summary>
	/// rogueAI 本体的 Spine 动画控制器。
	/// 骨架里只有 `idle_up / ldle_down / walk_up / walk_down / hit_down / death_down / attack_*`（**ldle_down 是资产里
	/// 拼错的**：小写 L；没有 idle / walk / stun），所以只点播确实存在的动画名，其余状态保持当前姿势——
	/// 原 drone 控制器会每帧重试不存在的动画名并刷报错。
	/// **walk_up / walk_down 是空动画（无内容）**——常态 Idle 与 Walk 共用**形态的 idle 循环**
	/// （up = `idle_up`、down = `ldle_down`）；key 取动画名本身，Idle ⇄ Walk 切换不会把循环重启。
	/// **攻击动画同样按形态选**：普攻槽位 / 大招蓄力 / 冲刺各有 `Down*` 变体（up = `attack_up` /
	/// `attack_warming_up`，down = `attack_down` / `attack_warming_down`；down 名留空 = 不区分形态）。
	/// 只换"演什么"，不改出招逻辑（攻击控制器不感知形态）。
	/// **近身下砸**（普通/Pro，按类型解析）：Warmup 整段循环 warming（按形态）；普通版 Active 起播一次
	/// `up_to_down`，Pro 版 Active 循环、Recovery 截断（`StrikeLoopCount` 圈）。
	/// up → down 由 DownTransition 状态过渡：播一次 `up_to_down`（时长与状态的 DurationSeconds 对齐）。
	/// **Spine hit 事件**：订阅 SpineController 的 `hit_received`，只把**本次攻击实际在播的那支片**的事件转发给
	/// 正在跑的模板（`TriggerAnimationHit`）——下砸的伤害/特效因此对齐 `up_to_down` 的 hit 帧。
	/// </summary>
	public partial class EnemyF1RogueAISpineAnimationController : EnemySpineAnimationController
	{
		/// <summary>常态循环（up 形态，Idle/Walk 共用；骨架的 walk_up 是空动画，不用）。</summary>
		[Export] public string IdleAnimation { get; set; } = "idle_up";
		/// <summary>常态循环（down 形态，Idle/Walk 共用；骨架的 walk_down 是空动画，不用）。
		/// **骨架里是拼错的 "ldle_down"**（小写 L），照抄才能播到；空 = 不区分形态（恒用
		/// <see cref="IdleAnimation"/>，旧行为）。</summary>
		[Export] public string DownIdleAnimation { get; set; } = "ldle_down";
		/// <summary>up → down 过渡动画：由 DownTransition 状态恰好播一次；时长需与过渡状态的
		/// DurationSeconds 对齐（当前骨架 0.8s）。</summary>
		[Export] public string UpToDownAnimation { get; set; } = "up_to_down";
		/// <summary>受击动画名（受击段/回正段由 EnemyHitState 的双时间轴驱动）。</summary>
		[Export] public string HitAnimation { get; set; } = "hit_down";
		/// <summary>死亡动画名。</summary>
		[Export] public string DieAnimation { get; set; } = "death_down";
		/// <summary>up 形态的普攻动画槽位（attack_up）；留空 = 不切换。</summary>
		[Export] public string AttackAnimation { get; set; } = "attack_up";
		/// <summary>down 形态的普攻动画槽位（骨架有 attack_down）；空 = 不区分形态、回退
		/// <see cref="AttackAnimation"/>。</summary>
		[Export] public string DownAttackAnimation { get; set; } = "attack_down";

		[ExportCategory("Near Melee")]
		/// <summary>Warmup 段（整个预热）循环动画，up 形态。</summary>
		[Export] public string ProximityWarmupAnimation { get; set; } = "attack_warming_up";
		/// <summary>Warmup 段循环动画，down 形态（空 = 回退 up 版）。</summary>
		[Export] public string DownProximityWarmupAnimation { get; set; } = "attack_warming_down";
		/// <summary>进入 Active 时播一次的落地动画（不循环，保持末帧）。</summary>
		[Export] public string ProximityStrikeAnimation { get; set; } = "up_to_down";

		[ExportCategory("Ultimate")]
		/// <summary>大招攻击名（与 AttackController 上的一致）。</summary>
		[Export] public string UltimateAttackName { get; set; } = "RogueAIOverload";
		/// <summary>强化版大招攻击名（进度升级后取代上面那个）：解析"正在跑的是哪一支"时两者都试。</summary>
		[Export] public string UltimateProAttackName { get; set; } = "RogueAIOverloadPro";
		/// <summary>蓄力段（滑向随机一端）循环动画（up 形态）。</summary>
		[Export] public string UltimateChargeAnimation { get; set; } = "attack_warming_up";
		/// <summary>冲刺段（横扫到另一端）循环动画（up 形态）。</summary>
		[Export] public string UltimateDashAnimation { get; set; } = "attack_up";
		/// <summary>蓄力段的 down 形态动画（骨架有 attack_warming_down）；空 = 回退
		/// <see cref="UltimateChargeAnimation"/>。</summary>
		[Export] public string DownUltimateChargeAnimation { get; set; } = "attack_warming_down";
		/// <summary>冲刺段的 down 形态动画（骨架有 attack_down）；空 = 回退
		/// <see cref="UltimateDashAnimation"/>。</summary>
		[Export] public string DownUltimateDashAnimation { get; set; } = "attack_down";

		/// <summary>down 形态开关：由本体（EnemyF1RogueAIPart2）按血量阈值驱动——true 时常态循环切
		/// <see cref="DownIdleAnimation"/>。受击/死亡本就只有 down 名（hit_down / death_down），不在本开关内。</summary>
		public bool DownForm { get; set; }

		// ── Spine hit 事件转发（SpawnTiming = OnAnimationHit 的条目与 RequireAnimationHitTrigger 靠这条链）──
		private Node? _spineControllerNode;
		private Callable _spineHitCallable;
		private bool _spineHitSubscribed;
		private readonly StringComparison _comparison = StringComparison.OrdinalIgnoreCase;

		public override void _Ready()
		{
			// 基类 OnControllerReady 会播放 DefaultLoopAnimation，必须先赋值
			if (string.IsNullOrEmpty(DefaultLoopAnimation))
				DefaultLoopAnimation = IdleAnimation;
			base._Ready();
		}

		protected override void OnControllerReady()
		{
			base.OnControllerReady();
			EnsureSpineHitSubscription();
		}

		public override void _ExitTree()
		{
			UnsubscribeSpineHit();
			base._ExitTree();
		}

		/// <summary>订阅主 SpineSprite（SpineController.gd）的 hit_received；找不到信号时静默跳过。</summary>
		private void EnsureSpineHitSubscription()
		{
			if (_spineHitSubscribed || SpineSpritePath.IsEmpty) return;

			_spineControllerNode = GetNodeOrNull(SpineSpritePath) ?? Enemy?.GetNodeOrNull(SpineSpritePath);
			if (_spineControllerNode == null || !_spineControllerNode.HasSignal("hit_received"))
			{
				_spineControllerNode = null;
				return;
			}

			_spineHitCallable = Callable.From<int, string>(OnSpineHitReceived);
			_spineControllerNode.Connect("hit_received", _spineHitCallable);
			_spineHitSubscribed = true;
		}

		private void UnsubscribeSpineHit()
		{
			if (!_spineHitSubscribed || _spineControllerNode == null) return;

			if (GodotObject.IsInstanceValid(_spineControllerNode)
				&& _spineControllerNode.IsConnected("hit_received", _spineHitCallable))
				_spineControllerNode.Disconnect("hit_received", _spineHitCallable);

			_spineHitSubscribed = false;
			_spineControllerNode = null;
		}

		/// <summary>事件 → 当前招式的 TriggerAnimationHit；只认本次攻击实际在播的片子，
		/// warming / 受击片 / 死亡片上的同名事件不会误触。</summary>
		private void OnSpineHitReceived(int hitStep, string animationName)
		{
			if (Enemy?.StateMachine?.CurrentState?.Name != "Attack") return;

			var template = ResolveRunningAttackTemplate();
			if (template == null) return;

			string expected = ResolveExpectedHitAnimation(template);
			if (string.IsNullOrEmpty(expected)) return;
			if (!string.Equals(animationName, expected, _comparison)) return;

			template.TriggerAnimationHit();
		}

		private EnemyAttackTemplate? ResolveRunningAttackTemplate()
		{
			var controller = Enemy?.StateMachine?.GetNodeOrNull<EnemyAttackController>("Attack/AttackController");
			if (controller == null) return null;

			foreach (var child in controller.GetChildren())
				if (child is EnemyAttackTemplate template && template.IsRunning)
					return template;

			return controller.IsRunning ? controller : null;
		}

		/// <summary>本次攻击应该从哪支片收 hit 事件：下砸=落地片；大招=当前相位片；其它=普攻槽位片。</summary>
		private string ResolveExpectedHitAnimation(EnemyAttackTemplate template)
		{
			if (template is EnemyF1RogueAIDownSlamAttack)
				return ProximityStrikeAnimation;

			if (template is EnemyF1RogueAIUltimateAttack ultimate)
			{
				return ultimate.Phase switch
				{
					EnemyF1RogueAIUltimateAttack.UltPhase.Charge =>
						PickFormAnimation(UltimateChargeAnimation, DownUltimateChargeAnimation),
					EnemyF1RogueAIUltimateAttack.UltPhase.Dash =>
						PickFormAnimation(UltimateDashAnimation, DownUltimateDashAnimation),
					_ => string.Empty,
				};
			}

			return PickFormAnimation(AttackAnimation, DownAttackAnimation);
		}

		protected override float GetPreferredMixDuration() => IdleMixDuration;

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateAnimation();
		}

		private void UpdateAnimation()
		{
			if (Enemy?.StateMachine?.CurrentState == null)
			{
				PlayIdle();
				return;
			}

			switch (Enemy.StateMachine.CurrentState.Name)
			{
				case "Walk":            // walk_* 空动画 → 与 Idle 共用形态的 idle 循环，靠 key 去重不重启
					PlayIdle();
					break;
				case "DownTransition":  // 过渡状态：恰好播一次 up_to_down；结束时回 Idle 自动接 ldle_down
					PlayOnceIfNeeded("DownTransition", UpToDownAnimation, IdleMixDuration);
					break;
				case "Hit":
					DriveHitPhaseAnimation(HitAnimation, HitMixDuration);
					break;
				case "Dying":
					PlayLoopIfNeeded("Die", DieAnimation, DieMixDuration);
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":
					PlayAttackPhaseAnimation();
					break;
				case "Frozen":          // 骨架没有眩晕动画：保持当前姿势，不切换
					break;
				default:
					PlayIdle();
					break;
			}
		}

		/// <summary>Attack 状态：大招按模板阶段播对应循环（蓄力 / 冲刺），其它攻击走 AttackAnimation 槽位。</summary>
		private void PlayAttackPhaseAnimation()
		{
			var ultimate = ResolveRunningUltimate();
			if (ultimate != null)
			{
				switch (ultimate.Phase)
				{
					case EnemyF1RogueAIUltimateAttack.UltPhase.Charge:
						string charge = PickFormAnimation(UltimateChargeAnimation, DownUltimateChargeAnimation);
						PlayLoopIfNeeded(charge, charge, WalkMixDuration);
						return;
					case EnemyF1RogueAIUltimateAttack.UltPhase.Dash:
						string dash = PickFormAnimation(UltimateDashAnimation, DownUltimateDashAnimation);
						PlayLoopIfNeeded(dash, dash, WalkMixDuration);
						return;
					default:   // Settle / None：收招，回常态循环
						PlayIdle();
						return;
				}
			}

			// 近身下砸：Warmup 整段循环 warming（按形态选片）；普通版 Active 起播一次落地片，
			// Pro 版（StrikeLoopCount > 1）Active 段循环落地片、Recovery 切回 idle 截断循环。
			if (ResolveRunningProximityAttack() is { } proximity)
			{
				if (proximity.IsWarmup)
				{
					string warming = PickFormAnimation(ProximityWarmupAnimation, DownProximityWarmupAnimation);
					PlayLoopIfNeeded(warming, warming, WalkMixDuration);
				}
				else if (proximity.StrikeLoopCount > 1)
				{
					if (proximity.IsActive)
						PlayLoopIfNeeded("DownSlamStrikeLoop", ProximityStrikeAnimation, AttackMixDuration);
					else
						PlayIdle();   // Recovery：停循环，防止 N+1 圈 hit
				}
				else
				{
					PlayOnceIfNeeded("DownSlamStrike", ProximityStrikeAnimation, AttackMixDuration);
				}
				return;
			}

			PlayAttackIfConfigured();
		}

		/// <summary>正在跑的是不是近身下砸（普通/Pro 同类型，按类型解析）；是则返回实例。</summary>
		private EnemyF1RogueAIDownSlamAttack? ResolveRunningProximityAttack()
		{
			var controller = Enemy?.StateMachine?.GetNodeOrNull<EnemyAttackController>("Attack/AttackController");
			if (controller == null) return null;

			foreach (var child in controller.GetChildren())
				if (child is EnemyF1RogueAIDownSlamAttack attack && attack.IsRunning)
					return attack;

			return null;
		}

		/// <summary>正在跑的是不是大招（原版或强化版）；是则返回模板实例（供读 Phase），否则 null。</summary>
		private EnemyF1RogueAIUltimateAttack? ResolveRunningUltimate()
		{
			var controller = Enemy?.StateMachine?.GetNodeOrNull<EnemyAttackController>("Attack/AttackController");
			if (controller == null) return null;

			return ResolveRunningUltimate(controller, UltimateProAttackName)
				?? ResolveRunningUltimate(controller, UltimateAttackName);
		}

		private static EnemyF1RogueAIUltimateAttack? ResolveRunningUltimate(EnemyAttackController controller, string attackName)
		{
			if (string.IsNullOrEmpty(attackName)) return null;
			var template = controller.GetNodeOrNull<EnemyF1RogueAIUltimateAttack>(attackName);
			return template is { IsRunning: true } ? template : null;
		}

		private void PlayAttackIfConfigured()
		{
			string attack = PickFormAnimation(AttackAnimation, DownAttackAnimation);
			if (string.IsNullOrEmpty(attack)) return;
			PlayLoopIfNeeded(attack, attack, AttackMixDuration);
		}

		private void PlayIdle()
		{
			string idle = PickFormAnimation(IdleAnimation, DownIdleAnimation);
			PlayLoopIfNeeded(idle, idle, IdleMixDuration);
		}

		/// <summary>按当前形态取动画名：down 且配了 down 变体则用变体，否则回退 up 版（down 名空 = 不区分形态）。
		/// key 用动画名本身——形态翻转时自动重播对应变体。</summary>
		private string PickFormAnimation(string upAnimation, string downAnimation)
			=> DownForm && !string.IsNullOrEmpty(downAnimation) ? downAnimation : upAnimation;
	}
}
