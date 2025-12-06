using System;
using Godot;
using Kuros.Items;

namespace Kuros.Systems.Loot
{
    /// <summary>
    /// 单条战利品掉落配置，可定义物品、概率、数量范围以及额外的抛洒表现。
    /// </summary>
    [GlobalClass]
    public partial class LootDropEntry : Resource
    {
        [ExportGroup("Item")]
        [Export] public ItemDefinition? Item { get; set; }

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float DropChance { get; set; } = 1f;

        [Export(PropertyHint.Range, "1,9999,1")]
        public int MinQuantity { get; set; } = 1;

        [Export(PropertyHint.Range, "1,9999,1")]
        public int MaxQuantity { get; set; } = 1;

        [Export(PropertyHint.Range, "1,8,1")]
        public int MaxStacks { get; set; } = 1;

        [ExportGroup("Presentation")]
        [Export] public Vector2 PositionOffset { get; set; } = Vector2.Zero;

        [Export(PropertyHint.Range, "0,4096,1")]
        public float ImpulseStrength { get; set; } = 0f;

        [Export(PropertyHint.Range, "0,360,1")]
        public float ImpulseSpreadDegrees { get; set; } = 360f;

        public bool IsValid =>
            Item != null &&
            MinQuantity > 0 &&
            MaxQuantity >= MinQuantity &&
            MaxStacks > 0;

        public bool ShouldDrop(RandomNumberGenerator rng)
        {
            if (!IsValid) return false;
            float chance = Mathf.Clamp(DropChance, 0f, 1f);
            return rng.Randf() <= chance;
        }

        public int RollQuantity(RandomNumberGenerator rng)
        {
            if (!IsValid) return 0;
            if (MinQuantity == MaxQuantity) return MinQuantity;
            return rng.RandiRange(MinQuantity, MaxQuantity);
        }

        public int RollStackCount(RandomNumberGenerator rng)
        {
            if (!IsValid) return 0;
            if (MaxStacks <= 1) return 1;
            return rng.RandiRange(1, Math.Max(1, MaxStacks));
        }
    }
}

