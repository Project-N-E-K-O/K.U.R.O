using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.Attributes;
using Kuros.Items.Durability;
using Kuros.Items.Effects;

namespace Kuros.Systems.Inventory
{
    /// <summary>
    /// 表示背包中的一组同类物品。
    /// </summary>
    public class InventoryItemStack
    {
        public ItemDefinition Item { get; }
        public int Quantity { get; private set; }
        public ItemDurabilityState? DurabilityState { get; }

        public bool IsFull => Quantity >= Item.MaxStackSize;
        public bool IsEmpty => Quantity <= 0;

        public InventoryItemStack(ItemDefinition item, int quantity)
        {
            Item = item;
            Quantity = Math.Max(0, quantity);
            if (item.DurabilityConfig != null)
            {
                DurabilityState = new ItemDurabilityState(item.DurabilityConfig);
            }
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
            var newStack = new InventoryItemStack(Item, removed);
            if (DurabilityState != null && newStack.DurabilityState != null)
            {
                int durabilityPerUnit = DurabilityState.CurrentDurability;
                newStack.DurabilityState.Reset();
                newStack.DurabilityState.ApplyDamage(DurabilityState.Config.MaxDurability - durabilityPerUnit);
            }
            return newStack;
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

        public bool HasDurability => DurabilityState != null;

        public bool ApplyDurabilityDamage(int amount, GameActor? owner = null, bool triggerEffects = true)
        {
            if (DurabilityState == null) return false;
            bool broke = DurabilityState.ApplyDamage(amount);
            if (broke && triggerEffects && owner != null)
            {
                Item.ApplyEffects(owner, ItemEffectTrigger.OnBreak);
            }
            return broke;
        }

        public void RepairDurability(int amount)
        {
            DurabilityState?.Repair(amount);
        }

    }
}

