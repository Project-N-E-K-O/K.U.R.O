using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 监听伤害事件，在拥有者命中目标后为目标回复生命值。
    /// </summary>
    [GlobalClass]
    public partial class HealAttackTargetsEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "1,50,1")]
        public int HealAmount { get; set; } = 1;

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
            if (target == null || HealAmount <= 0) return;

            int newHealth = Mathf.Clamp(target.CurrentHealth + HealAmount, 0, target.MaxHealth);
            target.RestoreHealth(newHealth);
        }
    }
}

