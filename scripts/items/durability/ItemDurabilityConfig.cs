using Godot;

namespace Kuros.Items.Durability
{
    /// <summary>
    /// 可在 Inspector 中配置的耐久度定义。
    /// </summary>
    [GlobalClass]
    public partial class ItemDurabilityConfig : Resource
    {
        [Export(PropertyHint.Range, "1,9999,1")]
        public int MaxDurability { get; set; } = 100;

        [Export(PropertyHint.Range, "0,9999,1")]
        public int DamagePerUse { get; set; } = 1;

        [Export(PropertyHint.Range, "0,9999,1")]
        public int DamagePerHit { get; set; } = 1;

        [Export] public bool IsRepairable { get; set; } = true;

        [Export] public DurabilityBreakBehavior BreakBehavior { get; set; } = DurabilityBreakBehavior.BecomeBroken;
    }
}

