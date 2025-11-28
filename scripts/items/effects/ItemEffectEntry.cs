using Godot;
using Kuros.Core.Effects;

namespace Kuros.Items.Effects
{
    public enum ItemEffectTrigger
    {
        OnPickup = 0,
        OnEquip = 1,
        OnConsume = 2
    }

    /// <summary>
    /// 描述物品可施加的效果，支持不同触发时机。
    /// </summary>
    public partial class ItemEffectEntry : Resource
    {
        [Export] public ItemEffectTrigger Trigger { get; set; } = ItemEffectTrigger.OnPickup;
        [Export] public PackedScene? EffectScene { get; set; }

        public ActorEffect? InstantiateEffect()
        {
            if (EffectScene == null)
            {
                return null;
            }

            return EffectScene.Instantiate<ActorEffect>();
        }
    }
}

