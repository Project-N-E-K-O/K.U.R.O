using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 受击状态——双时间轴模型（与 PlayerHitState 同构）：
    ///   动画轴 A = HitImpactDuration（hit 动画受击段时长，由动画控制器 partial 播放同步）
    ///   位移轴 K = 攻击方 KnockbackDuration（击退位移时长，匀减速滑完 KnockbackDistance）
    /// 交汇规则（回正开始 = max(A0, K0)）：
    ///   · A 先播完而位移未完 → 定格受击段末帧（partial 天然停在段尾）直到位移结束
    ///   · 位移先结束 → 动画按正常时序完整播放
    /// 动画由各 EnemyXxxSpineAnimationController 按 CurrentHitPhase/PhaseTick 驱动；
    /// 击退请求由攻击方在 Enter 之后写入（GameActor.ApplyKnockbackDisplacement / 旧 ApplyKnockback）。
    /// </summary>
    public partial class EnemyHitState : EnemyState, IHitReentrySuppressible
    {
        public enum HitPhase { Impact, Recover }

        /// <summary>hit 动画受击段时长（秒）——动画播完此段后定格等待位移（若位移未完）。</summary>
        [Export(PropertyHint.Range, "0.05,0.8,0.01")] public float HitImpactDuration = 0.2f;

        /// <summary>回正段播放时间（动画回正剩余 + 缓冲，如 0.13s）。</summary>
        [Export(PropertyHint.Range, "0.05,0.6,0.01")] public float RecoverDuration = 0.13f;

        /// <summary>当前受击阶段（供动画控制器查询/驱动）。</summary>
        public HitPhase CurrentPhase { get; private set; } = HitPhase.Impact;

        /// <summary>阶段序号（每次 Enter 递增）——供动画控制器检测重入后重播受击段。</summary>
        public int PhaseTick { get; private set; }

        /// <summary>动画需停帧：受击段动画已播完（A 尽）而位移未完（K>0）——控制器应将动画 time_scale 置 0 定格。</summary>
        public bool NeedsAnimHold => CurrentPhase == HitPhase.Impact && _animRemaining <= 0f && _hasKnock;

        /// <summary>连击打断上限：同一次 Hit 周期内允许完整打断（重播后仰）的次数——超过后抑制重入，
        /// 当前硬直自然走完让目标脱出（防屈死）；打击感靠前 N 次打断保留。</summary>
        [Export(PropertyHint.Range, "1,10,1")] public int MaxReentryBreaks { get; set; } = 3;

        /// <summary>本次 Hit 周期内已提供的打断次数。</summary>
        private int _comboBreaks;

        /// <summary>重入被抑制标记：消费到新位移请求时补重置（击退豁免——延迟一物理帧的完整重入）。</summary>
        private bool _reentrySuppressed;

        public bool OnReentryAttempted()
        {
            _comboBreaks++;
            return _comboBreaks <= MaxReentryBreaks;
        }

        public void NotifyReentrySuppressed() => _reentrySuppressed = true;

        // 动画轴 A
        private float _animRemaining;
        // 位移轴 K
        private bool _hasKnock;
        private Vector2 _knockDirection;
        private float _knockDistance;
        private float _knockDuration;
        private float _knockElapsed;
        // 回正计时
        private float _recoverRemaining;
        // Frozen 恢复
        private float _savedFrozenRemainingTime = 0f;

        public override void Enter()
        {
            PhaseTick++;
            CurrentPhase = HitPhase.Impact;
            _animRemaining = HitImpactDuration;
            _recoverRemaining = 0f;
            _reentrySuppressed = false;

            // 位移字段（_hasKnock/_knock*）保留：连击重入（同状态 Enter 重跑）不中断已在进行的击退位移；
            // 首次进入时字段本为 false（上次位移已结束）。仅无位移时清零残留速度。
            if (!_hasKnock)
                Enemy.Velocity = Vector2.Zero;
            // AnimPlayer 兜底（无 Spine 动画控制器的角色；Spine 敌人由控制器按阶段驱动）
            Enemy.AnimPlayer?.Play("animations/hit");

            // 检查是否从Frozen状态进入，并保存剩余时长
            if (Enemy.FrozenStateRemainingTime > 0f)
            {
                _savedFrozenRemainingTime = Enemy.FrozenStateRemainingTime;
                // 立即清空标志，防止后续重复使用
                Enemy.FrozenStateRemainingTime = 0f;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            float dt = (float)delta;

            // 消费击退请求（攻击方在 Enter 前后写入；滞留过期请求自动丢弃）
            if (!_hasKnock
                && Enemy.TryConsumeKnockbackRequest(out Vector2 dir, out float dist, out float dur,
                    HitImpactDuration))
            {
                _hasKnock = true;
                _knockDirection = dir;
                _knockDistance = dist;
                _knockDuration = dur;
                _knockElapsed = 0f;

                // 击退豁免：本次伤害带击退（重入被连击保护抑制）→ 补执行完整重入
                // （回受击段 + A 重置 + PhaseTick 递增触发动画重播——延迟一物理帧）
                if (_reentrySuppressed)
                {
                    _reentrySuppressed = false;
                    CurrentPhase = HitPhase.Impact;
                    _animRemaining = HitImpactDuration;
                    PhaseTick++;
                }
            }

            // 位移推进：K 内匀减速滑完 distance
            if (_hasKnock)
            {
                _knockElapsed += dt;
                if (_knockElapsed >= _knockDuration)
                {
                    Enemy.Velocity = Vector2.Zero;
                    _hasKnock = false;
                }
                else
                {
                    float v0 = 2f * _knockDistance / Mathf.Max(_knockDuration, 0.001f);
                    Enemy.Velocity = _knockDirection * (v0 * (1f - _knockElapsed / _knockDuration));
                }
                Enemy.MoveAndSlide();
            }
            // 无位移请求：不碰 Velocity/MoveAndSlide——外部击退通道（如 KnockbackDriver）
            // 直接驱动位移，Hit 状态清零会将其每帧杀掉；Enter 已清速度，静止状态自然保持

            switch (CurrentPhase)
            {
                case HitPhase.Impact:
                    if (_animRemaining > 0f)
                        _animRemaining -= dt;

                    if (_animRemaining <= 0f)
                    {
                        _animRemaining = 0f;
                        // 动画轴与位移轴都归零 → 回正（动画受击段播完由控制器停在段尾；位移未完则维持定格）
                        if (!_hasKnock)
                            EnterRecover();
                    }
                    break;

                case HitPhase.Recover:
                    _recoverRemaining -= dt;
                    if (_recoverRemaining <= 0f)
                    {
                        ExitHitState();
                        return;
                    }
                    break;
            }

            if (_savedFrozenRemainingTime > 0f)
                _savedFrozenRemainingTime -= (float)delta;
        }

        private void EnterRecover()
        {
            CurrentPhase = HitPhase.Recover;
            _recoverRemaining = RecoverDuration;
        }

        private void ExitHitState()
        {
            _comboBreaks = 0; // Hit 周期结束：打断计数复位（新一轮从完整打断开始）

            // 致死伤害：受击反馈（后仰 + 击退）已完整走完，直接转入死亡流程（Dying）
            if (Enemy.TryEnterDeferredDeath()) return;

            // 挂起眩晕（反霸体中断等在命中时请求）：受击完整走完 → 进眩晕，时长从此刻起算
            var pendingFrozen = Enemy.StateMachine?.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (pendingFrozen != null && Enemy.TryConsumeStunAfterHit(out float stunSeconds))
            {
                pendingFrozen.FrozenDuration = Mathf.Max(stunSeconds, 0.1f);
                ChangeState("Frozen");
                return;
            }

            // 若仍有活跃的 FreezeEffect，Hit 结束后转到该效果配置的目标状态
            var freezeEffect = Enemy.EffectController?.GetEffect<FreezeEffect>();
            if (freezeEffect != null)
            {
                _savedFrozenRemainingTime = 0f;
                ChangeState(freezeEffect.FrozenStateName);
                return;
            }

            // 若之前是从Frozen进入，且Frozen仍有剩余时长，则恢复Frozen
            if (_savedFrozenRemainingTime > 0f)
            {
                Enemy.FrozenStateRemainingTime = _savedFrozenRemainingTime;
                ChangeState("Frozen");
                _savedFrozenRemainingTime = 0f;
                return;
            }

            if (Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Walk");
            }
            else
            {
                ChangeState("Idle");
            }
        }
    }
}
