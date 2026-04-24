using System;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Builds
{
    /// <summary>
    /// 机械构筑 3 级效果：连续攻击命中时逐步提升额外最终伤害。
    /// </summary>
    [GlobalClass]
    public partial class BuildMachineLevel3Effect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,10,1")] public int MaxBonusDamage { get; set; } = 2;
        [Export(PropertyHint.Range, "0,10,1")] public int BonusDamagePerChainStep { get; set; } = 1;
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float ComboWindowSeconds { get; set; } = 1.5f;
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

            DamageEventBus.Subscribe(OnDamageResolved);
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
                DamageEventBus.Unsubscribe(OnDamageResolved);
            }

            _subscribed = false;
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage)
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

            _currentBonusDamage = ResolveNextBonusDamage();
            _comboTimer = ComboWindowSeconds;
            if (_currentBonusDamage <= 0)
            {
                return;
            }

            _applyingBonusDamage = true;
            try
            {
                target.TakeDamage(_currentBonusDamage, Actor.GlobalPosition, Actor);
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
