using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;

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

