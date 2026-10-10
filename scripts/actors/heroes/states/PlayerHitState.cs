using Godot;
using System;
using Kuros.Core;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 受击状态——双时间轴模型：
    ///   动画轴 A = HitImpactDuration（hit 动画受击段时长，动画实时播放）
    ///   位移轴 K = 攻击方 KnockbackDuration（击退位移时长，匀减速滑完 KnockbackDistance）
    /// 交汇规则（回正开始 = max(A0, K0)）：
    ///   · A 先播完而位移未完（HitImpactDuration < KnockbackDuration）→ 停帧在受击段末帧
    ///     直到位移结束（冻结延长，无输入）
    ///   · 位移先结束（KnockbackDuration ≤ HitImpactDuration）→ 动画按正常时序完整播放
    ///     （位移早停不影响动画，受击段播完自然回正）
    /// 无击退请求：仅 A 计时 → hit 动画完整自然时序（受击段 + 回正段）。
    /// 击退请求由攻击方在 Enter 之后写入（GameActor.ApplyKnockbackDisplacement / 旧 ApplyKnockback）。
    /// </summary>
    public partial class PlayerHitState : PlayerState, IHitReentrySuppressible
    {
        public enum HitPhase { Impact, Recover }

        [Export] public string SpineHitAnimationName = "hit";
        public float HitAnimationSpeed = 1.0f;

        /// <summary>hit 动画受击段时长（秒）——动画播完此段后停帧等待位移（若位移未完）。</summary>
        [Export(PropertyHint.Range, "0.05,0.8,0.01")] public float HitImpactDuration = 0.2f;

        /// <summary>回正段播放时间（动画回正剩余 + 缓冲，如 0.13s）。</summary>
        [Export(PropertyHint.Range, "0.05,0.6,0.01")] public float RecoverDuration = 0.13f;

        /// <summary>冻结期动画（空 = 停帧在受击段末帧；资源就绪后填 fly 动画名）。</summary>
        [Export] public string FlyAnimation { get; set; } = "";

        /// <summary>当前受击阶段（供动画层查询/驱动）。</summary>
        public HitPhase CurrentPhase { get; private set; } = HitPhase.Impact;

        /// <summary>阶段序号（每次 Enter 递增）——供动画层检测重入。</summary>
        public int PhaseTick { get; private set; }

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

        private float _originalSpeedScale = 1.0f;

        // 动画轴 A
        private float _animRemaining;
        // 位移轴 K（击退请求，Enter 后由攻击方写入）
        private bool _hasKnock;
        private Vector2 _knockDirection;
        private float _knockDistance;
        private float _knockDuration;
        private float _knockElapsed;
        // 冻结标记
        private bool _frozen;
        private bool _flyActive;
        // 回正计时
        private float _recoverRemaining;

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
                Actor.Velocity = Vector2.Zero;

            _frozen = false;
            _flyActive = false;

            if (Player is MainCharacter mainChar)
            {
                string spineAnim = string.IsNullOrWhiteSpace(SpineHitAnimationName)
                    ? "hit"
                    : SpineHitAnimationName;
                PlayAnimation(spineAnim, false, HitAnimationSpeed);
                mainChar.StartHitInvincibility();
            }

            if (Actor.AnimPlayer != null)
            {
                // Save original speed scale before modifying
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;

                string hitAnimation = ResolveHitAnimationName();
                if (!string.IsNullOrEmpty(hitAnimation))
                {
                    Actor.AnimPlayer.Play(hitAnimation);
                }
                // Set animation playback speed only for hit animation
                Actor.AnimPlayer.SpeedScale = HitAnimationSpeed;
            }
        }

        public override void Exit()
        {
            _comboBreaks = 0; // Hit 周期结束：打断计数复位（新一轮从完整打断开始）

            // Restore original animation speed when leaving hit state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }

            // Spine 停帧恢复（防从冻结阶段被强制切走）
            if (Player is MainCharacter mainChar)
                mainChar.SetSpineAnimationSpeed(1f);
        }

        public override void PhysicsUpdate(double delta)
        {
            float dt = (float)delta;

            // 消费击退请求（攻击方在 Enter 前后写入；首个物理帧即就绪；滞留过期请求自动丢弃）
            if (!_hasKnock
                && Actor.TryConsumeKnockbackRequest(out Vector2 dir, out float dist, out float dur,
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

            // 位移推进：K 内匀减速滑完 distance（v0 = 2d/K，末速 0）；撞墙由 MoveAndSlide 自然早停
            if (_hasKnock)
            {
                _knockElapsed += dt;
                if (_knockElapsed >= _knockDuration)
                {
                    Actor.Velocity = Vector2.Zero;
                    _hasKnock = false;
                }
                else
                {
                    float v0 = 2f * _knockDistance / Mathf.Max(_knockDuration, 0.001f);
                    Actor.Velocity = _knockDirection * (v0 * (1f - _knockElapsed / _knockDuration));
                }
                Actor.MoveAndSlide();
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

                        // 动画受击段播完而位移未完 → 定格在受击段末帧（或 fly）等待位移结束
                        if (_hasKnock && !_frozen)
                        {
                            _frozen = true;
                            FreezeAnimation();
                        }

                        // 动画轴与位移轴都归零 → 回正
                        if (!_hasKnock)
                            EnterRecover();
                    }
                    break;

                case HitPhase.Recover:
                    // 受身：回正段可按 dash 打断恢复（与 Idle→Dash 同模式，无条件切；
                    // 能否冲刺由 Dash 状态自身的充能判定，此处不耦合）。此刻击退位移已结束，无冲突。
                    // 例外：有挂起眩晕（攻方命中时请求）时眩晕优先——受身也逃不掉，直接进 Frozen。
                    if (IsActionJustPressed("dash"))
                    {
                        if (TryEnterPendingStun()) return;
                        ChangeState("Dash");
                        return;
                    }
                    _recoverRemaining -= dt;
                    if (_recoverRemaining <= 0f)
                    {
                        // 致死伤害：受击反馈（后仰 + 击退）已完整走完，转入死亡流程（Dying）
                        if (Actor.TryEnterDeferredDeath()) return;

                        // 挂起眩晕（攻击方在命中时请求）：受击完整走完 → 进眩晕，时长从此刻起算
                        if (TryEnterPendingStun()) return;

                        ChangeState("Idle");
                        return;
                    }
                    break;
            }
        }

        private void FreezeAnimation()
        {
            if (Player is not MainCharacter mainChar) return;

            if (!string.IsNullOrWhiteSpace(FlyAnimation))
            {
                mainChar.PlaySpineAnimation(FlyAnimation, loop: true);
                _flyActive = true;
            }
            else
            {
                // 停帧：动画此刻正停在受击段末帧（HitImpactDuration 帧）——回正时续播无跳变
                mainChar.SetSpineAnimationSpeed(0f);
                _flyActive = false;
            }
        }

        /// <summary>消费挂起眩晕并转入 Frozen（时长从此刻起算）。返回是否成功进入眩晕。</summary>
        private bool TryEnterPendingStun()
        {
            var sm = Actor.StateMachine;
            var frozen = sm?.GetNodeOrNull<PlayerFrozenState>("Frozen");
            if (frozen == null) return false;
            if (!Actor.TryConsumeStunAfterHit(out float stunSeconds)) return false;

            frozen.FrozenDuration = Mathf.Max(stunSeconds, 0.1f);
            ChangeState("Frozen");
            // 眩晕免疫（PlayerFrozenState.CanEnterFrom）拒绝时状态未变 → 返回 false，调用方按原流程继续
            return sm?.CurrentState?.Name == "Frozen";
        }

        private void EnterRecover()
        {
            CurrentPhase = HitPhase.Recover;
            _recoverRemaining = RecoverDuration;

            if (Player is not MainCharacter mainChar) return;

            if (_flyActive)
            {
                // 从 fly 切回 hit 回正段（动画不在 hit 时间轴上，需跳回受击段末帧）
                string spineAnim = string.IsNullOrWhiteSpace(SpineHitAnimationName)
                    ? "hit"
                    : SpineHitAnimationName;
                mainChar.SetSpineAnimationSpeed(1f);
                mainChar.PlaySpineAnimationFrom(spineAnim, HitImpactDuration, loop: false);
                _flyActive = false;
                return;
            }

            // 无 fly：动画若被停帧则恢复播放——停在受击段末帧，续播即回正段（无缝）
            mainChar.SetSpineAnimationSpeed(1f);
        }

        private string ResolveHitAnimationName()
        {
            if (Actor.AnimPlayer == null)
            {
                return string.Empty;
            }

            string[] candidates =
            {
                "animations/hit",
                "animations/Hit",
                "hit",
                "Hit"
            };

            foreach (string candidate in candidates)
            {
                if (Actor.AnimPlayer.HasAnimation(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }
    }
}
