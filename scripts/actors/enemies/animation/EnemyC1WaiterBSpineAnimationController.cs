using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.Animation
{
    /// <summary>
    /// Enemy_C1_WaiterB 专用 Spine 动画控制器，将动画与状态机/攻击模板绑定。
    /// </summary>
    public partial class EnemyC1WaiterBSpineAnimationController : EnemySpineAnimationController
    {
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");
        [Export] public string IdleAnimation = "idle";
        [Export] public string WalkAnimation = "walk";
        [Export] public string AttackAnimation = "attack";
        [Export] public string SkillAnimation = "skill_dash";
        [Export] public string Skill2Animation = "slash";
        [Export] public string Skill3Animation = "skill_beam";
        [Export] public string HitAnimation = "hit";
        [Export] public string StunAnimation = "stun";
        [Export] public string DieAnimation = "death";
        [Export(PropertyHint.Range, "0,5,0.01")] public float Skill3LoopStart = 1.63f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float Skill3LoopEnd = 2.13f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float Skill3PartStart = 2.14f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float Skill3PartEnd = 3.33f;

        private EnemyC1WaiterBAttackController? _attackController;
        private EnemyUltimateBeamAttack? _ultimateBeamAttack;
        private string _currentKey = string.Empty;
        private SpineAnimationPlaybackMode _currentMode = SpineAnimationPlaybackMode.Loop;
        private StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
        private float _activeLoopStart;
        private float _activeLoopEnd;
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
                    PlayOnceIfNeeded("Hit", HitAnimation, HitMixDuration);
                    break;
                case "Frozen":
                    PlayLoopIfNeeded("Frozen", StunAnimation, HitMixDuration);
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
                case "CooldownFrozen":
                    // 刺击攻击无冻结逻辑，直接播放 idle
                    PlayIdle();
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
                    // 用 AttackRunId 作为 key，保证连续两次 melee 能各自独立重播动画
                    PlayOnceIfNeeded($"Attack_{controller.AttackRunId}", AttackAnimation, AttackMixDuration);
                    return;
                }

                if (attackName.Equals(controller.Skill1AttackName, _comparison))
                {
                    // 从 AttackController 节点下按名称查找 DashSlashAttack
                    var skill1Attack = _attackController?.GetNodeOrNull<EnemyDashSlashAttack>(controller.Skill1AttackName);

                    if (skill1Attack == null || !skill1Attack.IsDashFinished)
                    {
                        // 正在冲刺中，循环播放冲刺动画
                        PlayLoopIfNeeded("skill_dash", SkillAnimation, SkillMixDuration);
                        return;
                    }

                    // 冲刺完成，播放 slash 收招动画
                    PlayOnceIfNeeded("skill_slash", Skill2Animation, SkillMixDuration);
                    return;
                }

                if (attackName.Equals(controller.UltimateAttackName, _comparison))
                {
                    var ultimateAttack =  ResolveUltimateBeamAttack(controller);

                    if (ultimateAttack != null && !ultimateAttack.IsBeamFinished)
                    {
                        // 光束攻击期间循环播放 skill_beam 动画
                        PlayPartLoopIfNeeded("skill_beam", Skill3Animation, Skill3LoopStart, Skill3LoopEnd, SkillMixDuration);
                        return;
                    }
                    if (ultimateAttack != null && ultimateAttack.IsBeamFinished)
                    {   
                        // 光束攻击结束收尾播放 skill_beam 动画
                        PlayPartOnceIfNeeded("skill_beam_PartOnce", Skill3Animation, Skill3PartStart, Skill3PartEnd, SkillMixDuration);
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

                // if (enqueueIdle && !string.IsNullOrEmpty(IdleAnimation))
                // {
                //     QueueAnimation(IdleAnimation, SpineAnimationPlaybackMode.Loop, 0f, mixDuration);
                // }
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

                // if (!string.IsNullOrEmpty(IdleAnimation))
                // {
                //     QueueAnimation(IdleAnimation, SpineAnimationPlaybackMode.Loop, 0f, mixDuration);
                // }
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

        private EnemyC1WaiterBAttackController? ResolveAttackController()
        {
            if (_attackController != null && IsInstanceValid(_attackController))
            {
                return _attackController;
            }

            if (AttackControllerPath.IsEmpty || Enemy == null)
            {
                return null;
            }

            _attackController = GetNodeOrNull<EnemyC1WaiterBAttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                _attackController = Enemy.GetNodeOrNull<EnemyC1WaiterBAttackController>(AttackControllerPath);
            }

            return _attackController;
        }
        private EnemyUltimateBeamAttack? ResolveUltimateBeamAttack(EnemyC1WaiterBAttackController controller)
        {
            if (_ultimateBeamAttack != null && IsInstanceValid(_ultimateBeamAttack))
            {
                return _ultimateBeamAttack;
            }

            _ultimateBeamAttack = controller.GetNodeOrNull<EnemyUltimateBeamAttack>(controller.UltimateAttackName);
            return _ultimateBeamAttack;
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

            if (currentAttack is EnemySimpleMeleeAttack simpleMelee && simpleMelee.RequireAnimationHitTrigger)
            {
                float originalDamage = Enemy != null ? Enemy.AttackDamage : 0f;
                if (Enemy != null) Enemy.AttackDamage = simpleMelee.Damage;
                currentAttack.TriggerAnimationHit();
                if (Enemy != null) Enemy.AttackDamage = originalDamage;
                return;
            }

            currentAttack.TriggerAnimationHit();
        }

        private bool IsExpectedHitAnimation(EnemyC1WaiterBAttackController controller, string animationName)
        {
            if (controller.CurrentAttackName.Equals(controller.MeleeAttackName, _comparison))
            {
                return MatchesAnimationName(animationName, AttackAnimation);
            }

            if (controller.CurrentAttackName.Equals(controller.Skill1AttackName, _comparison))
            {
                // 只有 slash 收招动画的 hit 帧才触发伤害；skill_dash 冲刺动画不触发，防止距离外命中
                return MatchesAnimationName(animationName, Skill2Animation);
            }

            if (controller.CurrentAttackName.Equals(controller.UltimateAttackName, _comparison))
            {
                // UltimateBeamAttack 不使用动画 hit 帧触发，始终放行
                return true;
            }

            return true;
        }

        private bool MatchesAnimationName(string animationName, string expectedAnimation)
        {
            if (string.IsNullOrEmpty(expectedAnimation))
            {
                return false;
            }

            if (string.Equals(animationName, expectedAnimation, _comparison))
            {
                return true;
            }

            return animationName.Contains(expectedAnimation, _comparison)
                || expectedAnimation.Contains(animationName, _comparison);
        }

    }
}


