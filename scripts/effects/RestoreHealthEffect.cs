using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 立即为效果拥有者恢复指定生命值的简易治疗效果。
    /// </summary>
    [GlobalClass]
    public partial class RestoreHealthEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "1,100,1")]
        public int HealAmount { get; set; } = 3;

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null || HealAmount <= 0)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            int targetHealth = Mathf.Clamp(Actor.CurrentHealth + HealAmount, 0, Actor.MaxHealth);
            Actor.RestoreHealth(targetHealth);
            Controller?.RemoveEffect(this);
        }
    }
}

