using System;
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
        [Export] public Vector2 DropOffset = new Vector2(32, 0);
        [Export] public Vector2 ThrowOffset = new Vector2(48, -10);
        [Export(PropertyHint.Range, "0,2000,1")] public float ThrowImpulse = 800f;
        [Export] public bool EnableInput = true;
        [Export] public string ThrowStateName { get; set; } = "Throw";

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

            if (Input.IsActionJustPressed("item_select_right"))
            {
                InventoryComponent?.SelectNextBackpackSlot();
            }

            if (Input.IsActionJustPressed("item_select_left"))
            {
                InventoryComponent?.SelectPreviousBackpackSlot();
            }

            if (Input.IsActionJustPressed("take_up"))
            {
                TriggerPickupState();
            }
        }

        public bool TryTriggerThrowAfterAnimation()
        {
            return TryHandleDrop(DropDisposition.Throw, skipAnimation: true);
        }

        private bool TryHandleDrop(DropDisposition disposition)
        {
            return TryHandleDrop(disposition, skipAnimation: false);
        }

        private bool TryHandleDrop(DropDisposition disposition, bool skipAnimation)
        {
            if (InventoryComponent == null)
            {
                return false;
            }

            var selectedStack = InventoryComponent.GetSelectedBackpackStack();
            if (selectedStack == null)
            {
                return false;
            }

            if (!skipAnimation && disposition == DropDisposition.Throw)
            {
                if (TryTriggerThrowState())
                {
                    return false;
                }

                return TryHandleDrop(disposition, skipAnimation: true);
            }

            if (!InventoryComponent.TryExtractFromSelectedSlot(selectedStack.Quantity, out var extracted) || extracted == null || extracted.IsEmpty)
            {
                return false;
            }

            var spawnPosition = ComputeSpawnPosition(disposition);
            var entity = WorldItemSpawner.SpawnFromStack(this, extracted, spawnPosition);

            if (entity == null)
            {
                // Recovery path: spawn failed, try to return extracted items to inventory
                if (extracted == null || extracted.IsEmpty)
                {
                    return false;
                }

                int originalQuantity = extracted.Quantity;
                int totalRecovered = 0;

                // Step 1: Try to return items to the selected slot first
                // Note: TryReturnStackToSelectedSlot already removes accepted items from extracted
                if (InventoryComponent.TryReturnStackToSelectedSlot(extracted, out var returnedToSlot))
                {
                    totalRecovered += returnedToSlot;
                }

                // Step 2: If there are remaining items, try to add them to any available inventory slot
                if (!extracted.IsEmpty && InventoryComponent.Backpack != null)
                {
                    int remainingQuantity = extracted.Quantity;
                    int addedToBackpack = InventoryComponent.Backpack.AddItem(extracted.Item, remainingQuantity);

                    if (addedToBackpack > 0)
                    {
                        totalRecovered += addedToBackpack;
                        // Only remove the amount that was successfully added (with safety clamp)
                        int safeRemove = Math.Min(addedToBackpack, extracted.Quantity);
                        if (safeRemove > 0)
                        {
                            extracted.Remove(safeRemove);
                        }
                    }
                }

                // Step 3: Handle any remaining items that couldn't be recovered
                if (!extracted.IsEmpty)
                {
                    int lostQuantity = extracted.Quantity;
                    GameLogger.Error(
                        nameof(PlayerItemInteractionComponent),
                        $"[Item Recovery] Failed to recover {lostQuantity}x '{extracted.Item?.ItemId ?? "unknown"}' " +
                        $"(recovered {totalRecovered}/{originalQuantity}). Items lost due to spawn failure and full inventory.");

                    // Clear the extracted stack to maintain consistency
                    // Note: These items are lost - inventory is full
                    extracted.Remove(lostQuantity);
                }

                return false;
            }

            if (disposition == DropDisposition.Throw)
            {
                entity.ApplyThrowImpulse(GetFacingDirection() * ThrowImpulse);
            }

            InventoryComponent.NotifyItemRemoved(extracted.Item.ItemId);
            return true;
        }

        private Vector2 ComputeSpawnPosition(DropDisposition disposition)
        {
            var origin = _actor?.GlobalPosition ?? Vector2.Zero;
            var direction = GetFacingDirection();
            var offset = disposition == DropDisposition.Throw ? ThrowOffset : DropOffset;
            return origin + new Vector2(direction.X * offset.X, offset.Y);
        }

        internal bool ExecutePickupAfterAnimation() => TryHandlePickup();

        private void TriggerPickupState()
        {
            if (InventoryComponent?.HasSelectedItem == true)
            {
                return;
            }

            if (_actor?.StateMachine == null)
            {
                TryHandlePickup();
                return;
            }

            if (_actor.StateMachine.HasState("PickUp"))
            {
                _actor.StateMachine.ChangeState("PickUp");
            }
            else
            {
                GameLogger.Warn(nameof(PlayerItemInteractionComponent), "StateMachine 中未找到 'PickUp' 状态，直接执行拾取逻辑。");
                TryHandlePickup();
            }
        }

        private bool TryHandlePickup()
        {
            if (_actor == null)
            {
                return false;
            }

            if (InventoryComponent?.HasSelectedItem == true)
            {
                return false;
            }

            var area = _actor.GetNodeOrNull<Area2D>("SpineCharacter/AttackArea");
            if (area == null)
            {
                return false;
            }

            foreach (var body in area.GetOverlappingBodies())
            {
                if (body is WorldItemEntity entity && entity.TryPickupByActor(_actor))
                {
                    return true;
                }
            }

            return false;
        }

        private Vector2 GetFacingDirection()
        {
            if (_actor == null)
            {
                return Vector2.Right;
            }

            return _actor.FacingRight ? Vector2.Right : Vector2.Left;
        }

        private bool TryTriggerThrowState()
        {
            if (_actor?.StateMachine == null)
            {
                return false;
            }

            if (!_actor.StateMachine.HasState(ThrowStateName))
            {
                return false;
            }

            _actor.StateMachine.ChangeState(ThrowStateName);
            return true;
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
