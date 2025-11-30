using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;
using Kuros.Items.Attributes;

namespace Kuros.Systems.Inventory
{
    /// <summary>
    /// 通用背包/容器实现，支持栈叠、转移与信号通知。
    /// </summary>
    public partial class InventoryContainer : Node
    {
        [Export(PropertyHint.Range, "1,200,1")]
        public int SlotCount { get; set; } = 20;

        [Signal] public delegate void InventoryChangedEventHandler();
        [Signal] public delegate void SlotChangedEventHandler(int slotIndex, string itemId, int quantity);

        private readonly List<InventoryItemStack?> _slots = new();

        public override void _Ready()
        {
            base._Ready();
            EnsureCapacity();
        }

        public IReadOnlyList<InventoryItemStack?> Slots => _slots;

        public InventoryItemStack? GetStack(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            return _slots[slotIndex];
        }

        public bool TryExtractFromSlot(int slotIndex, int amount, out InventoryItemStack? extracted)
        {
            extracted = null;
            if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
            var stack = _slots[slotIndex];
            if (stack == null || amount <= 0) return false;

            int clamped = Math.Min(amount, stack.Quantity);
            if (clamped <= 0) return false;

            var split = stack.Split(clamped);
            if (split.Quantity <= 0) return false;

            extracted = split;

            if (stack.IsEmpty)
            {
                _slots[slotIndex] = null;
                EmitSignal(SignalName.SlotChanged, slotIndex, string.Empty, 0);
            }
            else
            {
                EmitSignal(SignalName.SlotChanged, slotIndex, stack.Item.ItemId, stack.Quantity);
            }

            EmitSignal(SignalName.InventoryChanged);
            return true;
        }

        public float GetAttributeValue(string attributeId, float baseValue = 0f)
        {
            if (string.IsNullOrWhiteSpace(attributeId)) return baseValue;

            var accumulator = new ItemAttributeAccumulator();
            foreach (var stack in _slots)
            {
                if (stack == null) continue;
                if (!stack.TryGetAttribute(attributeId, out var attribute)) continue;
                accumulator.Accumulate(attribute);
            }

            return accumulator.HasContribution ? accumulator.Resolve(baseValue) : baseValue;
        }

        public Dictionary<string, float> GetAttributeSnapshot()
        {
            var accumulators = new Dictionary<string, ItemAttributeAccumulator>(StringComparer.Ordinal);

            foreach (var stack in _slots)
            {
                if (stack == null) continue;

                foreach (var attribute in stack.GetAllAttributes())
                {
                    if (!attribute.IsValid) continue;

                    if (!accumulators.TryGetValue(attribute.AttributeId, out var accumulator))
                    {
                        accumulator = new ItemAttributeAccumulator();
                        accumulators[attribute.AttributeId] = accumulator;
                    }

                    accumulator.Accumulate(attribute);
                }
            }

            var result = new Dictionary<string, float>(accumulators.Count, StringComparer.Ordinal);
            foreach (var pair in accumulators)
            {
                result[pair.Key] = pair.Value.Resolve();
            }

            return result;
        }

        public IEnumerable<InventoryItemStack> GetStacksWithTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId)) yield break;

            foreach (var stack in _slots)
            {
                if (stack == null) continue;
                if (stack.HasTag(tagId))
                {
                    yield return stack;
                }
            }
        }

        public int CountItemsWithTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId)) return 0;

            int total = 0;
            foreach (var stack in _slots)
            {
                if (stack == null) continue;
                if (!stack.HasTag(tagId)) continue;
                total += stack.Quantity;
            }

            return total;
        }

        public bool TryFindFirstStackWithTag(string tagId, out InventoryItemStack? stack)
        {
            if (string.IsNullOrWhiteSpace(tagId))
            {
                stack = null;
                return false;
            }

            foreach (var candidate in _slots)
            {
                if (candidate == null) continue;
                if (!candidate.HasTag(tagId)) continue;
                stack = candidate;
                return true;
            }

            stack = null;
            return false;
        }

        public bool TryAddItem(ItemDefinition item, int amount)
        {
            int remaining = AddInternal(item, amount);
            return remaining == 0;
        }

        public int AddItem(ItemDefinition item, int amount)
        {
            int remaining = AddInternal(item, amount);
            return amount - remaining;
        }

        public int RemoveItem(string itemId, int amount)
        {
            if (amount <= 0) return 0;

            int removed = 0;
            for (int i = 0; i < _slots.Count && removed < amount; i++)
            {
                var stack = _slots[i];
                if (stack == null || stack.Item.ItemId != itemId) continue;

                int take = stack.Remove(amount - removed);
                removed += take;
                if (stack.IsEmpty)
                {
                    _slots[i] = null;
                    EmitSignal(SignalName.SlotChanged, i, string.Empty, 0);
                }
                else
                {
                    EmitSignal(SignalName.SlotChanged, i, stack.Item.ItemId, stack.Quantity);
                }
            }

            if (removed > 0) EmitSignal(SignalName.InventoryChanged);
            return removed;
        }

        public bool MoveTo(InventoryContainer target, int slotIndex, int amount)
        {
            if (target == null || slotIndex < 0 || slotIndex >= _slots.Count) return false;
            var stack = _slots[slotIndex];
            if (stack == null || amount <= 0) return false;

            var split = stack.Split(amount);
            if (split.Quantity <= 0) return false;

            int remaining = target.AddInternal(split.Item, split.Quantity);
            int transferred = split.Quantity - remaining;

            if (transferred <= 0)
            {
                // rollback
                stack.Add(split.Quantity);
                return false;
            }

            if (stack.IsEmpty)
            {
                _slots[slotIndex] = null;
            }

            if (remaining > 0)
            {
                stack.Add(remaining);
            }

            var updated = _slots[slotIndex];
            EmitSignal(SignalName.SlotChanged, slotIndex, updated?.Item.ItemId ?? string.Empty, updated?.Quantity ?? 0);
            EmitSignal(SignalName.InventoryChanged);
            target.EmitSignal(SignalName.InventoryChanged);
            return true;
        }

        public bool TryAddToSlot(int slotIndex, ItemDefinition item, int amount, out int accepted)
        {
            accepted = 0;
            if (item == null || amount <= 0)
            {
                return false;
            }

            EnsureCapacity();
            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                return false;
            }

            var stack = _slots[slotIndex];
            if (stack == null)
            {
                int toAdd = Math.Min(amount, item.MaxStackSize);
                var newStack = new InventoryItemStack(item, toAdd);
                _slots[slotIndex] = newStack;
                accepted = toAdd;
                EmitSignal(SignalName.SlotChanged, slotIndex, item.ItemId, newStack.Quantity);
                EmitSignal(SignalName.InventoryChanged);
                return accepted > 0;
            }

            if (stack.Item != item || stack.IsFull)
            {
                return false;
            }

            int added = stack.Add(amount);
            if (added <= 0)
            {
                return false;
            }

            accepted = added;
            EmitSignal(SignalName.SlotChanged, slotIndex, stack.Item.ItemId, stack.Quantity);
            EmitSignal(SignalName.InventoryChanged);
            return true;
        }

        private int AddInternal(ItemDefinition item, int amount)
        {
            EnsureCapacity();
            int remaining = Math.Max(0, amount);

            // fill partial stacks
            for (int i = 0; i < _slots.Count && remaining > 0; i++)
            {
                var stack = _slots[i];
                if (stack == null || stack.Item != item || stack.IsFull) continue;

                int added = stack.Add(remaining);
                remaining -= added;
                EmitSignal(SignalName.SlotChanged, i, stack.Item.ItemId, stack.Quantity);
            }

            // fill empty slots
            for (int i = 0; i < _slots.Count && remaining > 0; i++)
            {
                if (_slots[i] != null) continue;
                var stack = new InventoryItemStack(item, 0);
                int added = stack.Add(remaining);
                remaining -= added;
                _slots[i] = stack;
                EmitSignal(SignalName.SlotChanged, i, stack.Item.ItemId, stack.Quantity);
            }

            if (amount != remaining)
            {
                EmitSignal(SignalName.InventoryChanged);
            }

            return remaining;
        }

        private void EnsureCapacity()
        {
            if (_slots.Count == SlotCount) return;

            if (_slots.Count < SlotCount)
            {
                while (_slots.Count < SlotCount)
                {
                    _slots.Add(null);
                }
            }
            else
            {
                _slots.RemoveRange(SlotCount, _slots.Count - SlotCount);
            }
        }
    }
}

