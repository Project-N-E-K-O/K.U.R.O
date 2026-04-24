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
        /// <summary>
        /// 纯物理弹出（不触发投掷状态机、不生成 OnThrowDestroy 效果），用于战利品掉落散开等场景。
        /// </summary>
        void ApplyScatterImpulse(Vector2 velocity);
        GameActor? LastDroppedBy { get; set; }
    }
}
