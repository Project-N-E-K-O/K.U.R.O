using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// Enemy_Normal_guard1 专用 Spine 动画控制器，将动画与状态机/攻击模板绑定。
    /// </summary>
    public partial class EnemyNormalGuard1SpineAnimationController : EnemySpineAnimationController
    {
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
        [Export] public string IdleAnimation = "idle";
        [Export] public string WalkAnimation = "walk";
        [Export] public string AttackAnimation = "attack";
        [Export] public string SkillAnimation = "skill";
        [Export] public string HitAnimation = "hit";
        [Export] public string DieAnimation = "death";
        [Export(PropertyHint.Range, "0,5,0.01")] public float SkillLoopStart = 1.49f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float SkillLoopEnd = 1.5f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float SkillPartStart = 1.51f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float SkillPartEnd = 1.97f;
        private EnemyNormalGuard1AttackController? _attackController;
        private string _currentKey = string.Empty;
        private SpineAnimationPlaybackMode _currentMode = SpineAnimationPlaybackMode.Loop;
        private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
        private float _activeLoopStart;
        private float _activeLoopEnd;
        private EnemyOnePunchAttack? _skillChargeOnePunchAttack;

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(DefaultLoopAnimation))
            {
                DefaultLoopAnimation = IdleAnimation;
            }

            base._Ready();
        }

        protected override void OnControllerReady()
        {
            base.OnControllerReady();
            ResolveAttackController();
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
                    PlayOnceIfNeeded("Hit", HitAnimation, HitMixDuration);
                    break;
                case "Dying":
                    PlayOnceIfNeeded("Die", DieAnimation, DieMixDuration, enqueueIdle: false);
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
            if (!string.IsNullOrEmpty(attackName))
            {   
                if (attackName.Equals(controller.MeleeAttackName, _comparison))
                {
                    PlayOnceIfNeeded("Attack", AttackAnimation, AttackMixDuration);
                    return;
                }

                if (attackName.Equals(controller.SkillAttackName, _comparison))
                {
                    var skillAttack = ResolveSkillOnePunchAttack(controller);

                    if (skillAttack != null && !skillAttack.IsDashFinished)
                    {
                        PlayPartLoopIfNeeded("Skill", SkillAnimation, SkillLoopStart, SkillLoopEnd, SkillMixDuration);
                        return;
                    }

                    if (skillAttack != null && skillAttack.IsDashFinished)
                    {
                        PlayPartOnceIfNeeded("SkillPartOnce", SkillAnimation, SkillPartStart, SkillPartEnd, SkillMixDuration);
                        return;
                    }
                }
            }

            PlayIdle();
        }

        private void PlayIdle()
        {
            PlayLoopIfNeeded("Idle", IdleAnimation, IdleMixDuration);
        }

        private void PlayLoopIfNeeded(string key, string animationName, float mixDuration)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (_currentKey == key && _currentMode == SpineAnimationPlaybackMode.Loop)
            {
                return;
            }

            if (PlayLoop(animationName, mixDuration))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.Loop;
            }
        }

        private void PlayOnceIfNeeded(string key, string animationName, float mixDuration, bool enqueueIdle = true)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (_currentKey == key && _currentMode == SpineAnimationPlaybackMode.Once)
            {
                return;
            }

            if (PlayOnce(animationName, mixDuration, 1f, string.Empty))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.Once;

                if (enqueueIdle && !string.IsNullOrEmpty(IdleAnimation))
                {
                    QueueAnimation(IdleAnimation, SpineAnimationPlaybackMode.Loop, 0f, mixDuration);
                }
            }
        }

        private void PlayPartLoopIfNeeded(string key, string animationName, float loopStart, float loopEnd, float mixDuration)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (loopEnd <= loopStart)
            {
                PlayLoopIfNeeded(key, animationName, mixDuration);
                return;
            }

            bool samePartialLoop = _currentKey == key
                && _currentMode == SpineAnimationPlaybackMode.PartialLoop
                && Mathf.IsEqualApprox(_activeLoopStart, loopStart)
                && Mathf.IsEqualApprox(_activeLoopEnd, loopEnd);

            if (samePartialLoop)
            {
                return;
            }

            if (PlayPartialLoop(animationName, loopStart, loopEnd, mixDuration))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.PartialLoop;
                _activeLoopStart = loopStart;
                _activeLoopEnd = loopEnd;
            }
        }

        private void PlayPartOnceIfNeeded(string key, string animationName, float partStart, float partEnd, float mixDuration)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (partEnd <= partStart)
            {
                PlayOnceIfNeeded(key, animationName, mixDuration);
                return;
            }

            bool samePartialOnce = _currentKey == key
                && _currentMode == SpineAnimationPlaybackMode.PartialOnce
                && Mathf.IsEqualApprox(_activeLoopStart, partStart)
                && Mathf.IsEqualApprox(_activeLoopEnd, partEnd);

            if (samePartialOnce)
            {
                return;
            }

            if (PlayPartialOnce(animationName, partStart, partEnd, mixDuration))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.PartialOnce;
                _activeLoopStart = partStart;
                _activeLoopEnd = partEnd;

                if (!string.IsNullOrEmpty(IdleAnimation))
                {
                    QueueAnimation(IdleAnimation, SpineAnimationPlaybackMode.Loop, 0f, mixDuration);
                }
            }
        }

        private void TickPartialLoop()
        {
            if (_currentMode != SpineAnimationPlaybackMode.PartialLoop)
            {
                return;
            }

            UpdatePartialLoop(_activeLoopStart, _activeLoopEnd);
        }

        private void PlayEmptyIfNeeded()
        {
            if (_currentKey == "Empty")
            {
                return;
            }

            if (PlayEmpty(DieMixDuration))
            {
                _currentKey = "Empty";
                _currentMode = SpineAnimationPlaybackMode.Loop;
            }
        }

        private EnemyNormalGuard1AttackController? ResolveAttackController()
        {
            if (_attackController != null && IsInstanceValid(_attackController))
            {
                return _attackController;
            }

            if (AttackControllerPath.IsEmpty || Enemy == null)
            {
                return null;
            }

            _attackController = GetNodeOrNull<EnemyNormalGuard1AttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                _attackController = Enemy.GetNodeOrNull<EnemyNormalGuard1AttackController>(AttackControllerPath);
            }

            return _attackController;
        }
        private EnemyOnePunchAttack? ResolveSkillOnePunchAttack(EnemyNormalGuard1AttackController controller)
        {
            if (_skillChargeOnePunchAttack != null && IsInstanceValid(_skillChargeOnePunchAttack))
            {
                return _skillChargeOnePunchAttack;
            }

            _skillChargeOnePunchAttack = controller.GetNodeOrNull<EnemyOnePunchAttack>(controller.SkillAttackName);
            return _skillChargeOnePunchAttack;
        }

    }
}


