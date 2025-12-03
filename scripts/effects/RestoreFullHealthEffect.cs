using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 立即将效果拥有者生命值回复至最大值的治疗效果。
    /// </summary>
    [GlobalClass]
    public partial class RestoreFullHealthEffect : ActorEffect
    {
        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            Actor.RestoreHealth(Actor.MaxHealth);
            Controller?.RemoveEffect(this);
        }
    }
}

