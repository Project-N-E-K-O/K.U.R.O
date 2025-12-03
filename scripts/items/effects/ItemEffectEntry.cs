using System;
using Godot;
using Kuros.Core.Effects;

namespace Kuros.Items.Effects
{
    public enum ItemEffectTrigger
    {
        OnPickup = 0,
        OnEquip = 1,
        OnConsume = 2,
        OnBreak = 3
    }

    /// <summary>
    /// 描述物品可施加的效果，支持不同触发时机。
    /// </summary>
    public partial class ItemEffectEntry : Resource
    {
        [Export] public ItemEffectTrigger Trigger { get; set; } = ItemEffectTrigger.OnPickup;
        [Export] public PackedScene? EffectScene { get; set; }
        [Export(PropertyHint.MultilineText)] public string Notes { get; set; } = string.Empty;
        [Export] public Godot.Collections.Dictionary<string, Variant> PropertyOverrides { get; set; } = new();

        public ActorEffect? InstantiateEffect()
        {
            if (EffectScene == null)
            {
                return null;
            }

            var effect = EffectScene.Instantiate<ActorEffect>();
            ApplyOverrides(effect);
            return effect;
        }

        private void ApplyOverrides(ActorEffect effect)
        {
            if (effect == null || PropertyOverrides == null || PropertyOverrides.Count == 0)
            {
                return;
            }

            foreach (var pair in PropertyOverrides)
            {
                if (pair.Key == null) continue;
                try
                {
                    effect.Set(pair.Key, pair.Value);
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[ItemEffectEntry] Failed to override property '{pair.Key}' on effect '{effect.Name}': {ex.Message}");
                }
            }
        }
    }
}

