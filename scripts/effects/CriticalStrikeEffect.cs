using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Effects
{
    /// <summary>
    /// 武器暴击效果：每次攻击命中敌人时，有 CritChance% 的概率触发暴击，
    /// 追加等量伤害（相当于伤害 ×2）。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class CriticalStrikeEffect : ActorEffect
    {
        /// <summary>
        /// 暴击概率（0~100，单位 %）
        /// </summary>
        [Export(PropertyHint.Range, "0,100,1")]
        public float CritChance { get; set; } = 20f;

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

        public CriticalStrikeEffect()
        {
            EffectId = "critical_strike";
            DisplayName = "武器暴击";
            Description = "攻击命中时有概率触发暴击，伤害翻倍。";
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

            // 概率判断
            if (GD.Randf() * 100f > CritChance) return;

            // 暴击追加伤害只乘武器攻击部分，而非全部伤害
            // 正确公式：总暴击伤害 = 原始伤害 + WeaponAttackValue × CritBonusMultiplier
            // 而非：原始伤害 × (1 + CritBonusMultiplier)
            int bonusDamage = Mathf.RoundToInt(WeaponAttackValue * CritBonusMultiplier);
            if (bonusDamage <= 0) return;

            // 使用 CritBonus 来源，FloatingDamageTextManager 会将飘字升级为红色暴击显示
            target.TakeDamage(bonusDamage, Actor.GlobalPosition, Actor, DamageSource.CritBonus);

            GD.Print($"[CriticalStrikeEffect] 暴击！概率 {CritChance}%，武器攻击 {WeaponAttackValue}×{CritBonusMultiplier} 追加伤害 {bonusDamage}");
        }
    }
}
