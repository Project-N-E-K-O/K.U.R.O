using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 基础敌人攻击模板。封装预热-生效-恢复的攻击流程，并提供可重写的钩子。
    /// 继承此类即可快速实现不同的攻击类型（近战、投射、范围等）。
    /// </summary>
    public partial class EnemyAttackTemplate : Node
    {
        private enum AttackPhase
        {
            Idle,
            Warmup,
            Active,
            Recovery
        }

        [ExportCategory("Meta")]
        [Export] public string AttackName = "DefaultAttack";

        [ExportCategory("Timing (s)")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float WarmupDuration = 0.2f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ActiveDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float RecoveryDuration = 0.35f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float CooldownDuration = 1.0f;

        [ExportCategory("Combat")]
        [Export(PropertyHint.Range, "0,500,1")] public float DetectionRangeBonus = 0.0f;
        [Export(PropertyHint.Range, "0,180,1")] public float MaxAllowedAngleToPlayer = 135.0f;
        [Export] public string AnimationName = "animations/attack";
        [Export] public NodePath AttackAreaPath = new NodePath();

        protected SampleEnemy Enemy { get; private set; } = null!;
        protected SamplePlayer? Player => Enemy.PlayerTarget;
        protected Area2D? AttackArea { get; private set; }

        private AttackPhase _phase = AttackPhase.Idle;
        private float _phaseTimer = 0.0f;
        private float _cooldownTimer = 0.0f;

        public bool IsRunning => _phase != AttackPhase.Idle;
        public bool IsOnCooldown => _cooldownTimer > 0.0f;

        public virtual void Initialize(SampleEnemy enemy)
        {
            Enemy = enemy;

            if (!string.IsNullOrEmpty(AttackAreaPath.ToString()))
            {
                AttackArea = Enemy.GetNodeOrNull<Area2D>(AttackAreaPath);
            }

            if (AttackArea == null && Enemy.AttackArea != null)
            {
                AttackArea = Enemy.AttackArea;
            }

            OnInitialized();
        }

        protected virtual void OnInitialized() { }

        public virtual bool CanStart()
        {
            if (Enemy == null || Player == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (Enemy.AttackTimer > 0) return false;

            if (!Enemy.IsPlayerWithinDetectionRange(DetectionRangeBonus))
            {
                return false;
            }

            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer == Vector2.Zero) return false;

            Vector2 facing = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            float angle = Mathf.RadToDeg(facing.AngleTo(toPlayer));
            return angle <= MaxAllowedAngleToPlayer;
        }

        public bool TryStart()
        {
            if (!CanStart()) return false;

            _cooldownTimer = CooldownDuration;
            Enemy.AttackTimer = Mathf.Max(Enemy.AttackTimer, CooldownDuration);

            OnAttackStarted();
            SetPhase(AttackPhase.Warmup);
            return true;
        }

        public void Tick(double delta)
        {
            if (_cooldownTimer > 0.0f)
            {
                _cooldownTimer -= (float)delta;
            }

            if (_phase == AttackPhase.Idle) return;

            _phaseTimer -= (float)delta;
            if (_phaseTimer <= 0.0f)
            {
                AdvancePhase();
            }
        }

        public void Cancel(bool clearCooldown = false)
        {
            if (clearCooldown)
            {
                _cooldownTimer = 0.0f;
                Enemy.AttackTimer = 0.0f;
            }

            if (_phase != AttackPhase.Idle)
            {
                SetPhase(AttackPhase.Idle);
            }
        }

        protected virtual void OnAttackStarted()
        {
            if (!string.IsNullOrEmpty(AnimationName))
            {
                Enemy.AnimPlayer?.Play(AnimationName);
            }
        }

        protected virtual void OnWarmupStarted()
        {
            Enemy.Velocity = Vector2.Zero;
        }

        protected virtual void OnActivePhase()
        {
            Enemy.PerformAttack();
        }

        protected virtual void OnRecoveryStarted()
        {
            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed);
        }

        protected virtual void OnAttackFinished() { }

        protected void ForceEnterRecoveryPhase()
        {
            if (_phase == AttackPhase.Active)
            {
                SetPhase(AttackPhase.Recovery);
            }
        }

        private void SetPhase(AttackPhase phase)
        {
            _phase = phase;
            switch (phase)
            {
                case AttackPhase.Warmup:
                    _phaseTimer = WarmupDuration;
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = ActiveDuration;
                    OnActivePhase();
                    break;
                case AttackPhase.Recovery:
                    _phaseTimer = RecoveryDuration;
                    OnRecoveryStarted();
                    break;
                case AttackPhase.Idle:
                    _phaseTimer = 0.0f;
                    OnAttackFinished();
                    break;
            }

            if (_phase != AttackPhase.Idle && _phaseTimer <= 0.0f)
            {
                AdvancePhase();
            }
        }

        private void AdvancePhase()
        {
            switch (_phase)
            {
                case AttackPhase.Warmup:
                    SetPhase(AttackPhase.Active);
                    break;
                case AttackPhase.Active:
                    SetPhase(AttackPhase.Recovery);
                    break;
                case AttackPhase.Recovery:
                    SetPhase(AttackPhase.Idle);
                    break;
            }
        }
    }
}

