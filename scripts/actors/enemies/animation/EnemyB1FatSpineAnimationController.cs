using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// Enemy_B1_fat 专用 Spine 动画控制器，将动画与状态机/攻击模板绑定。
    /// </summary>
    public partial class EnemyB1FatSpineAnimationController : EnemySpineAnimationController
    {
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
        [Export] public string IdleAnimation = "idle";
        [Export] public string WalkAnimation = "walk";
        [Export] public string AttackAnimation = "attack";
        [Export] public string SkillAnimation = "skill_01";
        [Export] public string Skill2Animation = "skill_02";
        [Export] public string Skill3Animation = "skill_03";
        [Export] public string HitAnimation = "hit";
        [Export] public string DieAnimation = "death";
        [Export(PropertyHint.Range, "0,1,0.01")] public float MixDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float Skill1LoopStart = 1.32f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float Skill1LoopEnd = 1.33f;

        private EnemyB1FatAttackController? _attackController;
        private string _currentKey = string.Empty;
        private SpineAnimationPlaybackMode _currentMode = SpineAnimationPlaybackMode.Loop;
        private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
        private float _activeLoopStart;
        private float _activeLoopEnd;
        private EnemyChargeGrabAttack? _skill1ChargeGrabAttack;

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
            return MixDuration;
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
                    PlayLoopIfNeeded("Walk", WalkAnimation);
                    break;
                case "Hit":
                    PlayOnceIfNeeded("Hit", HitAnimation);
                    break;
                case "Dying":
                    PlayOnceIfNeeded("Die", DieAnimation, enqueueIdle: false);
                    break;
                case "Dead":
                    PlayEmptyIfNeeded();
                    break;
                case "Attack":
                    HandleAttackAnimations();
                    break;
                case "CooldownFrozen":
                    if (_currentKey == "Skill3" && _currentMode == SpineAnimationPlaybackMode.Once)
                    {
                        // skill3 正在播放，等待其自然结束，不打断
                        break;
                    }
                    if (!TryPlaySkill3Finisher())
                    {
                        PlayIdle();
                    }
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
                    PlayOnceIfNeeded("Attack", AttackAnimation);
                    return;
                }

                if (attackName.Equals(controller.Skill1AttackName, _comparison))
                {
                    var skill1Attack = ResolveSkill1ChargeGrabAttack(controller);

                    if (skill1Attack == null || !skill1Attack.IsDashFinished)
                    {
                        PlayPartLoopIfNeeded("Skill", SkillAnimation, Skill1LoopStart, Skill1LoopEnd);
                        return;
                    }

                    if (skill1Attack.IsEvaluatingEscape || !skill1Attack.AreEscapeCountersCleared)
                    {
                        PlayLoopIfNeeded("Skill2", Skill2Animation);
                        return;
                    }

                    TryPlaySkill3Finisher();
                    return;
                }

            }

            PlayIdle();
        }

        private bool TryPlaySkill3Finisher()
        {
            var controller = ResolveAttackController();
            if (controller == null)
            {
                return false;
            }

            string attackName = controller.CurrentAttackName;
            if (string.IsNullOrEmpty(attackName) || !attackName.Equals(controller.Skill1AttackName, _comparison))
            {
                return false;
            }

            var skill1Attack = ResolveSkill1ChargeGrabAttack(controller);
            if (skill1Attack == null || !skill1Attack.IsDashFinished)
            {
                return false;
            }

            if (skill1Attack.IsEvaluatingEscape || !skill1Attack.AreEscapeCountersCleared)
            {
                return false;
            }

            if (!skill1Attack.HasPendingSkill3Finisher)
            {
                return false;
            }

            PlayOnceIfNeeded("Skill3", Skill3Animation);
            skill1Attack.ConsumeSkill3FinisherRequest();
            return true;
        }

        private void PlayIdle()
        {
            PlayLoopIfNeeded("Idle", IdleAnimation);
        }

        private void PlayLoopIfNeeded(string key, string animationName)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (_currentKey == key && _currentMode == SpineAnimationPlaybackMode.Loop)
            {
                return;
            }

            if (PlayLoop(animationName, MixDuration))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.Loop;
            }
        }

        private void PlayOnceIfNeeded(string key, string animationName, bool enqueueIdle = true)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (_currentKey == key && _currentMode == SpineAnimationPlaybackMode.Once)
            {
                return;
            }

            string? followUp = enqueueIdle ? IdleAnimation : null;
            if (PlayOnce(animationName, MixDuration, 1f, followUp))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.Once;
            }
        }

        private void PlayPartLoopIfNeeded(string key, string animationName, float loopStart, float loopEnd)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (loopEnd <= loopStart)
            {
                PlayLoopIfNeeded(key, animationName);
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

            if (PlayPartialLoop(animationName, loopStart, loopEnd, MixDuration))
            {
                _currentKey = key;
                _currentMode = SpineAnimationPlaybackMode.PartialLoop;
                _activeLoopStart = loopStart;
                _activeLoopEnd = loopEnd;
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

            if (PlayEmpty(MixDuration))
            {
                _currentKey = "Empty";
                _currentMode = SpineAnimationPlaybackMode.Loop;
            }
        }

        private EnemyB1FatAttackController? ResolveAttackController()
        {
            if (_attackController != null && IsInstanceValid(_attackController))
            {
                return _attackController;
            }

            if (AttackControllerPath.IsEmpty || Enemy == null)
            {
                return null;
            }

            _attackController = GetNodeOrNull<EnemyB1FatAttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                _attackController = Enemy.GetNodeOrNull<EnemyB1FatAttackController>(AttackControllerPath);
            }

            return _attackController;
        }
        private EnemyChargeGrabAttack? ResolveSkill1ChargeGrabAttack(EnemyB1FatAttackController controller)
        {
            if (_skill1ChargeGrabAttack != null && IsInstanceValid(_skill1ChargeGrabAttack))
            {
                return _skill1ChargeGrabAttack;
            }

            _skill1ChargeGrabAttack = controller.GetNodeOrNull<EnemyChargeGrabAttack>(controller.Skill1AttackName);
            return _skill1ChargeGrabAttack;
        }

    }
}


