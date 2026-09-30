using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies.Animation
{
	public partial class EnemyD1NetAdminSpineAnimationController : EnemySpineAnimationController
	{
		[Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
		[Export] public string IdleAnimation = "idle";
		[Export] public string WalkAnimation = "walk";
		[Export] public string AttackAnimation = "attack";
		[Export] public string SkillAnimation = "skill";
		[Export] public string Skill2Animation = "skill2";
		[Export] public string HitAnimation = "hit";
		[Export] public string StunAnimation = "stun2";
		[Export] public string DownAnimation = "down";
		[Export] public string UpAnimation = "up";
		[Export] public string DieAnimation = "death";
		[Export(PropertyHint.Range, "0.1,3,0.1")] public float KeepDistanceTimeScale = 2f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float WalkLoopStart = 0.5f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float WalkLoopEnd = 1.5f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float WalkPartStart = 1.51f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float WalkPartEnd = 2f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MultiAttackLoopStart = 1.5f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MultiAttackLoopEnd = 2.5f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MultiAttackPartStart = 2.5f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MultiAttackPartEnd = 3.0f;
		private EnemyD1NetAdminAttackController? _attackController;
		private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
		private Node? _spineControllerNode;
		private Callable _spineHitCallable;
		private bool _spineHitSubscribed;
		private bool _wasInMultiAttackPhase;

		public override void _Ready()
		{
			if (string.IsNullOrEmpty(DefaultLoopAnimation))
				DefaultLoopAnimation = IdleAnimation;
			base._Ready();
		}

		public override void _ExitTree()
		{
			UnsubscribeSpineHitSignal();
			base._ExitTree();
		}

		protected override void OnControllerReady()
		{
			base.OnControllerReady();
			ResolveAttackController();
			EnsureSpineHitSupport();
		}

		protected override float GetPreferredMixDuration()
		{
			return AttackMixDuration;
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			UpdateAnimation();
			TickPartialLoop();
		}

		private void UpdateAnimation()
		{
			if (Enemy?.StateMachine?.CurrentState == null)
			{
				PlayIdle();
				return;
			}

			string stateName = Enemy.StateMachine.CurrentState.Name;

			switch (stateName)
			{
				case "Walk":
					HandleWalkAnimation();
					break;
				case "Hit":
					DriveHitPhaseAnimation(HitAnimation, HitMixDuration);
					break;
				case "Dying":
					PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration);
					break;
				case "Frozen":
					HandleFrozenAnimation();
					break;
				case "KeepDistance":
					HandleKeepDistanceAnimation();
					break;
				case "Dead":
					PlayEmptyIfNeeded();
					break;
				case "Attack":
					HandleAttackAnimations();
					break;
				default:
					PlayIdle();
					break;
			}
		}

		private void HandleWalkAnimation()
		{
			var walkState = Enemy?.StateMachine?.CurrentState as EnemyNetAdminWalkState;
			if (walkState != null && walkState.IsStopping)
				PlayPartOnceIfNeeded("WalkPart", WalkAnimation, WalkPartStart, WalkPartEnd, WalkMixDuration);
			else
				PlayPartLoopIfNeeded("Walk", WalkAnimation, WalkLoopStart, WalkLoopEnd, WalkMixDuration);
		}

		private void HandleKeepDistanceAnimation()
		{
			var walkState = Enemy?.StateMachine?.CurrentState as EnemyNetAdminWalkState;
			if (walkState != null && walkState.IsStopping)
				PlayPartOnceIfNeeded("WalkPart", WalkAnimation, WalkPartStart, WalkPartEnd, WalkMixDuration);
			else
				PlayPartLoopIfNeeded("KeepDistance", WalkAnimation, WalkLoopStart, WalkLoopEnd, WalkMixDuration, KeepDistanceTimeScale);
		}

		private void HandleFrozenAnimation()
		{
			var frozenState = Enemy?.StateMachine?.CurrentState as EnemyNetAdminFrozenState;
			if (frozenState == null)
			{
				PlayLoopIfNeeded("Frozen", StunAnimation, AttackMixDuration);
				return;
			}

			switch (frozenState.CurrentPhase)
			{
				case EnemyNetAdminFrozenState.StunPhase.Down:
					PlayOnceIfNeeded("Down", DownAnimation, HitMixDuration);
					break;
				case EnemyNetAdminFrozenState.StunPhase.Loop:
					PlayLoopIfNeeded("Frozen", StunAnimation, HitMixDuration);
					break;
				case EnemyNetAdminFrozenState.StunPhase.Up:
					PlayOnceIfNeeded("Up", UpAnimation, HitMixDuration);
					break;
			}
		}

		private void HandleAttackAnimations()
		{
			var controller = ResolveAttackController();
			if (controller == null)
			{
				PlayIdle();
				return;
			}

			string attackName = controller.CurrentAttackName;
			if (!string.IsNullOrEmpty(attackName))
			{
				if (attackName.Equals(controller.MeleeAttackName, _comparison))
				{
					PlayOnceIfNeeded("attack", AttackAnimation, AttackMixDuration);
					return;
				}

				if (attackName.Equals(controller.MultiMeleeAttackName, _comparison))
				{
					var multiMelee = controller.GetNodeOrNull<EnemyNetAdminMultiMeleeAttack>(controller.MultiMeleeAttackName);
					if (multiMelee != null && multiMelee.IsInMultiAttackPhase)
					{
						GD.Print($"[MultiDebug] Loop: _currentKey='{_currentKey}', mode={_currentMode}");
						PlayPartLoopIfNeeded("MultiAttackLoop", AttackAnimation, MultiAttackLoopStart, MultiAttackLoopEnd, AttackMixDuration);
					}
					else
					{
						GD.Print($"[MultiDebug] Part: _currentKey='{_currentKey}', mode={_currentMode}");
						PlayPartOnceIfNeeded("MultiAttackPart", AttackAnimation, MultiAttackPartStart, MultiAttackPartEnd, AttackMixDuration);
					}
					return;
				}

				if (attackName.Equals(controller.SummonAttackName, _comparison))
				{
					PlayOnceIfNeeded("Skill", SkillAnimation, SkillMixDuration);
					return;
				}

				if (attackName.Equals(controller.UltimateAttackName, _comparison))
				{
					PlayOnceIfNeeded("Skill2", Skill2Animation, SkillMixDuration);
					return;
				}
			}

			PlayIdle();
		}

		private void PlayIdle()
		{
			PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
		}

		private EnemyD1NetAdminAttackController? ResolveAttackController()
		{
			if (_attackController != null && IsInstanceValid(_attackController))
				return _attackController;

			if (AttackControllerPath.IsEmpty || Enemy == null)
				return null;

			_attackController = GetNodeOrNull<EnemyD1NetAdminAttackController>(AttackControllerPath);
			if (_attackController == null)
				_attackController = Enemy.GetNodeOrNull<EnemyD1NetAdminAttackController>(AttackControllerPath);

			return _attackController;
		}

		private void EnsureSpineHitSupport()
		{
			if (_spineHitSubscribed) return;
			if (SpineSpritePath.IsEmpty) return;

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

		private void UnsubscribeSpineHitSignal()
		{
			if (!_spineHitSubscribed || _spineControllerNode == null)
			{
				_spineHitSubscribed = false;
				_spineControllerNode = null;
				return;
			}

			if (_spineControllerNode.IsConnected("hit_received", _spineHitCallable))
				_spineControllerNode.Disconnect("hit_received", _spineHitCallable);

			_spineHitSubscribed = false;
			_spineControllerNode = null;
		}

		private void OnSpineHitReceived(int hitStep, string animationName)
		{
			if (Enemy?.StateMachine?.CurrentState?.Name != "Attack") return;

			var controller = ResolveAttackController();
			if (controller == null || string.IsNullOrEmpty(controller.CurrentAttackName)) return;

			EnemyAttackTemplate? currentAttack = controller.GetNodeOrNull<EnemyAttackTemplate>(controller.CurrentAttackName);
			if (currentAttack == null || !currentAttack.IsRunning) return;

			if (!IsExpectedHitAnimation(controller, animationName)) return;

			if (currentAttack is EnemySimpleMeleeAttack simpleMelee && simpleMelee.RequireAnimationHitTrigger)
				currentAttack.TriggerAnimationHit();
			else
				currentAttack.TriggerAnimationHit();
		}

		private bool IsExpectedHitAnimation(EnemyD1NetAdminAttackController controller, string animationName)
		{
			string expectedAnimation = string.Empty;
			if (controller.CurrentAttackName.Equals(controller.MeleeAttackName, _comparison))
				expectedAnimation = AttackAnimation;   // Melee 播放的是 attack
			else if (controller.CurrentAttackName.Equals(controller.MultiMeleeAttackName, _comparison))
				expectedAnimation = AttackAnimation;   // MultiMelee 播放的是 attack

			if (string.IsNullOrEmpty(expectedAnimation))
				return true;

			return string.Equals(animationName, expectedAnimation, _comparison);
		}
	}
}
