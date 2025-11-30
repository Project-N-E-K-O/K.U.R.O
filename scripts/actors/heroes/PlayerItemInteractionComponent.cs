using Godot;
using Kuros.Core;
using Kuros.Items.World;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 负责处理玩家与背包物品之间的放置/投掷交互。
    /// </summary>
    public partial class PlayerItemInteractionComponent : Node
    {
        private enum DropDisposition
        {
            Place,
            Throw
        }

        [Export] public PlayerInventoryComponent? InventoryComponent { get; private set; }
        [Export(PropertyHint.Range, "0,199,1")] public int ActiveBackpackSlotIndex { get; private set; }
        [Export(PropertyHint.Range, "0,999,1")] public int DropAmountPerAction { get; set; } = 0;
        [Export] public Vector2 DropOffset = new Vector2(32, 0);
        [Export] public Vector2 ThrowOffset = new Vector2(48, -10);
        [Export(PropertyHint.Range, "0,2000,1")] public float ThrowImpulse = 800f;
        [Export] public bool EnableInput = true;

        private GameActor? _actor;

        public override void _Ready()
        {
            base._Ready();

            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            InventoryComponent ??= GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            InventoryComponent ??= FindChildComponent<PlayerInventoryComponent>(GetParent());

            if (InventoryComponent == null)
            {
                GameLogger.Error(nameof(PlayerItemInteractionComponent), $"{Name} 未能找到 PlayerInventoryComponent。");
            }

            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (!EnableInput || InventoryComponent?.Backpack == null)
            {
                return;
            }

            if (Input.IsActionJustPressed("put_down"))
            {
                TryHandleDrop(DropDisposition.Place);
            }

            if (Input.IsActionJustPressed("throw"))
            {
                TryHandleDrop(DropDisposition.Throw);
            }
        }

        public void SetActiveSlot(int slotIndex)
        {
            ActiveBackpackSlotIndex = Mathf.Clamp(slotIndex, 0, InventoryComponent?.Backpack?.Slots.Count - 1 ?? slotIndex);
        }

        private bool TryHandleDrop(DropDisposition disposition)
        {
            var backpack = InventoryComponent?.Backpack;
            if (backpack == null)
            {
                return false;
            }

            var stack = backpack.GetStack(ActiveBackpackSlotIndex);
            if (stack == null)
            {
                GameLogger.Info(nameof(PlayerItemInteractionComponent), $"背包槽 {ActiveBackpackSlotIndex} 没有物品可丢弃。");
                return false;
            }

            int requestedAmount = DropAmountPerAction <= 0 ? stack.Quantity : Mathf.Min(DropAmountPerAction, stack.Quantity);

            if (!backpack.TryExtractFromSlot(ActiveBackpackSlotIndex, requestedAmount, out var extracted) ||
                extracted == null || extracted.IsEmpty)
            {
                return false;
            }

            var spawnPosition = ComputeSpawnPosition(disposition);
            var entity = WorldItemSpawner.SpawnFromStack(this, extracted, spawnPosition);

            if (entity == null)
            {
                int restored = backpack.AddItem(extracted.Item, extracted.Quantity);
                if (restored < extracted.Quantity)
                {
                    GameLogger.Error(nameof(PlayerItemInteractionComponent), $"尝试恢复 {extracted.Item.ItemId} 失败，剩余 {extracted.Quantity - restored} 个物品无法找回。");
                }
                return false;
            }

            InventoryComponent?.NotifyItemRemoved(extracted.Item.ItemId);

            if (disposition == DropDisposition.Throw)
            {
                entity.ApplyThrowImpulse(GetFacingDirection() * ThrowImpulse);
            }

            return true;
        }

        private Vector2 ComputeSpawnPosition(DropDisposition disposition)
        {
            var origin = _actor?.GlobalPosition ?? Vector2.Zero;
            var direction = GetFacingDirection();
            var offset = disposition == DropDisposition.Throw ? ThrowOffset : DropOffset;
            return origin + new Vector2(direction.X * offset.X, offset.Y);
        }

        private Vector2 GetFacingDirection()
        {
            if (_actor == null)
            {
                return Vector2.Right;
            }

            return _actor.FacingRight ? Vector2.Right : Vector2.Left;
        }

        private static T? FindChildComponent<T>(Node? root) where T : Node
        {
            if (root == null)
            {
                return null;
            }

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
    }
}

