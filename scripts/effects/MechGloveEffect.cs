using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Effects
{
    /// <summary>
    /// 机械冲撞臂专属效果：同一轮攻击的三段 Hit 全部命中敌人时，
    /// 最后一段触发暴击，追加等量伤害（相当于伤害翻倍）。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class MechGloveEffect : ActorEffect
    {
        /// <summary>
        /// 暴击时额外伤害的倍率（默认 1.0 表示追加 1 倍伤害，即总计 2 倍）
        /// </summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float CritBonusMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// 机械冲撞臂攻击的总段数（默认 3 段）
        /// </summary>
        [Export(PropertyHint.Range, "2,10,1")]
        public int TotalHitSteps { get; set; } = 3;

        private bool[] _stepHit = System.Array.Empty<bool>();
        private bool _critApplied;
        private int _lastTrackedStep;
        private bool _subscribed;

        public MechGloveEffect()
        {
            EffectId = "mech_glove_crit";
            DisplayName = "机械冲撞臂·暴击";
            Description = "三段攻击均命中时，最后一段暴击，伤害翻倍。";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();
            ResetCombo();
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

            int step = PlayerAttackTemplate.CurrentAttackHitStep;

            // 段数归 1 或出现倒退，说明开始了新一轮攻击，重置连击状态
            if (step == 1 || step < _lastTrackedStep)
            {
                ResetCombo();
            }

            _lastTrackedStep = step;

            int stepIndex = step - 1;
            if (stepIndex < 0 || stepIndex >= TotalHitSteps)
            {
                return;
            }

            _stepHit[stepIndex] = true;

            // 只在最后一段触发暴击判定
            if (step != TotalHitSteps)
            {
                return;
            }

            // 检查所有前置段是否全部命中
            if (_critApplied)
            {
                return;
            }

            for (int i = 0; i < TotalHitSteps; i++)
            {
                if (!_stepHit[i])
                {
                    return;
                }
            }

            // 全部命中，触发暴击追加伤害
            _critApplied = true;
            int bonusDamage = Mathf.RoundToInt(damage * CritBonusMultiplier);
            if (bonusDamage <= 0)
            {
                return;
            }

            // 使用 CritBonus 来源，FloatingDamageTextManager 会将对应飘字升级为红色暴击显示
            target.TakeDamage(bonusDamage, Actor.GlobalPosition, Actor, DamageSource.CritBonus);

            GD.Print($"[MechGloveEffect] 暴击触发！{TotalHitSteps} 段全中，追加伤害 {bonusDamage}，本段总伤害 {damage + bonusDamage}");
        }

        private void ResetCombo()
        {
            _stepHit = new bool[Mathf.Max(1, TotalHitSteps)];
            _critApplied = false;
            _lastTrackedStep = 0;
        }
    }
}
