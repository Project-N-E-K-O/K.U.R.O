using System;
using System.Collections.Generic;
using Kuros.Items;

namespace Kuros.Systems.Inventory
{
    /// <summary>
    /// 具备标签约束与容量限制的特殊物品栏位。
    /// </summary>
    public class SpecialInventorySlot
    {
        private readonly HashSet<string> _allowedTags;
        private InventoryItemStack? _stack;

        public string SlotId { get; }
        public string DisplayName { get; }
        public int Capacity { get; }
        public bool HasCapacityLimit => Capacity > 0;
        public bool IsEmpty => _stack == null || _stack.IsEmpty;
        public InventoryItemStack? Stack => _stack;
        public IReadOnlyCollection<string> AllowedTags => _allowedTags;

        public event Action<SpecialInventorySlot>? Changed;

        public SpecialInventorySlot(SpecialInventorySlotConfig config)
            : this(config?.SlotId ?? string.Empty,
                   config?.DisplayName ?? string.Empty,
                   config?.AllowedTags,
                   config?.Capacity ?? 1)
        {
        }

        public SpecialInventorySlot(string slotId, string displayName, IEnumerable<string>? allowedTags, int capacity)
        {
            SlotId = string.IsNullOrWhiteSpace(slotId) ? Guid.NewGuid().ToString("N") : slotId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? SlotId : displayName;
            Capacity = capacity < 0 ? 0 : capacity;
            _allowedTags = BuildTagSet(allowedTags);
        }

        public bool CanAccept(ItemDefinition item)
        {
            if (item == null) return false;
            if (_allowedTags.Count == 0) return true;
            return item.HasAnyTag(_allowedTags);
        }

        public int ClampQuantity(int quantity)
        {
            int safeQuantity = Math.Max(0, quantity);
            if (!HasCapacityLimit) return safeQuantity;
            return Math.Min(safeQuantity, Capacity);
        }

        public bool TryAssign(InventoryItemStack stack, bool replaceExisting = false)
        {
            if (stack == null || stack.IsEmpty) return false;
            if (!replaceExisting && !IsEmpty) return false;
            if (!CanAccept(stack.Item)) return false;

            if (HasCapacityLimit && stack.Quantity > Capacity)
            {
                return false;
            }

            _stack = stack;
            RaiseChanged();
            return true;
        }

        public InventoryItemStack? TakeStack()
        {
            var stack = _stack;
            if (stack == null) return null;

            _stack = null;
            RaiseChanged();
            return stack;
        }

        public void Clear()
        {
            if (_stack == null) return;
            _stack = null;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke(this);
        }

        private static HashSet<string> BuildTagSet(IEnumerable<string>? tags)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tags == null) return set;

            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                set.Add(tag.Trim());
            }

            return set;
        }
    }
}

