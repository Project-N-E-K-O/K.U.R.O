using System;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds
{
    /// <summary>
    /// 机械构筑 3 级效果：连续攻击命中时逐步提升额外最终伤害。
    /// 仅在玩家第一段攻击命中时触发，后续段伤害不计入连续计数。
    /// </summary>
    [GlobalClass]
    public partial class BuildMachineLevel3Effect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,100,1")] public float MaxBonusPercent { get; set; } = 20f;
        [Export(PropertyHint.Range, "0,50,1")] public float BonusPercentPerStep { get; set; } = 5f;
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float ComboWindowSeconds { get; set; } = 2.5f;
        [Export] public bool RequirePositiveDamage { get; set; } = true;

        private double _comboTimer;
        private bool _subscribed;
        private bool _applyingBonusDamage;
        private float _currentBonusPercent;

        public BuildMachineLevel3Effect()
        {
            EffectId = "build_machine_level3";
            DisplayName = "机械III";
            Description = "连续使用攻击时增加命中敌人时的最终伤害，最多增加 20% ";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();
            if (_subscribed)
            {
                return;
            }

            DamageEventBus.SubscribeWithSource(OnDamageResolved);
            _subscribed = true;
        }

        protected override void OnTick(double delta)
        {
            if (_comboTimer > 0)
            {
                _comboTimer = Math.Max(0d, _comboTimer - delta);
                return;
            }

            if (_currentBonusPercent > 0f)
            {
                _currentBonusPercent = Mathf.Max(0f,
                    _currentBonusPercent - BonusPercentPerStep * (float)delta);
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
            }

            _subscribed = false;
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (Actor == null || attacker != Actor || target == null)
            {
                return;
            }

            if (RequirePositiveDamage && damage <= 0)
            {
                return;
            }

            if (_applyingBonusDamage)
            {
                return;
            }

            _currentBonusPercent = ResolveNextBonusPercent();
            _comboTimer = ComboWindowSeconds;
            if (_currentBonusPercent <= 0f)
            {
                return;
            }

            _applyingBonusDamage = true;
            try
            {
                int bonusDamage = Mathf.Max(1, Mathf.RoundToInt(damage * _currentBonusPercent / 100f));
                // 使用 EffectBonus 源，避免触发 CriticalStrikeEffect 等监听 DirectAttack 的武器词条
                target.TakeDamage(bonusDamage, Actor.GlobalPosition, Actor, DamageSource.EffectBonus);
            }
            finally
            {
                _applyingBonusDamage = false;
            }
        }

        private float ResolveNextBonusPercent()
        {
            float nextPercent = _comboTimer > 0d
                ? _currentBonusPercent + BonusPercentPerStep
                : 0f;

            return Mathf.Clamp(nextPercent, 0f, Mathf.Max(0f, MaxBonusPercent));
        }
    }
}
