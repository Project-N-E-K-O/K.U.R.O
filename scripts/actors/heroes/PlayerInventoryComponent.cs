using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家背包组件，封装背包容器并提供基础接口。
    /// </summary>
    public partial class PlayerInventoryComponent : Node
    {
        [Export(PropertyHint.Range, "1,200,1")]
        public int BackpackSlots { get; set; } = 24;

        public InventoryContainer Backpack { get; private set; } = null!;

        [ExportGroup("Special Slots")]
        [Export] public Godot.Collections.Array<SpecialInventorySlotConfig> SpecialSlotConfigs
        {
            get => _specialSlotConfigs;
            set => _specialSlotConfigs = value ?? new();
        }

        private Godot.Collections.Array<SpecialInventorySlotConfig> _specialSlotConfigs = new();
        private readonly Dictionary<string, SpecialInventorySlot> _specialSlots = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, SpecialInventorySlot> SpecialSlots => _specialSlots;
        public SpecialInventorySlot? WeaponSlot => GetSpecialSlot(SpecialInventorySlotIds.PrimaryWeapon);
        public event Action<ItemDefinition>? ItemPicked;
        public event Action<string>? ItemRemoved;

        public override void _Ready()
        {
            base._Ready();

            Backpack = GetNodeOrNull<InventoryContainer>("Backpack") ?? CreateBackpack();
            Backpack.SlotCount = BackpackSlots;

            InitializeSpecialSlots();
        }

        private InventoryContainer CreateBackpack()
        {
            var container = new InventoryContainer
            {
                Name = "Backpack",
                SlotCount = BackpackSlots
            };
            AddChild(container);
            return container;
        }

        public bool TryAddItem(ItemDefinition item, int amount)
        {
            return Backpack.TryAddItem(item, amount);
        }

        public int RemoveItem(string itemId, int amount)
        {
            return Backpack.RemoveItem(itemId, amount);
        }

        public bool TryAssignSpecialSlotFromBackpack(string specialSlotId, int backpackSlotIndex, int requestedQuantity = 0)
        {
            if (!TryResolveSpecialSlot(specialSlotId, out var slot) || Backpack == null) return false;
            if (!slot.IsEmpty) return false;

            var sourceStack = Backpack.GetStack(backpackSlotIndex);
            if (sourceStack == null) return false;
            if (!slot.CanAccept(sourceStack.Item)) return false;

            int transferAmount = requestedQuantity > 0 ? Math.Min(requestedQuantity, sourceStack.Quantity) : sourceStack.Quantity;
            transferAmount = slot.ClampQuantity(transferAmount);
            if (transferAmount <= 0) return false;

            if (!Backpack.TryExtractFromSlot(backpackSlotIndex, transferAmount, out var extracted) || extracted == null)
            {
                return false;
            }

            if (slot.TryAssign(extracted))
            {
                return true;
            }

            Backpack.AddItem(extracted.Item, extracted.Quantity);
            return false;
        }

        public bool TryEquipWeaponFromBackpack(int backpackSlotIndex)
        {
            return TryAssignSpecialSlotFromBackpack(SpecialInventorySlotIds.PrimaryWeapon, backpackSlotIndex);
        }

        public bool TryUnequipSpecialSlotToBackpack(string specialSlotId)
        {
            if (!TryResolveSpecialSlot(specialSlotId, out var slot) || Backpack == null) return false;
            if (slot.IsEmpty) return false;

            var stack = slot.TakeStack();
            if (stack == null || stack.IsEmpty) return false;

            int inserted = Backpack.AddItem(stack.Item, stack.Quantity);
            if (inserted == stack.Quantity)
            {
                NotifyItemRemoved(stack.Item.ItemId);
                return true;
            }

            int remaining = stack.Quantity - inserted;
            if (remaining > 0)
            {
                var restoreStack = new InventoryItemStack(stack.Item, remaining);
                slot.TryAssign(restoreStack, replaceExisting: true);
            }

            NotifyItemRemoved(stack.Item.ItemId);
            return false;
        }

        public bool RemoveFirstItem(string itemId)
        {
            if (Backpack == null) return false;

            for (int i = 0; i < Backpack.Slots.Count; i++)
            {
                var stack = Backpack.Slots[i];
                if (stack == null || stack.Item.ItemId != itemId) continue;

                Backpack.RemoveItem(itemId, stack.Quantity);
            NotifyItemRemoved(itemId);
            NotifyItemRemoved(itemId);
                return true;
            }

            return false;
        }

        public bool TryUnequipWeaponToBackpack()
        {
            return TryUnequipSpecialSlotToBackpack(SpecialInventorySlotIds.PrimaryWeapon);
        }

        public float GetBackpackAttributeValue(string attributeId, float baseValue = 0f)
        {
            return Backpack?.GetAttributeValue(attributeId, baseValue) ?? baseValue;
        }

        public Dictionary<string, float> GetBackpackAttributeSnapshot()
        {
            return Backpack?.GetAttributeSnapshot() ?? new Dictionary<string, float>();
        }

        public SpecialInventorySlot? GetSpecialSlot(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId)) return null;
            return _specialSlots.TryGetValue(slotId, out var slot) ? slot : null;
        }

        internal void NotifyItemPicked(ItemDefinition item)
        {
            ItemPicked?.Invoke(item);
            OnItemPicked(item);
        }

        protected virtual void OnItemPicked(ItemDefinition item)
        {
        }

        internal void NotifyItemRemoved(string itemId)
        {
            ItemRemoved?.Invoke(itemId);
            OnItemRemoved(itemId);
        }

        protected virtual void OnItemRemoved(string itemId)
        {
        }

        private bool TryResolveSpecialSlot(string slotId, out SpecialInventorySlot slot)
        {
            slot = null!;
            var resolved = GetSpecialSlot(slotId);
            if (resolved == null) return false;
            slot = resolved;
            return true;
        }

        private void InitializeSpecialSlots()
        {
            _specialSlots.Clear();
            bool hasWeaponSlot = false;

            foreach (var config in _specialSlotConfigs)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.SlotId)) continue;
                var slot = new SpecialInventorySlot(config);
                _specialSlots[slot.SlotId] = slot;
                if (slot.SlotId == SpecialInventorySlotIds.PrimaryWeapon)
                {
                    hasWeaponSlot = true;
                }
            }

            if (!hasWeaponSlot)
            {
                var defaultWeapon = new SpecialInventorySlot(SpecialInventorySlotConfig.CreateDefaultWeapon());
                _specialSlots[defaultWeapon.SlotId] = defaultWeapon;
            }
        }
    }
}

