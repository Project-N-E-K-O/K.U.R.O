using Godot;
using Kuros.Core;
using Kuros.Systems.Inventory;

namespace Kuros.Items.World
{
    public interface IWorldItemEntity
    {
        Vector2 GlobalPosition { get; set; }
        void InitializeFromStack(InventoryItemStack stack);
        void InitializeFromItem(ItemDefinition definition, int quantity);
        void ApplyThrowImpulse(Vector2 velocity);
        GameActor? LastDroppedBy { get; set; }
    }
}
