using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Actors.Enemies.Animation;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// guard5 防御反击（skill1 + skill2 一体，结构与 guard4 同族）：
    ///   · Warmup（skill1 跪地持盾）：**固定跪满 WarmupDuration（默认 3s）的完整前摇**，
    ///     期间**不做按帧范围检测**（站桩）；**结束时刻**才判定一次：玩家在触发区
    ///     （AttackDetectionArea）内 → 快照其坐标、锁定方向出手；不在 → **收招**：
    ///     挂住 Active 播完 skill1 的起身段（时长从动画段推导）再进 CD（不空发、不自晕）。
    ///   · Active（skill2 冲撞）：朝**快照方向**直线冲撞（出手瞬间锁定，可被侧移躲开）；
    ///     命中帧（动画事件）：对攻击区内的玩家结算伤害 + 沿冲撞方向击退 +
    ///     挂起眩晕（受击完整走完后由 Hit 状态转入 Frozen）。
    ///   · 落空（出手过、整个冲撞期间没碰到玩家）→ Recovery 前延迟一帧自进 Frozen（MissStunSeconds，默认 0.3s）。
    /// 起手条件 = 基类（检测圈 + 朝向角）+ 玩家在自配触发区里——纯查询、零副作用。
    /// </summary>
    public partial class EnemyGuard5ShieldStanceAttack : EnemyAttackTemplate
    {
        [ExportCategory("Shield Charge")]
        /// <summary>冲撞速度 = 基础 Speed × 倍率。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float DashSpeedMultiplier = 2f;
        /// <summary>冲撞持续（秒）：到时停位移，Active 由 ActiveDuration 自然收尾。</summary>
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float DashDuration = 2f;
        /// <summary>冲撞期间锁朝向（出手瞬间翻转对齐冲撞方向）。</summary>
        [Export] public bool LockFacingDuringDash = true;

        [ExportCategory("Miss / Player Stun")]
        /// <summary>冲撞落空（全程没碰到玩家）后的自晕时长（秒）。≤0 = 不晕。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float MissStunSeconds = 0.3f;
        /// <summary>冲撞命中后给玩家的眩晕时长（秒）。≤0 = 不晕玩家。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float PlayerStunSeconds = 0.8f;

        private Vector2 _chargeDirection = Vector2.Right;
        private bool _isCharging;
        private float _chargeElapsed;
        private bool _hitPlayer;
        private bool _standingUp;
        private bool _didCharge;
        private float _standupRemaining;

        /// <summary>收招中：warmup 到点无人 → 挂住 Active 播 skill1 起身段，播完转 Recovery 收尾。
        /// 动画控制器据此播起身段（而非 skill2）。</summary>
        public bool IsStandingUp => _standingUp;

        /// <summary>起手条件：基类（检测圈 + 朝向角）之上再要求玩家在自配触发区里。</summary>
        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInTriggerArea();
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            _isCharging = false;
            _chargeElapsed = 0f;
            _hitPlayer = false;
            _standingUp = false;
            _didCharge = false;
            _standupRemaining = 0f;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !IsRunning) return;

            if (_standingUp)
            {
                // 收招起身段：挂住 Active，计时（时长 = 起身动画段长度）到点转 Recovery 正常收尾
                if (CurrentPhase == AttackPhase.Active)
                {
                    _standupRemaining -= (float)delta;
                    if (_standupRemaining <= 0f)
                        ForceEnterRecoveryPhase();
                }
                return;
            }

            switch (CurrentPhase)
            {
                case AttackPhase.Warmup:
                    // 跪地期间站桩：固定跪满整个前摇（期间不做范围检测，出手判定放在结束时刻）
                    Enemy.Velocity = Vector2.Zero;
                    break;

                case AttackPhase.Active:
                    if (!_isCharging) return;
                    _chargeElapsed += (float)delta;
                    if (_chargeElapsed >= Mathf.Max(DashDuration, 0.05f))
                    {
                        _isCharging = false;
                        Enemy.Velocity = Vector2.Zero;
                        return;
                    }
                    Enemy.Velocity = _chargeDirection * (Enemy.Speed * DashSpeedMultiplier);
                    break;
            }
        }

        protected override void OnActivePhase()
        {
            // 跪满前摇后的判定时刻：玩家在触发区内 → 快照坐标、锁定方向出手；
            // 不在 → 收招：挂住 Active 播完 skill1 起身段（由 _PhysicsProcess 计时）再收尾进 CD，不空发不自晕
            if (!TrySnapshotChargeTarget())
            {
                _standingUp = true;
                _standupRemaining = ResolveStandupDuration();
                return;
            }

            _didCharge = true;
            base.OnActivePhase();   // 特效 + RequireAnimationHitTrigger 的命中窗口
            _isCharging = true;
            _chargeElapsed = 0f;
            if (Enemy != null) Enemy.Velocity = _chargeDirection * (Enemy.Speed * DashSpeedMultiplier);
        }

        /// <summary>收招期间挂住 Active（不自然流逝），由 _PhysicsProcess 的起身计时负责转 Recovery。</summary>
        protected override bool ShouldHoldActivePhase() => _standingUp;

        /// <summary>起身段时长：取自动画控制器的 skill1 起身段（单一来源）；拿不到节点时回退 0.5s。</summary>
        private float ResolveStandupDuration()
        {
            var anim = Enemy?.GetNodeOrNull<EnemyNormalGuard5SpineAnimationController>("AnimationController");
            return anim?.Skill1StandupDuration ?? 0.5f;
        }

        /// <summary>被打断后的收尾（由攻击控制器在"打断 = 清子攻击 CD"流程之后回调）：
        /// 把冷却补回到当前倍率对应的 CD——跪地被打断即"起手失败"，按普通 CD 结算。只补不缩，不影响已有冷却。</summary>
        public void ApplyInterruptedCooldown()
        {
            EnsureCooldown(GetCooldown());
        }

        /// <summary>出手判定 + 方向快照：玩家必须在触发区内；方向朝玩家当前位置锁定（可被侧移躲开）。</summary>
        private bool TrySnapshotChargeTarget()
        {
            if (Enemy?.PlayerTarget == null || !IsPlayerInTriggerArea()) return false;

            Vector2 from = Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition;
            _chargeDirection = from.LengthSquared() < 0.0001f
                ? (Enemy.FacingRight ? Vector2.Right : Vector2.Left)
                : from.Normalized();
            if (LockFacingDuringDash && Mathf.Abs(_chargeDirection.X) > 0.01f)
                Enemy.FlipFacing(_chargeDirection.X > 0f);
            return true;
        }

        /// <summary>命中帧（动画事件）：基类结算伤害，攻击区内的玩家额外吃击退 + 眩晕，并标记已命中（不算落空）。
        /// 眩晕不顶掉受击：玩家在受击中 → 挂起（受击完整走完、时长从 hit 结束起算）；否则即时眩晕兜底。</summary>
        protected override void OnAnimationHit()
        {
            base.OnAnimationHit();
            if (Enemy?.PlayerTarget is not SamplePlayer player) return;
            if (AttackArea == null || !player.IsHitByArea(AttackArea)) return;

            _hitPlayer = true;
            TryApplyPlayerKnockback(player, KnockbackDistance, KnockbackDuration, _chargeDirection);
            if (PlayerStunSeconds > 0f)
            {
                if (player.StateMachine?.CurrentState?.Name == "Hit")
                {
                    player.RequestStunAfterHit(PlayerStunSeconds);
                }
                else
                {
                    player.ApplyEffect(new FreezeEffect
                    {
                        Duration = PlayerStunSeconds,
                        EffectId = $"guard5_charge_{player.GetInstanceId()}_{Time.GetUnixTimeFromSystem()}",
                    });
                }
            }
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            _isCharging = false;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;

            // 落空（出手过、整个冲撞期间没碰到玩家）→ 延迟一帧自晕（避开状态切换流程内部）；
            // 无人收招（_didCharge=false）不算落空，不触发自晕
            if (_didCharge && !_hitPlayer)
                Callable.From(EnterMissStun).CallDeferred();
        }

        private void EnterMissStun()
        {
            if (MissStunSeconds <= 0f) return;
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy)) return;
            if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return;

            var sm = Enemy.StateMachine;
            if (sm == null) return;
            var frozen = sm.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (frozen == null) return;

            frozen.FrozenDuration = Mathf.Max(MissStunSeconds, 0.1f);
            sm.ChangeState("Frozen");
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            _isCharging = false;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }
    }
}
