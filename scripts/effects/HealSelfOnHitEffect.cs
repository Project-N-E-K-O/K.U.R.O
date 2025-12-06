using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 拥有者每次攻击命中敌人时回复固定生命值。
    /// </summary>
    [GlobalClass]
    public partial class HealSelfOnHitEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "1,50,1")] public int HealAmount { get; set; } = 1;
        [Export] public bool RequirePositiveDamage { get; set; } = true;

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
            if (HealAmount <= 0) return;
            if (RequirePositiveDamage && damage <= 0) return;

            int newHealth = Mathf.Clamp(Actor.CurrentHealth + HealAmount, 0, Actor.MaxHealth);
            if (newHealth != Actor.CurrentHealth)
            {
                Actor.RestoreHealth(newHealth);
            }
        }
    }
}

