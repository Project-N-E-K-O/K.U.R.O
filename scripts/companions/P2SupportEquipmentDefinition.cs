using Godot;

namespace Kuros.Companions
{
    [GlobalClass]
    public partial class P2SupportEquipmentDefinition : Resource
    {
        [Export] public string EquipmentId { get; set; } = "p2_equipment_none";
        [Export] public string DisplayName { get; set; } = "无装备";
        [Export(PropertyHint.Range, "0.5,3,0.01")] public float HealPowerMultiplier { get; set; } = 1.0f;
    }
}
