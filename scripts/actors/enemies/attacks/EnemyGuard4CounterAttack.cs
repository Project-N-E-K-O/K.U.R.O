using Godot;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// guard4 防御反击——**一个技能模组的两个动作**：
    ///   · Warmup（skill1 招架）：站桩举刀循环，时长 = 招架窗口（场景配 WarmupDuration，默认 3s）。
    ///     期间敌人根脚本拦下正面 180° 的伤害（免伤免击退）并调 <see cref="NotifyParried"/>；
    ///     背面被打走正常受击流程，攻击由模板的受击打断机制接管。
    ///   · <see cref="NotifyParried"/>：立刻 <c>ForceEnterActivePhase()</c>（Warmup 提前结束）→
    ///     Active（skill2 反击）= 朝伤害来源方向突进 + 动画事件触发的横扫判定；
    ///     反击成立后冷却 = **基础倍率（场景配的 CooldownDurationMultiplier）× <see cref="CounterCooldownMultiplier"/>**（默认 2）。
    ///   · Warmup 自然到时且从未招架：OnActivePhase 里直接 <c>Cancel()</c> 收招进 CD——
    ///     不出 skill2、不空挥。
    /// 霸体只在 Active 期间（GrantedImmunities 配 ActiveSuperArmor）。
    /// </summary>
    public partial class EnemyGuard4CounterAttack : EnemyAttackTemplate
    {
        [ExportCategory("Counter Dash")]
        /// <summary>突进速度 = 基础 Speed × 倍率。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float DashSpeedMultiplier = 2f;
        /// <summary>突进持续（秒）：到时停位移，Active 由 ActiveDuration 自然收尾。</summary>
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float DashDuration = 0.3f;
        /// <summary>突进期间锁朝向（翻转对齐突进方向）。</summary>
        [Export] public bool LockFacingDuringDash = true;

        [ExportCategory("Counter")]
        /// <summary>反击成立后的冷却放大倍数：实际冷却 = **基础倍率**（场景里配的 CooldownDurationMultiplier）
        /// × 本值（默认 2 倍）。未招架收招时按基础倍率、不放大。</summary>
        [Export(PropertyHint.Range, "1,10,0.1")] public float CounterCooldownMultiplier = 2f;

        /// <summary>被打断（受击/眩晕）后的兜底眩晕时长（秒，默认 2；≤0 = 不兜底）。
        /// 模板内置的"受伤眩晕打断"依赖伤害事件发布时攻击仍在运行——部分打断路径攻击会先被清掉，
        /// 内置机制轮不到；控制器级打断回调是所有打断路径的必经点，在这里兜底保证"被打断 = 眩晕"必然成立。
        /// 两套同时生效时只会重复设同一个时长，无副作用。</summary>
        [Export(PropertyHint.Range, "0,10,0.1")] public float InterruptStunSeconds = 2f;

        /// <summary>场景里配置的基础冷却倍率（OnInitialized 时捕获——启动/收招时恢复到它，招架成立时在它之上放大）。</summary>
        private float _baseCooldownMultiplier = 1f;

        private bool _parried;
        private bool _isDashing;
        private float _dashElapsed;
        private Vector2 _dashDirection = Vector2.Right;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            // 场景在实例化时写入的 CooldownDurationMultiplier 就是"基础冷却倍率"，捕获后不再被覆写丢失
            _baseCooldownMultiplier = CooldownDurationMultiplier;
        }

        /// <summary>本次运行是否已招架成功（未招架 = 收招进 CD，不出 skill2）。</summary>
        public bool IsParried => _parried;

        /// <summary>招架命中（由敌人根脚本在"正面受击被挡下"时调用）：记录反击方向 → 立刻结束 Warmup
        /// 进入 Active（skill2 出手）→ 冷却按基础倍率放大。仅 Warmup 阶段且未招架过时生效。</summary>
        public void NotifyParried(Vector2 sourcePosition)
        {
            if (_parried || CurrentPhase != AttackPhase.Warmup) return;

            _parried = true;
            CooldownDurationMultiplier = _baseCooldownMultiplier * Mathf.Max(CounterCooldownMultiplier, 1f);

            Vector2 from = Enemy != null ? sourcePosition - Enemy.GlobalPosition : Vector2.Zero;
            _dashDirection = from.LengthSquared() < 0.0001f
                ? (Enemy?.FacingRight == true ? Vector2.Right : Vector2.Left)
                : from.Normalized();
            if (LockFacingDuringDash && Enemy != null && Mathf.Abs(_dashDirection.X) > 0.01f)
                Enemy.FlipFacing(_dashDirection.X > 0f);

            ForceEnterActivePhase();
        }

        /// <summary>被受击/眩晕打断后的收尾（由攻击控制器在"打断 = 清子攻击 CD"流程之后回调）：
        /// 把冷却补回到**当前倍率对应的 CD**——招架窗口内被打断即"招架失败"，按基础 CD（不放大）结算；
        /// 已招架成立（倍率已放大）时被打断则按放大后的 CD 结算。只补不缩，不影响已有冷却。</summary>
        public void ApplyInterruptedCooldown()
        {
            EnsureCooldown(GetCooldown());
        }

        /// <summary>被打断的眩晕兜底（由控制器在打断回调里调用）：延迟一帧把敌人打入
        /// <see cref="InterruptStunSeconds"/> 秒 Frozen。延迟是为了避开外层状态切换的流程内部
        /// （打断发生在状态机切换里，立即再 ChangeState 会被外层覆盖）。死亡/已死或时长为 0 时不处理。</summary>
        public void ApplyInterruptStun()
        {
            if (InterruptStunSeconds <= 0f) return;
            if (Enemy == null || Enemy.IsDeathSequenceActive || Enemy.IsDead) return;
            Callable.From(EnterInterruptStun).CallDeferred();
        }

        private void EnterInterruptStun()
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy)) return;
            if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return;

            var sm = Enemy.StateMachine;
            if (sm == null) return;

            var frozen = sm.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (frozen == null) return;

            frozen.FrozenDuration = Mathf.Max(InterruptStunSeconds, 0.1f);
            sm.ChangeState("Frozen");
        }

        /// <summary>起手条件：在基类（大检测圈 + 朝向角）之上**再要求玩家在自配的触发区里**
        /// （场景 TriggerAreaPath = AttackDetectionArea）——基类 CanStart 不查这张触发区，
        /// 不补的话玩家在圈外也能起手招架（KickAttack 同款约定）。</summary>
        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInTriggerArea();
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            _parried = false;
            _isDashing = false;
            _dashElapsed = 0f;
            CooldownDurationMultiplier = _baseCooldownMultiplier;   // 每次启动恢复到基础冷却；招架成立时在其之上放大
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }

        protected override void OnActivePhase()
        {
            // Warmup 自然到时且从未招架 → 不收招直接退出（进 CD），不出现 skill2
            if (!_parried)
            {
                Cancel();
                return;
            }

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
                    // 招架期间站桩：每帧归零防外部移动源漂移（与 Kick/MoveAttack 同款）
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

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            _isDashing = false;
            if (Enemy != null) Enemy.Velocity = Vector2.Zero;
        }
    }
}
