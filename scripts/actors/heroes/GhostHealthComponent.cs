using System;
using Godot;
using Kuros.Core;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 虚血（Ghost Health）组件：挂在对象根节点下，管理三层血条所需的显示值状态机。
    /// - 受伤：HealthFillBar（真实红条）立即掉；虚血（GhostValue）停留受伤前值一段时间后缓慢下降；
    ///   HurtBar（深红虚血条）锁定跟随虚血。
    /// - 治疗：虚血（逻辑值）立即对齐当前血量；RecoveryBar（青色恢复条）立即=当前血量（露出恢复段）；
    ///   HealthFillBar 与 HurtBar 停留一段时间后缓慢上升到当前血量。
    /// - 部分治疗（治疗量 &lt; 虚血差）：虚血保持保留状态继续下降，HurtBar 仍跟随虚血。
    /// 虚血不是真实生命，游戏逻辑只看 GameActor.CurrentHealth；本组件仅驱动显示与提供 Build 钩子。
    /// </summary>
    [GlobalClass]
    public partial class GhostHealthComponent : Node
    {
        [ExportCategory("Ghost (受伤虚血)")]
        /// <summary>受伤后虚血停留时长（秒），之后开始缓慢下降。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float GhostHoldDuration { get; set; } = 0.6f;
        /// <summary>虚血下降速度（MaxHealth 比例/秒，等比随血量缩放）：0.6 = 每秒下降 60% 最大血量。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float GhostDescendRatio { get; set; } = 0.6f;
        /// <summary>单次虚血回落的最短时长（秒）：剩余差值再小也至少用这么久走完，
        /// 避免 MaxHealth 很大时 1 点虚血在第一帧就瞬移消失。0 = 关闭（纯按 GhostDescendRatio）。</summary>
        [Export(PropertyHint.Range, "0,2,0.01")] public float GhostMinFallDuration { get; set; } = 0.25f;

        [ExportCategory("Heal (治疗恢复)")]
        /// <summary>治疗时红条/虚血条停留时长（秒），之后缓慢上升。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float HealHoldDuration { get; set; } = 0.25f;
        /// <summary>治疗时红条/虚血条上升速度（MaxHealth 比例/秒，等比随血量缩放）：1.2 = 每秒上升 120% 最大血量。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float HealRiseRatio { get; set; } = 1.2f;

        /// <summary>显示值/虚血状态变化时触发（预留，Build 卡牌可监听）。</summary>
        public event Action? GhostStateChanged;

        private GameActor? _actor;
        private float _previousHealth;

        private float _ghostHoldTimer;      // 受伤后虚血停留计时
        private bool _ghostDescending;      // 虚血下降中
        /// <summary>本次回落使用的固定速度（开始回落时按剩余差值算一次，避免逐帧重算导致长尾）。</summary>
        private float _ghostFallSpeed;
        private float _hurtHoldTimer;       // 治疗时 HurtBar 停留计时
        private bool _hurtRising;           // HurtBar 治疗上升中
        private float _fillHoldTimer;       // 治疗时 HealthFillBar 停留计时
        private bool _fillRising;           // HealthFillBar 治疗上升中
        private bool _hurtLockedToGhost = true;  // HurtBar 显示锁定跟随虚血（受伤/部分治疗模式）

        /// <summary>逻辑虚血值（钩子判定与 Build 卡牌恢复的目标）。</summary>
        public float GhostValue { get; private set; }
        /// <summary>HurtBar（深红虚血条）显示值。</summary>
        public float HurtDisplay { get; private set; }
        /// <summary>HealthFillBar（真实红条）显示值。</summary>
        public float FillValue { get; private set; }

        public override void _Ready()
        {
            _actor = GetParent() as GameActor;
            if (_actor == null) return;

            _actor.HealthChanged += OnHealthChanged;
            // 初始显示值需等父级 GameActor._Ready 把 CurrentHealth 设为 MaxHealth 后再读取：
            // 子节点 _Ready 先于父节点执行，此时 CurrentHealth 仍是 0，直接读取会导致
            // 生成时虚血从 0 动画升到满值。延迟到本帧所有 _Ready 完成后初始化。
            CallDeferred(nameof(InitializeDisplayValues));
        }

        private void InitializeDisplayValues()
        {
            if (_actor == null || !IsInsideTree()) return;

            float h = Mathf.Max(0f, _actor.CurrentHealth);
            GhostValue = h;
            HurtDisplay = h;
            FillValue = h;
            _previousHealth = _actor.CurrentHealth;
            _ghostHoldTimer = 0f;
            _ghostDescending = false;
            _ghostFallSpeed = 0f;
            _hurtHoldTimer = 0f;
            _hurtRising = false;
            _fillHoldTimer = 0f;
            _fillRising = false;
            _hurtLockedToGhost = true;
        }

        public override void _ExitTree()
        {
            if (_actor != null)
            {
                _actor.HealthChanged -= OnHealthChanged;
            }
            base._ExitTree();
        }

        private void OnHealthChanged(int health, int maxHealth)
        {
            float prev = _previousHealth;
            _previousHealth = health;

            if (health < prev)
            {
                // 受伤：红条立即掉；虚血停留受伤前值后缓慢下降；HurtBar 锁定跟随虚血
                FillValue = health;
                _fillHoldTimer = 0f;
                _fillRising = false;
                _ghostDescending = false;
                _ghostFallSpeed = 0f;
                _ghostHoldTimer = GhostHoldDuration;
                _hurtLockedToGhost = true;
                _hurtRising = false;
                _hurtHoldTimer = 0f;
                HurtDisplay = GhostValue;
                if (GhostValue <= health) GhostValue = health;
            }
            else if (health > prev)
            {
                if (health >= GhostValue)
                {
                    // 完全对齐治疗：虚血立即对齐当前血量；HurtBar 进入治疗动画（停留后缓升）
                    GhostValue = health;
                    _ghostDescending = false;
                    _ghostHoldTimer = 0f;
                    _hurtLockedToGhost = false;
                    _hurtRising = false;
                    _hurtHoldTimer = HealHoldDuration;
                }
                // 部分治疗（health < GhostValue）：虚血保留继续下降，HurtBar 保持锁定跟随
                _fillRising = false;
                _fillHoldTimer = HealHoldDuration;
            }
            else
            {
                // max 变化等：clamp 显示值
                GhostValue = Mathf.Clamp(GhostValue, health, maxHealth);
                HurtDisplay = Mathf.Clamp(HurtDisplay, health, maxHealth);
                FillValue = Mathf.Clamp(FillValue, health, maxHealth);
            }

            GhostStateChanged?.Invoke();
        }

        public override void _Process(double delta)
        {
            if (_actor == null) return;
            float h = Mathf.Max(0f, _actor.CurrentHealth);
            float maxH = Mathf.Max(1f, _actor.MaxHealth);
            float dt = (float)delta;

            // 虚血下降（受伤）：停留结束后缓慢下降到当前血量
            if (_ghostHoldTimer > 0f)
            {
                _ghostHoldTimer -= dt;
            }
            else if (!_ghostDescending && GhostValue > h + 0.01f)
            {
                _ghostDescending = true;
                // 回落开始：按当时的剩余差值算一次固定速度。
                // 正常速度按 MaxHealth 缩放；但差值很小时用 remaining / 最短时长兜底，
                // 保证 1 点虚血也不会在一帧内消失。两者取较小值（大差值仍走正常速度）。
                float remaining = GhostValue - h;
                float normalSpeed = Mathf.Max(0f, GhostDescendRatio * maxH);
                float minDurationSpeed = GhostMinFallDuration > 0f
                    ? remaining / GhostMinFallDuration
                    : float.PositiveInfinity;
                _ghostFallSpeed = Mathf.Min(normalSpeed, minDurationSpeed);
                if (_ghostFallSpeed <= 0f)
                    _ghostFallSpeed = normalSpeed > 0f ? normalSpeed : 0.01f;
            }
            if (_ghostDescending)
            {
                GhostValue = Mathf.Max(h, GhostValue - _ghostFallSpeed * dt);
                if (GhostValue <= h + 0.01f)
                {
                    GhostValue = h;
                    _ghostDescending = false;
                }
            }

            // HurtBar：受伤模式锁定跟随虚血；治疗模式停留后缓慢上升到当前血量
            if (_hurtLockedToGhost)
            {
                HurtDisplay = GhostValue;
            }
            else
            {
                if (_hurtHoldTimer > 0f)
                {
                    _hurtHoldTimer -= dt;
                }
                else if (!_hurtRising && HurtDisplay < h - 0.01f)
                {
                    _hurtRising = true;
                }
                if (_hurtRising)
                {
                    HurtDisplay = Mathf.Min(h, HurtDisplay + HealRiseRatio * maxH * dt);
                    if (HurtDisplay >= h - 0.01f)
                    {
                        HurtDisplay = h;
                        _hurtRising = false;
                    }
                }
            }

            // HealthFillBar 治疗上升：停留后缓慢上升到当前血量
            if (_fillHoldTimer > 0f)
            {
                _fillHoldTimer -= dt;
            }
            else if (!_fillRising && FillValue < h - 0.01f)
            {
                _fillRising = true;
            }
            if (_fillRising)
            {
                FillValue = Mathf.Min(h, FillValue + HealRiseRatio * maxH * dt);
                if (FillValue >= h - 0.01f)
                {
                    FillValue = h;
                    _fillRising = false;
                }
            }
        }

        /// <summary>虚血保留是否激活（虚血 &gt; 当前血量，Build 卡牌判定用）。</summary>
        public bool IsRetentionActive => GhostValue > (_actor?.CurrentHealth ?? 0f) + 0.01f;

        /// <summary>当前可恢复的虚血量（GhostValue - 当前血量，≥ 0）。</summary>
        public float RetentionAmount => Mathf.Max(0f, GhostValue - (_actor?.CurrentHealth ?? 0f));

        /// <summary>
        /// 虚血恢复钩子：按造成伤害的比例将实时生命恢复到虚血值（Build 卡牌在虚血保留期间调用）。
        /// 返回实际恢复量（0 = 无虚血保留或无需恢复）。
        /// </summary>
        public int RecoverFromRetention(int damage, float ratio)
        {
            if (_actor == null || damage <= 0 || ratio <= 0f) return 0;
            if (!IsRetentionActive) return 0;

            int heal = Mathf.Min((int)(damage * ratio), (int)Mathf.Ceil(RetentionAmount));
            if (heal <= 0) return 0;

            _actor.RestoreHealth(_actor.CurrentHealth + heal);
            return heal;
        }
    }
}
