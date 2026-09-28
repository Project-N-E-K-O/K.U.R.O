using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
	/// <summary>
	/// 机械桩炮台的 Spine 动画控制器（炮台 / 以后机枪台共用）。
	///
	/// 骨架只有 14 支片子：`idle/d_idle`、`walk/d_walk`、`hit/d_hit`、`attack/d_attack`、
	/// `preheat/d_preheat`、`recover/d_recover`、`turn/d_turn`（没有 death / stun）。
	/// `d_*` 是美术手绘的镜像孪生（朝向左），所以**选名规则**是：
	///   · 静止/受击/开火：按**当前朝向**取（左 = `d_` 前缀）；
	///   · 转身：按**起飞时的朝向**取（朝右 → `turn` = 右→左，片子自己完成镜像）。
	/// 引擎永不写 `SpineSprite.scale.x`（见 <see cref="EnemyF1RogueAITurretFacingController"/> 的说明）。
	///
	/// 状态映射（键取动画名本身，朝向变化会自然重播）：
	///   Idle/Walk → idle；Hit → hit（走基类的双时间轴管线）；Attack → 按攻击模板阶段 preheat/attack/recover；
	///   Dying → 用 hit 收尾；Dead → 空动画（无死亡片）；Frozen/CooldownFrozen → 保持当前姿势。
	/// </summary>
	public partial class EnemyF1RogueAITurretSpineAnimationController : EnemySpineAnimationController
	{
		[Export] public string IdleAnimation { get; set; } = "idle";
		[Export] public string HitAnimation { get; set; } = "hit";
		[Export] public string TurnAnimation { get; set; } = "turn";
		[Export] public string PreHeatAnimation { get; set; } = "preheat";
		[Export] public string AttackAnimation { get; set; } = "attack";
		[Export] public string RecoverAnimation { get; set; } = "recover";

		/// <summary>朝向 → 动画名：朝左取 `d_` 前缀（这套骨架的 `d_*` 是手绘镜像，引擎不做缩放翻转）。</summary>
		public static string ResolveName(bool facingRight, string baseName)
			=> facingRight || string.IsNullOrEmpty(baseName) ? baseName : "d_" + baseName;

		/// <summary>当前选片键（无头验证用；也用于排查"配了没反应"）。</summary>
		public string CurrentKey => _currentKey;

		private EnemyF1RogueAITurretFacingController? _facing;

		public override void _Ready()
		{
			// 基类 OnControllerReady 会播 DefaultLoopAnimation，先给它一个名字（朝向键在 Update 里接管）
			if (string.IsNullOrEmpty(DefaultLoopAnimation))
				DefaultLoopAnimation = IdleAnimation;
			base._Ready();
		}

		protected override float GetPreferredMixDuration() => IdleMixDuration;

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateAnimation();
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
					DriveHitPhaseAnimation(ResolveName(right, HitAnimation), HitMixDuration);
					break;
				case "Dying":           // 骨架没有死亡片：用受击片收尾，随后停住
					PlayOnceIfNeeded("die", ResolveName(right, HitAnimation), DieMixDuration);
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":
					PlayAttackPhaseAnimation(right);
					break;
				case "Frozen":
				case "CooldownFrozen":  // 冻结/眩晕：保持当前姿势，不切换
					break;
				default:
					PlayIdleLoop();
					break;
			}
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
