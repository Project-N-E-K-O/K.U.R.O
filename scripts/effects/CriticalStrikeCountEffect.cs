using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 武器计数暴击效果：每N次攻击命中敌人后的下一次攻击必定触发暴击
    /// 追加等量伤害（相当于伤害 ×2）。
    /// 切换武器后失效，重新装备后重新计数。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class CriticalStrikeCountEffect : ActorEffect
    {
        /// <summary>
        /// 每N次命中后，下一次攻击必定暴击
        /// </summary>
        [Export(PropertyHint.Range, "1,20,1")]
        public int HitCount { get; set; } = 5;

        /// <summary>
        /// 暴击时追加伤害的倍率（默认 1.0 = 追加 1 倍武器攻击力）
        /// </summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float CritBonusMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// 武器自身的攻击力数值（暴击只对此部分乘倍率）。
        /// 公式：总伤害 = 原始伤害（基础+武器+其他效果） + WeaponAttackValue × CritBonusMultiplier
        /// 应在 ItemDefinition 的 EffectEntry 属性覆盖中与武器攻击力保持一致。
        /// </summary>
        [Export(PropertyHint.Range, "0,9999,1")]
        public float WeaponAttackValue { get; set; } = 0f;

        private bool _subscribed;
        private int _hitCounter;
        private bool _nextHitCrits;

        public CriticalStrikeCountEffect()
        {
            EffectId = "critical_strike_count";
            DisplayName = "武器计数暴击";
            Description = "每N次攻击命中敌人后的下一次攻击必定触发暴击。";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();
            _hitCounter = 0;
            _nextHitCrits = false;
            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
            GD.Print($"[CriticalStrikeCountEffect] OnApply - HitCount={HitCount}, WeaponAttackValue={WeaponAttackValue}, Actor={Actor?.Get("display_name") ?? "null"}");
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            _hitCounter = 0;
            _nextHitCrits = false;
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor || target == null) return;
            if (damage <= 0) return;

            _hitCounter++;

            bool shouldCrit = false;

            if (_nextHitCrits)
            {
                shouldCrit = true;
                _nextHitCrits = false;
                _hitCounter = 0;
            }
            else if (_hitCounter >= HitCount)
            {
                _nextHitCrits = true;
            }

            GD.Print($"[CriticalStrikeCountEffect] 命中! counter={_hitCounter}, HitCount={HitCount}, nextCrit={_nextHitCrits}, shouldCrit={shouldCrit}, WeaponAttackValue={WeaponAttackValue}");

            if (!shouldCrit) return;

            int bonusDamage = Mathf.RoundToInt(WeaponAttackValue * CritBonusMultiplier);
            GD.Print($"[CriticalStrikeCountEffect] bonusDamage={bonusDamage} (WeaponAttackValue={WeaponAttackValue} × CritBonusMultiplier={CritBonusMultiplier})");

            if (bonusDamage <= 0) return;

            target.TakeDamage(bonusDamage, Actor.GlobalPosition, Actor, DamageSource.CritBonus);

            GD.Print($"[CriticalStrikeCountEffect] 计数暴击触发！每{HitCount}次命中，追加伤害 {bonusDamage}");
        }
    }
}
