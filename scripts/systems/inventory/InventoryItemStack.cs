using System;
using Kuros.Items;

namespace Kuros.Systems.Inventory
{
    /// <summary>
    /// 表示背包中的一组同类物品。
    /// </summary>
    public class InventoryItemStack
    {
        public ItemDefinition Item { get; }
        public int Quantity { get; private set; }

        public bool IsFull => Quantity >= Item.MaxStackSize;
        public bool IsEmpty => Quantity <= 0;

        public InventoryItemStack(ItemDefinition item, int quantity)
        {
            Item = item;
            Quantity = Math.Max(0, quantity);
        }

        public int Add(int amount)
        {
            if (amount <= 0) return 0;

            int space = Item.MaxStackSize - Quantity;
            int added = Math.Clamp(amount, 0, space);
            Quantity += added;
            return added;
        }

        public int Remove(int amount)
        {
            if (amount <= 0) return 0;

            int removed = Math.Clamp(amount, 0, Quantity);
            Quantity -= removed;
            return removed;
        }

        public InventoryItemStack Split(int amount)
        {
            int removed = Remove(amount);
            return new InventoryItemStack(Item, removed);
        }

        public bool CanMerge(ItemDefinition other) => other == Item;
    }
}

