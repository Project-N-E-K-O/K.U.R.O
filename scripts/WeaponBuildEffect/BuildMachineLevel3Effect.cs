using System;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Builds
{
    /// <summary>
    /// 机械构筑 3 级效果：连续攻击命中时逐步提升额外最终伤害。
    /// 仅在玩家第一段攻击命中时触发，后续段伤害不计入连续计数。
    /// </summary>
    [GlobalClass]
    public partial class BuildMachineLevel3Effect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,10,1")] public int MaxBonusDamage { get; set; } = 2;
        [Export(PropertyHint.Range, "0,10,1")] public int BonusDamagePerChainStep { get; set; } = 1;
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float ComboWindowSeconds { get; set; } = 2.5f;
        [Export] public bool RequirePositiveDamage { get; set; } = true;

        private double _comboTimer;
        private bool _subscribed;
        private bool _applyingBonusDamage;
        private int _currentBonusDamage;

        public BuildMachineLevel3Effect()
        {
            EffectId = "build_machine_level3";
            DisplayName = "机械III";
            Description = "连续使用攻击时增加命中敌人时的最终伤害，最多增加 2 点";
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

            if (_currentBonusDamage != 0)
            {
                _currentBonusDamage = 0;
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
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor || target == null)
            {
                return;
            }

            // 只在第一段伤害时触发效果
            // 后续段伤害不应用加成，以避免多段攻击时加成被放大
            if (PlayerAttackTemplate.CurrentAttackHitStep != 1)
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

            _currentBonusDamage = ResolveNextBonusDamage();
            _comboTimer = ComboWindowSeconds;
            if (_currentBonusDamage <= 0)
            {
                return;
            }

            _applyingBonusDamage = true;
            try
            {
                // 使用 EffectBonus 源，避免触发 CriticalStrikeEffect 等监听 DirectAttack 的武器词条
                target.TakeDamage(_currentBonusDamage, Actor.GlobalPosition, Actor, DamageSource.EffectBonus);
            }
            finally
            {
                _applyingBonusDamage = false;
            }
        }

        private int ResolveNextBonusDamage()
        {
            int nextBonus = _comboTimer > 0d
                ? _currentBonusDamage + BonusDamagePerChainStep
                : 0;

            return Mathf.Clamp(nextBonus, 0, Math.Max(0, MaxBonusDamage));
        }
    }
}
