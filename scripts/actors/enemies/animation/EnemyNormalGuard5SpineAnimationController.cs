using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// Enemy_Normal_guard5 专用 Spine 动画控制器：
    ///   · 盾击（SimpleMeleeAttack）→ "attack"（单次）；
    ///   · 跪地持盾→冲撞（ShieldStance）→ Warmup 循环 "skill1"，Active/Recovery 播 "skill2"（单次）；
    ///   · **受击按方向分 clip**：正面格挡表现 = "hit1"、背面受击 = "hit2"
    ///     （方向由敌人根脚本 <see cref="EnemyNormalGuard5.LastHitFromFront"/> 记录——正面命中是被减免+格挡的表现，
    ///     背面才是正常受击）；
    ///   · Frozen → "stun"、Dying → "death"。
    ///   hit 帧事件只认当前动作该有的 clip（盾击=attack、冲撞=skill2）。
    /// </summary>
    public partial class EnemyNormalGuard5SpineAnimationController : EnemySpineAnimationController
    {
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
        [Export] public string IdleAnimation = "idle";
        [Export] public string WalkAnimation = "walk";
        [Export] public string AttackAnimation = "attack";
        [Export] public string Skill1Animation = "skill1";
        [Export] public string Skill2Animation = "skill2";
        [Export] public string Hit1Animation = "hit1";
        [Export] public string Hit2Animation = "hit2";
        [Export] public string StunAnimation = "stun";
        [Export] public string DieAnimation = "death";

        private EnemyNormalGuard5AttackController? _attackController;
        private EnemyGuard5ShieldStanceAttack? _stanceAttack;
        private readonly StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
        private Node? _spineControllerNode;
        private Callable _spineHitCallable;
        private bool _spineHitSubscribed;

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(DefaultLoopAnimation))
            {
                DefaultLoopAnimation = IdleAnimation;
            }

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
                    PlayLoopIfNeeded("Walk", WalkAnimation, WalkMixDuration);
                    break;
                case "Hit":
                    // 正面 = hit1（举盾格挡表现）、背面/侧面 = hit2（正常受击）
                    DriveHitPhaseAnimation(ResolveHitAnimation(), HitMixDuration);
                    break;
                case "Dying":
                    PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration);
                    break;
                case "Frozen":
                    PlayLoopIfNeeded("Frozen", StunAnimation, HitMixDuration);
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

        private string ResolveHitAnimation()
            => Enemy is EnemyNormalGuard5 guard5 && guard5.LastHitFromFront ? Hit1Animation : Hit2Animation;

        private void HandleAttackAnimations()
        {
            var controller = ResolveAttackController();
            if (controller == null)
            {
                PlayIdle();
                return;
            }

            string attackName = controller.CurrentAttackName;
            if (string.IsNullOrEmpty(attackName))
            {
                PlayIdle();
                return;
            }

            if (attackName.Equals(controller.MeleeAttackName, _comparison))
            {
                PlayOnceIfNeeded("Attack", AttackAnimation, AttackMixDuration);
                return;
            }

            if (attackName.Equals(controller.SkillAttackName, _comparison))
            {
                var stance = ResolveStanceAttack(controller);
                if (stance != null && stance.CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup)
                {
                    PlayLoopIfNeeded("Skill1", Skill1Animation, SkillMixDuration);   // 跪地等待：整段循环
                    return;
                }

                PlayOnceIfNeeded("Skill2", Skill2Animation, SkillMixDuration);        // 冲撞出手：单次
                return;
            }

            PlayIdle();
        }

        private void PlayIdle()
        {
            PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
        }

        private EnemyNormalGuard5AttackController? ResolveAttackController()
        {
            if (_attackController != null && IsInstanceValid(_attackController))
            {
                return _attackController;
            }

            if (AttackControllerPath.IsEmpty || Enemy == null)
            {
                return null;
            }

            _attackController = GetNodeOrNull<EnemyNormalGuard5AttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                _attackController = Enemy.GetNodeOrNull<EnemyNormalGuard5AttackController>(AttackControllerPath);
            }

            return _attackController;
        }

        private EnemyGuard5ShieldStanceAttack? ResolveStanceAttack(EnemyNormalGuard5AttackController controller)
        {
            if (_stanceAttack != null && IsInstanceValid(_stanceAttack))
            {
                return _stanceAttack;
            }

            _stanceAttack = controller.GetNodeOrNull<EnemyGuard5ShieldStanceAttack>(controller.SkillAttackName);
            return _stanceAttack;
        }

        private void EnsureSpineHitSupport()
        {
            if (_spineHitSubscribed)
            {
                return;
            }

            if (SpineSpritePath.IsEmpty)
            {
                return;
            }

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
            {
                _spineControllerNode.Disconnect("hit_received", _spineHitCallable);
            }

            _spineHitSubscribed = false;
            _spineControllerNode = null;
        }

        private void OnSpineHitReceived(int hitStep, string animationName)
        {
            if (Enemy?.StateMachine?.CurrentState?.Name != "Attack")
            {
                return;
            }

            var controller = ResolveAttackController();
            if (controller == null || string.IsNullOrEmpty(controller.CurrentAttackName))
            {
                return;
            }

            EnemyAttackTemplate? currentAttack = controller.GetNodeOrNull<EnemyAttackTemplate>(controller.CurrentAttackName);
            if (currentAttack == null || !currentAttack.IsRunning)
            {
                return;
            }

            if (!IsExpectedHitAnimation(controller, animationName))
            {
                return;
            }

            currentAttack.TriggerAnimationHit();
        }

        private bool IsExpectedHitAnimation(EnemyNormalGuard5AttackController controller, string animationName)
        {
            string expectedAnimation = string.Empty;
            if (controller.CurrentAttackName.Equals(controller.MeleeAttackName, _comparison))
            {
                expectedAnimation = AttackAnimation;
            }
            else if (controller.CurrentAttackName.Equals(controller.SkillAttackName, _comparison))
            {
                expectedAnimation = Skill2Animation;
            }

            if (string.IsNullOrEmpty(expectedAnimation))
            {
                return true;
            }

            return string.Equals(animationName, expectedAnimation, _comparison);
        }
    }
}
