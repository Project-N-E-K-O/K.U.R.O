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

            if (Input.IsActionJustPressed("item_use"))
            {
                TryUseSelectedItem();
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

            // 從快捷欄選中的槽位獲取物品（左手物品）
            var selectedStack = InventoryComponent.GetSelectedQuickBarStack();
            if (selectedStack == null || selectedStack.IsEmpty || selectedStack.Item.ItemId == "empty_item")
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

            // 從快捷欄提取物品
            if (!InventoryComponent.TryExtractFromSelectedQuickBarSlot(selectedStack.Quantity, out var extracted) || extracted == null || extracted.IsEmpty)
            {
                return false;
            }

            var spawnPosition = ComputeSpawnPosition(disposition);
            var entity = WorldItemSpawner.SpawnFromStack(this, extracted, spawnPosition);

            if (entity == null)
            {
                // Recovery path: spawn failed, try to return extracted items to quickbar
                if (extracted == null || extracted.IsEmpty)
                {
                    return false;
                }

                int originalQuantity = extracted.Quantity;
                int totalRecovered = 0;

                // Step 1: Try to return items to the selected quickbar slot first
                if (InventoryComponent.TryReturnStackToSelectedQuickBarSlot(extracted, out var returnedToSlot))
                {
                    totalRecovered += returnedToSlot;
                }

                // Step 2: If there are remaining items, try to add them to quickbar or backpack
                if (!extracted.IsEmpty)
                {
                    int remainingQuantity = extracted.Quantity;
                    
                    // 先嘗試放回快捷欄
                    if (InventoryComponent.QuickBar != null)
                    {
                        for (int i = 1; i < 5 && remainingQuantity > 0; i++)
                        {
                            int added = InventoryComponent.QuickBar.TryAddItemToSlot(extracted.Item, remainingQuantity, i);
                            if (added > 0)
                            {
                                totalRecovered += added;
                                remainingQuantity -= added;
                                int safeRemove = Math.Min(added, extracted.Quantity);
                                if (safeRemove > 0)
                                {
                                    extracted.Remove(safeRemove);
                                }
                            }
                        }
                    }
                    
                    // 如果快捷欄也放不下，放入背包
                    if (!extracted.IsEmpty && InventoryComponent.Backpack != null)
                    {
                        int addedToBackpack = InventoryComponent.Backpack.AddItem(extracted.Item, extracted.Quantity);
                        if (addedToBackpack > 0)
                        {
                            totalRecovered += addedToBackpack;
                            int safeRemove = Math.Min(addedToBackpack, extracted.Quantity);
                            if (safeRemove > 0)
                            {
                                extracted.Remove(safeRemove);
                            }
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

            if (entity == null)
            {
                return false;
            }

            entity.LastDroppedBy = _actor;

            if (disposition == DropDisposition.Throw)
            {
                entity.ApplyThrowImpulse(GetFacingDirection() * ThrowImpulse);
            }

            InventoryComponent.NotifyItemRemoved(extracted.Item.ItemId);
            return true;
        }

        private bool TryUseSelectedItem()
        {
            if (InventoryComponent == null)
            {
                return false;
            }

            return InventoryComponent.TryConsumeSelectedItem(_actor);
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

            // 找到最近的可拾取物品（支持 WorldItemEntity 和 PickupProperty）
            Node2D? nearestPickable = null;
            float nearestDistanceSq = float.MaxValue;
            var actorPosition = _actor.GlobalPosition;

            // 检查重叠的 Area2D（WorldItemEntity 和 PickupProperty 都使用 TriggerArea）
            foreach (var areaNode in area.GetOverlappingAreas())
            {
                var parent = areaNode.GetParent();
                
                // 检查是否是 WorldItemEntity
                if (parent is WorldItemEntity entity)
                {
                    float distanceSq = actorPosition.DistanceSquaredTo(entity.GlobalPosition);
                    if (distanceSq < nearestDistanceSq)
                    {
                        nearestDistanceSq = distanceSq;
                        nearestPickable = entity;
                    }
                }
                // 检查是否是 PickupProperty
                else if (parent is PickupProperty pickup)
                {
                    float distanceSq = actorPosition.DistanceSquaredTo(pickup.GlobalPosition);
                    if (distanceSq < nearestDistanceSq)
                    {
                        nearestDistanceSq = distanceSq;
                        nearestPickable = pickup;
                    }
                }
            }

            // 只拾取最近的一個物品
            if (nearestPickable != null)
            {
                if (nearestPickable is WorldItemEntity worldItem)
                {
                    return worldItem.TryPickupByActor(_actor);
                }
                else if (nearestPickable is PickupProperty pickupProp)
                {
                    return pickupProp.TryPickupByActor(_actor);
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
