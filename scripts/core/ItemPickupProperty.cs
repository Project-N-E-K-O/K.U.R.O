using Godot;
using Kuros.Items;
using Kuros.Actors.Heroes;

namespace Kuros.Core
{
	/// <summary>
	/// 物品拾取属性 - 拾取物品时自动添加到玩家物品栏
	/// 优先放入快捷栏2345，剩余放入物品栏
	/// </summary>
	public partial class ItemPickupProperty : PickupProperty
	{
		[ExportGroup("Item")]
		[Export] public ItemDefinition? Item { get; set; }
		[Export(PropertyHint.Range, "1,9999,1")]
		public int Quantity { get; set; } = 1;

		protected override void OnPicked(GameActor actor)
		{
			base.OnPicked(actor);

			// 如果是玩家，尝试添加到物品栏
			if (actor is SamplePlayer player && Item != null)
			{
				GD.Print($"ItemPickupProperty: Player {player.Name} picked up {Quantity} x {Item.DisplayName}");
				
				if (player.InventoryComponent != null)
				{
					GD.Print($"ItemPickupProperty: InventoryComponent found, QuickBar is {(player.InventoryComponent.QuickBar != null ? "set" : "null")}");
					
					int added = player.InventoryComponent.AddItemSmart(Item, Quantity);
					if (added > 0)
					{
						GD.Print($"ItemPickupProperty: Successfully added {added} x {Item.DisplayName} to inventory");
					}
					else
					{
						GD.PrintErr($"ItemPickupProperty: Failed to add {Item.DisplayName} to inventory - inventory may be full");
					}
				}
				else
				{
					GD.PrintErr($"ItemPickupProperty: Player {player.Name} has no InventoryComponent");
				}
			}
			else
			{
				GD.PrintErr($"ItemPickupProperty: Actor is not SamplePlayer or Item is null. Actor: {actor?.GetType().Name}, Item: {Item?.DisplayName}");
			}
		}
	}
}

