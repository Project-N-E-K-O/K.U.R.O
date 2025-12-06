using System;
using System.Collections.Generic;
using Godot;
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
        private readonly Dictionary<string, float> _runtimeAttributeAdditions = new(StringComparer.OrdinalIgnoreCase);

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
            // Runtime 属性视为当前堆叠的“单体武器强化”状态，不在分堆时复制，
            // 以避免一份强化被重复克隆到多个堆叠。若未来需要更复杂的拆分逻辑，可在此处细化。
            return newStack;
        }

        public bool CanMerge(ItemDefinition other) => other == Item;

        public bool TryGetAttribute(string attributeId, out ResolvedItemAttribute attribute)
        {
            attribute = ResolvedItemAttribute.Empty;
            if (string.IsNullOrWhiteSpace(attributeId))
            {
                return false;
            }

            bool hasBase = Item.TryResolveAttribute(attributeId, Quantity, out var baseAttribute) && baseAttribute.IsValid;
            float runtimeBonus = GetRuntimeAttributeValue(attributeId, 0f);

            bool hasRuntime = !Mathf.IsZeroApprox(runtimeBonus);

            if (!hasBase && !hasRuntime)
            {
                return false;
            }

            if (!hasBase && hasRuntime)
            {
                attribute = new ResolvedItemAttribute(attributeId, runtimeBonus, ItemAttributeOperation.Add);
                return true;
            }

            if (hasBase && !hasRuntime)
            {
                attribute = baseAttribute;
                return true;
            }

            float combinedValue = baseAttribute.Value + runtimeBonus;
            attribute = new ResolvedItemAttribute(attributeId, combinedValue, baseAttribute.Operation);
            return true;
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

            foreach (var pair in _runtimeAttributeAdditions)
            {
                if (Mathf.IsZeroApprox(pair.Value)) continue;
                yield return new ResolvedItemAttribute(pair.Key, pair.Value, ItemAttributeOperation.Add);
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

        public float GetRuntimeAttributeValue(string attributeId, float defaultValue = 0f)
        {
            if (string.IsNullOrWhiteSpace(attributeId)) return defaultValue;
            return _runtimeAttributeAdditions.TryGetValue(attributeId, out var value) ? value : defaultValue;
        }

        public float AddRuntimeAttributeValue(string attributeId, float delta)
        {
            if (string.IsNullOrWhiteSpace(attributeId) || Mathf.IsZeroApprox(delta))
            {
                return GetRuntimeAttributeValue(attributeId, 0f);
            }

            float updated = GetRuntimeAttributeValue(attributeId, 0f) + delta;
            SetRuntimeAttributeValue(attributeId, updated);
            return updated;
        }

        public float SetRuntimeAttributeValue(string attributeId, float value)
        {
            if (string.IsNullOrWhiteSpace(attributeId))
            {
                return 0f;
            }

            if (Mathf.IsZeroApprox(value))
            {
                _runtimeAttributeAdditions.Remove(attributeId);
                return 0f;
            }

            _runtimeAttributeAdditions[attributeId] = value;
            return value;
        }

        public void ClearRuntimeAttribute(string attributeId)
        {
            if (string.IsNullOrWhiteSpace(attributeId)) return;
            _runtimeAttributeAdditions.Remove(attributeId);
        }

        public void ClearAllRuntimeAttributes()
        {
            _runtimeAttributeAdditions.Clear();
        }

    }
}

