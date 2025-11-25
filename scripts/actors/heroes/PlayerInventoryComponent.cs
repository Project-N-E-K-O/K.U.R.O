using Godot;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家背包组件，封装背包容器并提供基础接口。
    /// </summary>
    public partial class PlayerInventoryComponent : Node
    {
        [Export(PropertyHint.Range, "1,200,1")]
        public int BackpackSlots { get; set; } = 24;

        public InventoryContainer Backpack { get; private set; } = null!;

        public override void _Ready()
        {
            base._Ready();

            Backpack = GetNodeOrNull<InventoryContainer>("Backpack") ?? CreateBackpack();
            Backpack.SlotCount = BackpackSlots;
        }

        private InventoryContainer CreateBackpack()
        {
            var container = new InventoryContainer
            {
                Name = "Backpack",
                SlotCount = BackpackSlots
            };
            AddChild(container);
            return container;
        }

        public bool TryAddItem(ItemDefinition item, int amount)
        {
            return Backpack.TryAddItem(item, amount);
        }

        public int RemoveItem(string itemId, int amount)
        {
            return Backpack.RemoveItem(itemId, amount);
        }
    }
}

