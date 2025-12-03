using System;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.Effects
{
    /// <summary>
    /// 按固定间隔向玩家背包发放指定物品的被动效果。
    /// </summary>
    [GlobalClass]
    public partial class PeriodicItemGrantEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "1,300,1")]
        public float IntervalSeconds { get; set; } = 15f;

        [Export(PropertyHint.File, "*.tres")]
        public string ItemResourcePath { get; set; } = string.Empty;

        [Export(PropertyHint.Range, "1,99,1")]
        public int Quantity { get; set; } = 1;

        [Export]
        public bool RequireFreeSlot { get; set; } = true;

        private float _timer;
        private ItemDefinition? _itemDefinition;
        private PlayerInventoryComponent? _inventoryComponent;

        protected override void OnApply()
        {
            base.OnApply();
            Duration = 0f; // 持续生效直至被外部移除
            _timer = Math.Max(0.01f, IntervalSeconds);
            _itemDefinition = LoadItemDefinition();
            _inventoryComponent = ResolveInventoryComponent();
        }

        protected override void OnTick(double delta)
        {
            base.OnTick(delta);
            if (_inventoryComponent == null || _itemDefinition == null)
            {
                return;
            }

            _timer -= (float)delta;
            if (_timer > 0f)
            {
                return;
            }

            _timer += Math.Max(0.01f, IntervalSeconds);
            TryGrantItem();
        }

        private void TryGrantItem()
        {
            if (_inventoryComponent?.Backpack == null || _itemDefinition == null)
            {
                return;
            }

            if (RequireFreeSlot && !HasCapacity(_inventoryComponent.Backpack, _itemDefinition))
            {
                return;
            }

            int added = _inventoryComponent.Backpack.AddItem(_itemDefinition, Quantity);
            if (added <= 0)
            {
                return;
            }

            // Backpack.AddItem 会触发 InventoryChanged/SlotChanged 信号，足以驱动 UI。
        }

        private ItemDefinition? LoadItemDefinition()
        {
            if (string.IsNullOrWhiteSpace(ItemResourcePath))
            {
                return null;
            }

            return ResourceLoader.Load<ItemDefinition>(ItemResourcePath);
        }

        private PlayerInventoryComponent? ResolveInventoryComponent()
        {
            if (Actor == null)
            {
                return null;
            }

            if (Actor is SamplePlayer player && player.InventoryComponent != null)
            {
                return player.InventoryComponent;
            }

            var nodeInventory = Actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            if (nodeInventory != null)
            {
                return nodeInventory;
            }

            return FindChildComponent<PlayerInventoryComponent>(Actor);
        }

        private static T? FindChildComponent<T>(Node root) where T : Node
        {
            foreach (Node child in root.GetChildren())
            {
                if (child is T typed)
                {
                    return typed;
                }

                if (child.GetChildCount() > 0)
                {
                    var nested = FindChildComponent<T>(child);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private bool HasCapacity(InventoryContainer container, ItemDefinition item)
        {
            foreach (var slot in container.Slots)
            {
                if (slot == null)
                {
                    return true;
                }

                if (slot.Item.ItemId == item.ItemId && !slot.IsFull)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

