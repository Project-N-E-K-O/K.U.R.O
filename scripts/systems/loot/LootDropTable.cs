using System;
using Godot;

namespace Kuros.Systems.Loot
{
    /// <summary>
    /// 可复用的敌人战利品表，描述掉落概率、数量以及散落表现。
    /// </summary>
    [GlobalClass]
    public partial class LootDropTable : Resource
    {
        [Export(PropertyHint.Range, "0,1,0.01")]
        public float GlobalDropChance { get; set; } = 1f;

        [Export(PropertyHint.Range, "0,32,1")]
        public int MaxDrops { get; set; } = 0; // 0 表示不限

        [Export(PropertyHint.Range, "0,256,1")]
        public float ScatterRadius { get; set; } = 24f;

        [Export(PropertyHint.Range, "0,4096,1")]
        public float DefaultImpulse { get; set; } = 0f;

        [Export] public Vector2 SpawnOffset { get; set; } = Vector2.Zero;

        [Export] public LootDropEntry[] Entries { get; set; } = Array.Empty<LootDropEntry>();

        public bool ShouldRoll(RandomNumberGenerator rng)
        {
            if (Entries == null || Entries.Length == 0)
            {
                return false;
            }

            float chance = Mathf.Clamp(GlobalDropChance, 0f, 1f);
            return rng.Randf() <= chance;
        }
    }
}

