using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 攻击减速效果：命中敌人时，使目标移动速度降低 SlowPercent%，持续 SlowDuration 秒。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class SlowOnHitEffect : ActorEffect
    {
        /// <summary>
        /// 减速百分比（0~100），例如 50 表示速度降低 50%
        /// </summary>
        [Export(PropertyHint.Range, "1,100,1")]
        public float SlowPercent { get; set; } = 50f;

        /// <summary>
        /// 减速持续时间（秒）
        /// </summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")]
        public float SlowDuration { get; set; } = 2f;

        private bool _subscribed;

        public SlowOnHitEffect()
        {
            EffectId = "slow_on_hit";
            DisplayName = "攻击减速";
            Description = "攻击命中时使目标减速。";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();
            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor || target == null) return;
            if (damage <= 0) return;
            if (target.EffectController == null) return;

            // 创建减速 Debuff 并应用到目标
            var slowDebuff = new SpeedSlowDebuff
            {
                SlowPercent = SlowPercent,
                Duration = SlowDuration
            };
            target.ApplyEffect(slowDebuff);

            GD.Print($"[SlowOnHitEffect] 减速 {SlowPercent}%，持续 {SlowDuration}s → {target.Name}");
        }

        // ─── 内部 Debuff 类：挂载到目标身上，负责修改并恢复 Speed ───

        /// <summary>
        /// 减速 Debuff：在 OnApply 时降低目标 Speed，在 OnRemoved 时恢复。
        /// 同一目标重复命中时会刷新持续时间（依赖 ActorEffect 的 MaxStacks=1 叠加逻辑）。
        /// </summary>
        private sealed partial class SpeedSlowDebuff : ActorEffect
        {
            public float SlowPercent { get; set; }

            private float _originalSpeed;

            public SpeedSlowDebuff()
            {
                EffectId = "speed_slow_debuff";
                DisplayName = "减速";
                Description = "移动速度降低。";
                IsBuff = false;
                MaxStacks = 1;
            }

            protected override void OnApply()
            {
                base.OnApply();
                _originalSpeed = Actor.Speed;
                float multiplier = 1f - Mathf.Clamp(SlowPercent / 100f, 0f, 1f);
                Actor.Speed = _originalSpeed * multiplier;
            }

            public override void OnRemoved()
            {
                // 恢复原始速度
                if (Actor != null && !Actor.IsDead)
                {
                    Actor.Speed = _originalSpeed;
                }
                base.OnRemoved();
            }
        }
    }
}
