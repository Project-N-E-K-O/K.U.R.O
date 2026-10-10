using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// Enemy_Normal_guard4 专用 Spine 动画控制器：
    ///   · 下劈（Attack，复用 KickAttack）→ "attack"（单次）；
    ///   · 防御反击（CounterAttack）→ Warmup（招架窗口）循环 "skill1"，Active/Recovery（反击）播 "skill2"（单次）。
    ///   hit 帧事件只认当前动作该有的 clip：下劈 = attack、反击 = skill2；skill1 循环里的 hit 事件被过滤，
    ///   防止招架期空事件被暂存成 pending 命中。
    /// </summary>
    public partial class EnemyNormalGuard4SpineAnimationController : EnemySpineAnimationController
    {
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
        [Export] public string IdleAnimation = "idle";
        [Export] public string WalkAnimation = "walk";
        [Export] public string AttackAnimation = "attack";
        [Export] public string Skill1Animation = "skill1";
        [Export] public string Skill2Animation = "skill2";
        [Export] public string HitAnimation = "hit";
        [Export] public string StunAnimation = "stun";
        [Export] public string DieAnimation = "death";

        private EnemyNormalGuard4AttackController? _attackController;
        private EnemyGuard4CounterAttack? _counterAttack;
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
                    DriveHitPhaseAnimation(HitAnimation, HitMixDuration);
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
                var counter = ResolveCounterAttack(controller);
                if (counter != null && counter.CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup)
                {
                    PlayLoopIfNeeded("Skill1", Skill1Animation, SkillMixDuration);   // 招架窗口：整段循环
                    return;
                }

                PlayOnceIfNeeded("Skill2", Skill2Animation, SkillMixDuration);        // 反击出手：单次
                return;
            }

            PlayIdle();
        }

        private void PlayIdle()
        {
            PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
        }

        private EnemyNormalGuard4AttackController? ResolveAttackController()
        {
            if (_attackController != null && IsInstanceValid(_attackController))
            {
                return _attackController;
            }

            if (AttackControllerPath.IsEmpty || Enemy == null)
            {
                return null;
            }

            _attackController = GetNodeOrNull<EnemyNormalGuard4AttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                _attackController = Enemy.GetNodeOrNull<EnemyNormalGuard4AttackController>(AttackControllerPath);
            }

            return _attackController;
        }

        private EnemyGuard4CounterAttack? ResolveCounterAttack(EnemyNormalGuard4AttackController controller)
        {
            if (_counterAttack != null && IsInstanceValid(_counterAttack))
            {
                return _counterAttack;
            }

            _counterAttack = controller.GetNodeOrNull<EnemyGuard4CounterAttack>(controller.SkillAttackName);
            return _counterAttack;
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

        private bool IsExpectedHitAnimation(EnemyNormalGuard4AttackController controller, string animationName)
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
