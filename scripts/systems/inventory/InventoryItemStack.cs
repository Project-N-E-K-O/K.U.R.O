using System;
using System.Collections.Generic;
using Kuros.Items;
using Kuros.Items.Attributes;

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

        public bool TryGetAttribute(string attributeId, out ResolvedItemAttribute attribute)
        {
            if (Item.TryResolveAttribute(attributeId, Quantity, out attribute))
            {
                return attribute.IsValid;
            }

            attribute = ResolvedItemAttribute.Empty;
            return false;
        }

        public float GetAttributeValue(string attributeId, float defaultValue = 0f)
        {
            return TryGetAttribute(attributeId, out var attribute) ? attribute.Value : defaultValue;
        }

        public IEnumerable<ResolvedItemAttribute> GetAllAttributes()
        {
            foreach (var attributeValue in Item.GetAttributeValues())
            {
                var resolved = attributeValue.Resolve(Quantity);
                if (resolved.IsValid)
                {
                    yield return resolved;
                }
            }
        }

        public bool HasTag(string tagId) => Item.HasTag(tagId);

        public bool HasAnyTag(IEnumerable<string> tagIds) => Item.HasAnyTag(tagIds);

        public IReadOnlyCollection<string> GetTags() => Item.GetTags();
    }
}

