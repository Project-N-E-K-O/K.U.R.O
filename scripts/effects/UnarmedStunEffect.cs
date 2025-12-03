using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 在应用期间监听目标攻击事件，命中敌人时施加短暂冻结。
    /// </summary>
    [GlobalClass]
    public partial class UnarmedStunEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float StunDuration = 1.2f;

        protected override void OnApply()
        {
            base.OnApply();
            DamageEventBus.Subscribe(OnDamageResolved);
        }

        public override void OnRemoved()
        {
            DamageEventBus.Unsubscribe(OnDamageResolved);
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage)
        {
            if (Actor == null || attacker != Actor) return;
            TryApplyStun(target);
        }

        private void TryApplyStun(GameActor target)
        {
            if (target == null) return;
            var freeze = new FreezeEffect
            {
                Duration = StunDuration,
                EffectId = $"unarmed_stun_{target.GetInstanceId()}_{Time.GetUnixTimeFromSystem()}"
            };
            target.ApplyEffect(freeze);
        }
    }
}


