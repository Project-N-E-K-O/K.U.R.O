using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家背包组件，封装背包容器并提供基础接口。
    /// </summary>
    public partial class PlayerInventoryComponent : Node
    {
        [Export(PropertyHint.Range, "1,200,1")]
        public int BackpackSlots { get; set; } = 4;

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
        public int SelectedBackpackSlot { get; private set; }
        public bool HasSelectedItem => GetSelectedBackpackStack() != null;
        public event Action<ItemDefinition>? ItemPicked;
        public event Action<string>? ItemRemoved;
        public event Action<ItemDefinition>? WeaponEquipped;
        public event Action? WeaponUnequipped;
        public event Action<int>? ActiveBackpackSlotChanged;

        public override void _Ready()
        {
            base._Ready();

            Backpack = GetNodeOrNull<InventoryContainer>("Backpack") ?? CreateBackpack();
            Backpack.SlotCount = BackpackSlots;
            Backpack.InventoryChanged += OnBackpackInventoryChanged;

            InitializeSpecialSlots();
            InitializeSelection();
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
                if (specialSlotId == SpecialInventorySlotIds.PrimaryWeapon)
                {
                    WeaponEquipped?.Invoke(extracted.Item);
                }
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
                if (specialSlotId == SpecialInventorySlotIds.PrimaryWeapon)
                {
                    WeaponUnequipped?.Invoke();
                }
                return true;
            }

            int remaining = stack.Quantity - inserted;
            if (remaining > 0)
            {
                var restoreStack = new InventoryItemStack(stack.Item, remaining);
                slot.TryAssign(restoreStack, replaceExisting: true);
            }

            NotifyItemRemoved(stack.Item.ItemId);
            if (specialSlotId == SpecialInventorySlotIds.PrimaryWeapon)
            {
                WeaponUnequipped?.Invoke();
            }
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
                return true;
            }

            return false;
        }

        public bool TryUnequipWeaponToBackpack()
        {
            return TryUnequipSpecialSlotToBackpack(SpecialInventorySlotIds.PrimaryWeapon);
        }

        public bool TryExtractFromSelectedSlot(int amount, out InventoryItemStack? extracted)
        {
            extracted = null;
            if (Backpack == null) return false;
            return Backpack.TryExtractFromSlot(SelectedBackpackSlot, amount, out extracted);
        }

        public bool TryReturnStackToSelectedSlot(InventoryItemStack? stack, out int acceptedQuantity)
        {
            acceptedQuantity = 0;
            if (Backpack == null || stack == null || stack.IsEmpty) return false;

            if (!Backpack.TryAddToSlot(SelectedBackpackSlot, stack.Item, stack.Quantity, out var accepted) || accepted <= 0)
            {
                return false;
            }

            acceptedQuantity = Math.Min(accepted, stack.Quantity);
            if (acceptedQuantity > 0)
            {
                stack.Remove(acceptedQuantity);
            }

            return true;
        }

        public int TryAddItemToSelectedSlot(ItemDefinition item, int quantity)
        {
            if (Backpack == null || item == null || quantity <= 0) return 0;

            if (Backpack.TryAddToSlot(SelectedBackpackSlot, item, quantity, out var accepted) && accepted > 0)
            {
                NotifyItemPicked(item);
                return accepted;
            }

            return 0;
        }

        public void SelectNextBackpackSlot()
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            SetSelectedBackpackSlot(SelectedBackpackSlot + 1);
        }

        public void SelectPreviousBackpackSlot()
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            SetSelectedBackpackSlot(SelectedBackpackSlot - 1);
        }

        public InventoryItemStack? GetSelectedBackpackStack()
        {
            return Backpack?.GetStack(SelectedBackpackSlot);
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

        private void InitializeSelection()
        {
            if (Backpack == null || Backpack.Slots.Count == 0)
            {
                SelectedBackpackSlot = 0;
                ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
                return;
            }

            SelectedBackpackSlot = 0;
            ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
        }

        private void SetSelectedBackpackSlot(int index)
        {
            if (Backpack == null || Backpack.Slots.Count == 0) return;
            int count = Backpack.Slots.Count;
            int normalized = ((index % count) + count) % count;
            if (normalized == SelectedBackpackSlot) return;

            SelectedBackpackSlot = normalized;
            ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
        }

        private void OnBackpackInventoryChanged()
        {
            if (Backpack == null || Backpack.Slots.Count == 0)
            {
                SelectedBackpackSlot = 0;
                ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
                return;
            }

            if (SelectedBackpackSlot >= Backpack.Slots.Count)
            {
                SelectedBackpackSlot = Backpack.Slots.Count - 1;
                ActiveBackpackSlotChanged?.Invoke(SelectedBackpackSlot);
            }
        }
    }
}

