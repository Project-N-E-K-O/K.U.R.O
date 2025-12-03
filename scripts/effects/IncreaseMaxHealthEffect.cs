using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 立即提升角色最大生命值，可选地恢复对应生命。
    /// </summary>
    [GlobalClass]
    public partial class IncreaseMaxHealthEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "1,50,1")]
        public int MaxHealthIncrease { get; set; } = 1;

        [Export(PropertyHint.Range, "0,50,1")]
        public int HealAmount { get; set; } = 1;

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null || MaxHealthIncrease <= 0)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            Actor.MaxHealth += MaxHealthIncrease;
            int targetHealth = Mathf.Clamp(Actor.CurrentHealth + HealAmount, 0, Actor.MaxHealth);
            Actor.RestoreHealth(targetHealth);

            Controller?.RemoveEffect(this);
        }
    }
}

