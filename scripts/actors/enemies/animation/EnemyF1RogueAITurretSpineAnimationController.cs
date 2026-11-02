using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies.Animation
{
	/// <summary>
	/// 机械桩炮台的 Spine 动画控制器（炮台 / 以后机枪台共用）。
	///
	/// 骨架的动画片以 `d_` 前缀成对（美术手绘的镜像孪生，朝向左；镜像键在动画数据里，
	/// 引擎永不写 `SpineSprite.scale.x`——见 <see cref="EnemyF1RogueAITurretFacingController"/> 的说明），**选名规则**：
	///   · 静止/受击/开火/近战：按**当前朝向**取（左 = `d_` 前缀）；
	///   · 转身：按**起飞时的朝向**取（朝右 → `turn` = 右→左，片子自己完成镜像）。
	/// 新片可能只画了一侧（如 `skill` 尚无 `d_skill`）：朝向选片统一走 <see cref="ResolveFacingName"/>
	/// 做存在性回退（暂播基名，美术补片后自动升级）。
	///
	/// 状态映射（键取动画名本身，朝向变化会自然重播）：
	///   Idle/Walk → idle；Hit → hit（走基类的双时间轴管线）；
	///   Attack → 按攻击模板阶段 preheat/attack/recover（炮台族近战 = skill 整段，罐子协助 = idle 整段）；
	///   Dying → death（两骨架已有 death/d_death，镜像缺失回退基名）；Dead → 空动画；Frozen/CooldownFrozen → 保持当前姿势。
	/// </summary>
	public partial class EnemyF1RogueAITurretSpineAnimationController : EnemySpineAnimationController
	{
		[Export] public string IdleAnimation { get; set; } = "idle";
		[Export] public string HitAnimation { get; set; } = "hit";
		[Export] public string TurnAnimation { get; set; } = "turn";
		[Export] public string PreHeatAnimation { get; set; } = "preheat";
		[Export] public string AttackAnimation { get; set; } = "attack";
		[Export] public string RecoverAnimation { get; set; } = "recover";
		/// <summary>炮台族近战（<see cref="EnemyF1RogueAITurretMeleeAttack"/>）的出手片。骨架已有 `skill`/`d_skill`；
		/// 选片带存在性回退（未来某侧图标缺镜像时回退基名）。</summary>
		[Export] public string SkillAnimation { get; set; } = "skill";
		/// <summary>阵亡片：Dying 状态播一次并保持末帧（两骨架已有 death/d_death；镜像缺失时回退基名）。</summary>
		[Export] public string DieAnimation { get; set; } = "death";

		[ExportCategory("Jar Assist Stun")]
		[Export] public string StunDownAnimation { get; set; } = "stun_1";
		[Export] public string StunLoopAnimation { get; set; } = "stun";
		[Export] public string StunUpAnimation { get; set; } = "stun_2";

		/// <summary>朝向 → 动画名：朝左取 `d_` 前缀（这套骨架的 `d_*` 是手绘镜像，引擎不做缩放翻转）。</summary>
		public static string ResolveName(bool facingRight, string baseName)
			=> facingRight || string.IsNullOrEmpty(baseName) ? baseName : "d_" + baseName;

		/// <summary>朝向 → 动画名（带**存在性回退**）：朝左优先 `d_` 镜像片；骨架里还没有这支镜像片时回退基名
		/// （否则播放失败会每帧重试 + 刷屏报错）。美术补上镜像片后自动升级，无需改代码。</summary>
		private string ResolveFacingName(bool facingRight, string baseName)
		{
			string preferred = ResolveName(facingRight, baseName);
			if (preferred == baseName) return baseName;
			return SpineHasAnimation(preferred) ? preferred : baseName;
		}

		/// <summary>当前选片键（无头验证用；也用于排查"配了没反应"）。</summary>
		public string CurrentKey => _currentKey;

		private EnemyF1RogueAITurretFacingController? _facing;

		// ── Spine hit 事件转发（SpawnTiming = OnAnimationHit 的条目靠这条链）────────────
		//
		// 骨架里的 `attack` / `d_attack` 带一串 `hit` 事件（机枪台是每片 10 个）；事件本身由
		// SpineController.gd 发成 `hit_received` 信号，但**必须由敌人自己的动画控制器订阅并转发**给
		// 正在跑的招式（项目里每个敌人的动画控制器都有一份这段，基类没有）。炮台骨架没有事件，
		// 所以最早那版没接——机枪台有，缺了这段就会"日志里能看到 hit、特效却不生成"。
		private Node? _spineControllerNode;
		private Callable _spineHitCallable;
		private bool _spineHitSubscribed;
		private readonly StringComparison _comparison = StringComparison.OrdinalIgnoreCase;

		public override void _Ready()
		{
			// 基类 OnControllerReady 会播 DefaultLoopAnimation，先给它一个名字（朝向键在 Update 里接管）
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

		/// <summary>Spine 的 hit 事件 → 转发给正在跑的招式（`TriggerAnimationHit`）。
		/// 只在 Attack 状态、且事件确实来自**本次攻击实际在播的那支片**时转发——
		/// 炮台族近战播 skill（左向 d_skill 未就绪时回退基名，与选片同一套解析），开火系播 attack / d_attack；
		/// 受击片 `hit`、转身片等也带同名事件时不会误触。</summary>
		private void OnSpineHitReceived(int hitStep, string animationName)
		{
			if (Enemy?.StateMachine?.CurrentState?.Name != "Attack") return;

			var template = ResolveRunningTemplate();
			if (template == null) return;

			string expected = template is EnemyF1RogueAITurretMeleeAttack
				? ResolveFacingName(Enemy.FacingRight, SkillAnimation)
				: ResolveName(Enemy.FacingRight, AttackAnimation);
			if (!string.Equals(animationName, expected, _comparison)) return;

			template.TriggerAnimationHit();
		}

		protected override float GetPreferredMixDuration() => IdleMixDuration;

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateAnimation();
			TickPartialLoop();   // PlayPartLoopIfNeeded 需要每帧回绕片段时间
		}

		private void UpdateAnimation()
		{
			var enemy = Enemy;
			if (enemy?.StateMachine?.CurrentState == null)
			{
				PlayIdleLoop();
				return;
			}

			_facing ??= enemy.GetNodeOrNull<EnemyF1RogueAITurretFacingController>("FacingController");

			// 转身优先：片子自己完成镜像（turn = 右→左，d_turn = 左→右），按起飞时朝向选
			if (_facing is { IsTurning: true })
			{
				PlayLoopIfNeeded("turn",
					ResolveName(_facing.TurnOriginFacing, TurnAnimation), WalkMixDuration);
				return;
			}

			bool right = enemy.FacingRight;
			switch (enemy.StateMachine.CurrentState.Name)
			{
				case "Walk":            // 炮台不走路，但状态名沿用（静止占位状态），与 Idle 同一支片
				case "Idle":
					PlayIdleLoop();
					break;
				case "Hit":
					PlayHitAnimation(right);
					break;
				case "Dying":           // 阵亡片播一次并保持末帧（death/d_death，按朝向取、镜像缺失回退基名）
					PlayOnceIfNeeded("die", ResolveFacingName(right, DieAnimation), DieMixDuration);
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":
					PlayAttackPhaseAnimation(right);
					break;
				case "JarAssistStun":
					PlayJarAssistStunAnimation(right);
					break;
				case "Frozen":
				case "CooldownFrozen":  // 冻结/眩晕：保持当前姿势，不切换
					break;
				default:
					PlayIdleLoop();
					break;
			}
		}

		private void PlayHitAnimation(bool right)
		{
			if (Enemy?.StateMachine?.CurrentState is EnemyF1RogueAITurretHitState hit
				&& hit.UseStunRecovery && hit.CurrentPhase == EnemyHitState.HitPhase.Recover)
			{
				string animation = ResolveFacingName(right, StunUpAnimation);
				PlayOnceIfNeeded($"TurretHitRecover:{hit.PhaseTick}:{animation}", animation, HitMixDuration);
				return;
			}
			// 眩晕来源仍由原 Hit 双阶段管线驱动，播完按剩余时间恢复眩晕。
			DriveHitPhaseAnimation(ResolveFacingName(right, HitAnimation), HitMixDuration);
		}

		private void PlayJarAssistStunAnimation(bool right)
		{
			if (Enemy?.StateMachine?.CurrentState is not EnemyJarAssistStunState stun) return;
			string baseName = stun.CurrentPhase switch
			{
				EnemyJarAssistStunState.StunPhase.Down => StunDownAnimation,
				EnemyJarAssistStunState.StunPhase.Up => StunUpAnimation,
				_ => StunLoopAnimation
			};
			string animation = ResolveFacingName(right, baseName);
			string key = $"JarAssistStun:{stun.CycleId}:{animation}";
			if (stun.CurrentPhase == EnemyJarAssistStunState.StunPhase.Loop)
				PlayLoopIfNeeded(key, animation, AttackMixDuration);
			else
				PlayOnceIfNeeded(key, animation, AttackMixDuration,
					stun.CurrentPhase == EnemyJarAssistStunState.StunPhase.Down
						? stun.DownTimeScale : stun.UpTimeScale);
		}

		/// <summary>攻击阶段 → 片子：warmup=preheat、active=attack、recovery=recover（阶段直接读模板的 CurrentPhase）。</summary>
		private void PlayAttackPhaseAnimation(bool right)
		{
			var template = ResolveRunningTemplate();
			if (template == null)
			{
				PlayIdleLoop();
				return;
			}

			// 罐子协助：整招期间保持待机片（起手/出手/回收不做表演），朝左取 d_idle——
			// 罐子生成后计数 7.5s 里的动作主体本来就是 idle，避免只在前 1s 闪 preheat/attack/recover。
			if (template is JarAssistAttack)
			{
				PlayIdleLoop();
				return;
			}

			// 炮台族近战招（机枪台/炮台共用）：warmup 起播 skill 一次——片段自带全段表演（~1.1s，
			// 与整招时长接近），播完自然回 Idle；出手/收招不重发（回 Idle 后 key 变更，下一次近战自然重播）。
			if (template is EnemyF1RogueAITurretMeleeAttack)
			{
				if (template.CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup)
					PlayOnceIfNeeded("melee_strike", ResolveFacingName(right, SkillAnimation), AttackMixDuration);
				return;
			}

			switch (template.CurrentPhase)
			{
				case EnemyAttackTemplate.AttackPhase.Warmup:
					PlayLoopIfNeeded("preheat", ResolveName(right, PreHeatAnimation), AttackMixDuration);
					break;
				case EnemyAttackTemplate.AttackPhase.Active:
					PlayLoopIfNeeded("attack", ResolveName(right, AttackAnimation), AttackMixDuration);
					break;
				case EnemyAttackTemplate.AttackPhase.Recovery:
					PlayOnceIfNeeded("recover", ResolveName(right, RecoverAnimation), AttackMixDuration);
					break;
				default:
					PlayIdleLoop();
					break;
			}
		}

		/// <summary>正在跑的子攻击模板（优先子节点；子节点都没有在跑时退回控制器自身）。</summary>
		private EnemyAttackTemplate? ResolveRunningTemplate()
		{
			var controller = Enemy?.StateMachine?.GetNodeOrNull<EnemyAttackController>("Attack/AttackController");
			if (controller == null) return null;

			foreach (var child in controller.GetChildren())
				if (child is EnemyAttackTemplate template && template.IsRunning)
					return template;

			return controller.IsRunning ? controller : null;
		}

		private void PlayIdleLoop()
			=> PlayLoopIfNeeded(ResolveName(Enemy?.FacingRight ?? true, IdleAnimation),
				ResolveName(Enemy?.FacingRight ?? true, IdleAnimation), IdleMixDuration);
	}
}
