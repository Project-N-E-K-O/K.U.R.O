using Godot;
using Kuros.Items.Tags;

namespace Kuros.Systems.Inventory
{
    /// <summary>
    /// 可在 Inspector 中配置的特殊物品栏位定义。
    /// </summary>
    public partial class SpecialInventorySlotConfig : Resource
    {
        [Export] public string SlotId { get; set; } = SpecialInventorySlotIds.PrimaryWeapon;
        [Export] public string DisplayName { get; set; } = "Special Slot";
        [Export(PropertyHint.Range, "0,99,1")] public int Capacity { get; set; } = 1;

        [Export] public Godot.Collections.Array<string> AllowedTags
        {
            get => _allowedTags;
            set => _allowedTags = value ?? new();
        }

        private Godot.Collections.Array<string> _allowedTags = new();

        public static SpecialInventorySlotConfig CreateDefaultWeapon()
        {
            return new SpecialInventorySlotConfig
            {
                SlotId = SpecialInventorySlotIds.PrimaryWeapon,
                DisplayName = "Primary Weapon",
                Capacity = 1,
                AllowedTags = new Godot.Collections.Array<string> { ItemTagIds.Weapon }
            };
        }

        public static SpecialInventorySlotConfig CreateHeldItemSlot()
        {
            return new SpecialInventorySlotConfig
            {
                SlotId = SpecialInventorySlotIds.CurrentHeldItem,
                DisplayName = "Held Item",
                Capacity = 1,
                AllowedTags = new Godot.Collections.Array<string>()
            };
        }
    }
}

