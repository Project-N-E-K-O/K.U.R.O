using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// guard4 普通攻击"举刀下劈"（简单重载 <see cref="EnemyAttackTemplate"/>，刻意不用 EnemyKickAttack）：
    ///   · Warmup（举刀）：站桩（每帧归零速度防漂移）；出手瞬间快照玩家方向——举起期间玩家可以侧移躲开；
    ///   · Active：沿快照方向直线突进 DashDuration 秒（到时停位移，Active 由 ActiveDuration 自然收尾）；
    ///     伤害走动画事件（RequireAnimationHitTrigger）：命中帧结算伤害 + 玩家在攻击区内则沿冲刺方向击退；
    ///   · Recovery：停冲、速度归零。
    /// 刻意不含 KickAttack 的：触发区轮询 / BodyEntered 强切 Attack 状态 / CanStart 朝向对齐副作用 /
    /// 拆家具 / 后摇 CooldownFrozen 状态——guard4 的攻击由攻击控制器排队，不需要"进圈自动出招"，
    /// 那些机制正是眩晕被抢、朝向被隔着控制状态翻转等问题的策源地。
    /// </summary>
    public partial class EnemyGuard4CleaveAttack : EnemyAttackTemplate
    {
        [ExportCategory("Cleave Dash")]
        /// <summary>突进速度 = 基础 Speed × 倍率（倍率语义：基础速度调整时突进自动适配）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float DashSpeedMultiplier = 2f;
        /// <summary>突进持续（秒）：到时停位移，Active 由 ActiveDuration 自然收尾。</summary>
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float DashDuration = 0.2f;
        /// <summary>突进期间锁朝向（出手瞬间翻转对齐突进方向）。</summary>
        [Export] public bool LockFacingDuringDash = true;

        private Vector2 _dashDirection = Vector2.Right;
        private bool _isDashing;
        private float _dashElapsed;

        /// <summary>起手条件：基类（检测圈 + 朝向角）之上再要求玩家在自配触发区里。
        /// 纯查询、无任何副作用（轮询/信号/翻朝向都不做）。</summary>
        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInTriggerArea();
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();

            // 出手瞬间快照突进方向（举起前锁定：举起期间玩家可以侧移躲开）
            Vector2 from = Enemy != null && Enemy.PlayerTarget != null
                ? Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition
                : Vector2.Zero;
            _dashDirection = from.LengthSquared() < 0.0001f
                ? (Enemy?.FacingRight == true ? Vector2.Right : Vector2.Left)
                : from.Normalized();
            if (LockFacingDuringDash && Enemy != null && Mathf.Abs(_dashDirection.X) > 0.01f)
                Enemy.FlipFacing(_dashDirection.X > 0f);

            _isDashing = false;
            _dashElapsed = 0f;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }

        protected override void OnActivePhase()
        {
            base.OnActivePhase();   // 特效 + RequireAnimationHitTrigger 的命中窗口
            _isDashing = true;
            _dashElapsed = 0f;
            if (Enemy != null) Enemy.Velocity = _dashDirection * (Enemy.Speed * DashSpeedMultiplier);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !IsRunning) return;

            switch (CurrentPhase)
            {
                case AttackPhase.Warmup:
                    // 举刀期间站桩：每帧归零防外部移动源漂移（与防御反击同款）
                    Enemy.Velocity = Vector2.Zero;
                    break;

                case AttackPhase.Active:
                    if (!_isDashing) return;
                    _dashElapsed += (float)delta;
                    if (_dashElapsed >= Mathf.Max(DashDuration, 0.05f))
                    {
                        _isDashing = false;
                        Enemy.Velocity = Vector2.Zero;
                        return;
                    }
                    Enemy.Velocity = _dashDirection * (Enemy.Speed * DashSpeedMultiplier);
                    break;
            }
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            _isDashing = false;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }

        /// <summary>动画事件命中：基类结算伤害（PerformAttackNow），再对攻击区内的玩家沿冲刺方向击退。</summary>
        protected override void OnAnimationHit()
        {
            base.OnAnimationHit();
            if (Enemy?.PlayerTarget is SamplePlayer player && AttackArea != null
                && player.IsHitByArea(AttackArea))
            {
                TryApplyPlayerKnockback(player, KnockbackDistance, KnockbackDuration, _dashDirection);
            }
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            _isDashing = false;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }
    }
}
