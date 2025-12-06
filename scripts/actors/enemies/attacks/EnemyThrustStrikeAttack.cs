using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 简化突刺攻击：冲向玩家记录位置，短暂停顿后以常规伤害判定输出。
    /// 复用基本 AttackArea/PerformAttack 流程，无抓取冻结逻辑。
    /// </summary>
    public partial class EnemyThrustStrikeAttack : EnemyAttackTemplate
    {
        [ExportGroup("Dash")]
        [Export(PropertyHint.Range, "50,3000,10")] public float DashSpeed = 800f;
        [Export(PropertyHint.Range, "0,2000,10")] public float MaxDashDistance = 600f;
        [Export(PropertyHint.Range, "0,2000,10")] public float MinDashDistance = 120f;
        [Export] public bool SnapFacingToDash = true;

        [ExportGroup("Strike")]
        [Export(PropertyHint.Range, "0,2,0.01")] public float StrikeDelaySeconds = 0.25f;
        [Export(PropertyHint.Range, "1,200,1")] public int StrikeDamage = 12;

        private Vector2 _dashDirection = Vector2.Right;
        private Vector2 _dashTarget;
        private bool _isDashing;
        private bool _strikeQueued;
        private float _strikeDelayTimer;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            SetPhysicsProcess(true);
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            if (Enemy == null || Player == null) return false;

            Vector2 delta = Player.GlobalPosition - Enemy.GlobalPosition;
            if (delta == Vector2.Zero) return false;

            return true;
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            PrepareDash();
            _strikeQueued = false;
            _isDashing = false;
            _strikeDelayTimer = 0f;
        }

        protected override void OnActivePhase()
        {
            base.OnActivePhase();
            StartDash();
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            _isDashing = false;
            Enemy.Velocity = Vector2.Zero;
            _strikeQueued = false;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (Enemy == null) return;

            if (_isDashing)
            {
                UpdateDash(delta);
            }
            else if (_strikeQueued)
            {
                _strikeDelayTimer -= (float)delta;
                if (_strikeDelayTimer <= 0f)
                {
                    _strikeQueued = false;
                    ExecuteStrike();
                }
            }
        }

        private void PrepareDash()
        {
            if (Enemy == null) return;

            Vector2 start = Enemy.GlobalPosition;
            Vector2 targetPos = start;

            if (Player != null)
            {
                targetPos = Player.GlobalPosition;
            }
            else
            {
                targetPos += (Enemy.FacingRight ? Vector2.Right : Vector2.Left) * MinDashDistance;
            }

            Vector2 delta = targetPos - start;
            if (delta == Vector2.Zero)
            {
                delta = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            }

            float distance = delta.Length();
            if (MaxDashDistance > 0)
            {
                distance = Mathf.Min(distance, MaxDashDistance);
            }
            distance = Mathf.Max(distance, MinDashDistance);

            _dashDirection = delta.Normalized();
            _dashTarget = start + _dashDirection * distance;

            if (SnapFacingToDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }

            float dashTime = distance / Mathf.Max(DashSpeed, 1f);
            ActiveDuration = Mathf.Max(dashTime + StrikeDelaySeconds, 0.05f);
        }

        private void StartDash()
        {
            if (Enemy == null) return;
            _isDashing = true;
            Enemy.Velocity = _dashDirection * DashSpeed;
        }

        private void UpdateDash(double delta)
        {
            if (Enemy == null) return;

            Vector2 toTarget = _dashTarget - Enemy.GlobalPosition;
            float moveStep = DashSpeed * (float)delta;

            if (toTarget.Dot(_dashDirection) <= 0f || toTarget.LengthSquared() <= moveStep * moveStep)
            {
                Enemy.GlobalPosition = _dashTarget;
                FinishDash();
                return;
            }

            Enemy.MoveAndSlide();
        }

        private void FinishDash()
        {
            if (Enemy == null) return;

            _isDashing = false;
            Enemy.Velocity = Vector2.Zero;

            _strikeDelayTimer = StrikeDelaySeconds;
            _strikeQueued = StrikeDelaySeconds > 0f;

            if (!_strikeQueued)
            {
                ExecuteStrike();
            }
        }

        private void ExecuteStrike()
        {
            if (Enemy == null) return;

            float originalDamage = Enemy.AttackDamage;
            Enemy.AttackDamage = StrikeDamage;
            Enemy.PerformAttack();
            Enemy.AttackDamage = originalDamage;

            ForceEnterRecoveryPhase();
        }
    }
}

