using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using System;

namespace Kuros.Builds
{
    /// <summary>
    /// 机械构筑 1 级效果：玩家受击后减弱受到的击退力度。
    /// </summary>
    [GlobalClass]
    public partial class BuildMachineLevel1Effect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,1,0.05")] public float KnockbackMultiplier { get; set; } = 0.2f;
        [Export(PropertyHint.Range, "0.01,0.5,0.01")] public float KnockbackAdjustWindowSeconds { get; set; } = 0.1f;

        private double _pendingKnockbackAdjustTime;
        private Vector2 _lastAdjustedVelocity = Vector2.Zero;
        private bool _subscribed;

        public BuildMachineLevel1Effect()
        {
            EffectId = "build_machine_level1";
            DisplayName = "机械I";
            Description = "减少玩家受到攻击时的击退力度";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null || _subscribed)
            {
                return;
            }

            GameActor.AnyDamageTaken += OnAnyDamageTaken;
            _subscribed = true;
        }

        protected override void OnTick(double delta)
        {
            if (Actor == null || _pendingKnockbackAdjustTime <= 0d)
            {
                return;
            }

            _pendingKnockbackAdjustTime = Math.Max(0d, _pendingKnockbackAdjustTime - delta);
            Vector2 velocity = Actor.Velocity;
            if (velocity == Vector2.Zero || velocity == _lastAdjustedVelocity)
            {
                return;
            }

            Actor.Velocity = velocity * Mathf.Clamp(KnockbackMultiplier, 0f, 1f);
            _lastAdjustedVelocity = Actor.Velocity;
            _pendingKnockbackAdjustTime = 0d;
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                GameActor.AnyDamageTaken -= OnAnyDamageTaken;
            }

            _subscribed = false;
            base.OnRemoved();
        }

        private void OnAnyDamageTaken(GameActor victim, GameActor? attacker, int damage)
        {
            if (Actor == null || victim != Actor || damage <= 0)
            {
                return;
            }

            _pendingKnockbackAdjustTime = KnockbackAdjustWindowSeconds;
            _lastAdjustedVelocity = Vector2.Zero;
        }
    }
}
