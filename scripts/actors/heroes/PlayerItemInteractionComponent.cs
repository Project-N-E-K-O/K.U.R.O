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
        [Export] public NodePath? InteractionAreaPath { get; set; }
        [Export(PropertyHint.Range, "50,500,10")] public float PickupRange = 150f; // 拾取范围（像素）

        private GameActor? _actor;
        private Area2D? _interactionArea;

        public override void _Ready()
        {
            base._Ready();

            // 获取 Actor 引用（优先使用父节点，然后是 Owner）
            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            
            // 如果还是 null，尝试从父节点的父节点获取（处理嵌套结构）
            if (_actor == null && GetParent() != null)
            {
                var parent = GetParent();
                _actor = parent.GetParent() as GameActor;
            }
            
            // 如果还是 null，尝试通过场景树查找
            if (_actor == null)
            {
                var player = GetTree().GetFirstNodeInGroup("player") as GameActor;
                if (player != null)
                {
                    _actor = player;
                    GD.Print($"[{Name}] 通过场景树查找找到 Actor: {_actor.Name}");
                }
            }

            if (_actor == null)
            {
                GameLogger.Error(nameof(PlayerItemInteractionComponent), $"{Name} 未能找到 GameActor（父节点: {GetParent()?.Name ?? "null"}, Owner: {GetOwner()?.Name ?? "null"}）。");
            }
            else
            {
                GD.Print($"[{Name}] Actor 初始化成功: {_actor.Name}");
            }

            // 查找 InventoryComponent（优先使用 Export 属性，然后是节点查找）
            if (InventoryComponent == null)
            {
                InventoryComponent = GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            }
            
            if (InventoryComponent == null && _actor != null)
            {
                InventoryComponent = _actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            }
            
            if (InventoryComponent == null)
            {
                InventoryComponent = FindChildComponent<PlayerInventoryComponent>(GetParent());
            }

            if (InventoryComponent == null)
            {
                GameLogger.Error(nameof(PlayerItemInteractionComponent), $"{Name} 未能找到 PlayerInventoryComponent。");
            }
            else
            {
                GD.Print($"[{Name}] InventoryComponent 初始化成功: {InventoryComponent.Name}");
            }

            // 尝试解析互动区域
            ResolveInteractionArea();

            SetProcess(true);
        }
        
        private void ResolveInteractionArea()
        {
            // 优先使用指定的路径
            if (InteractionAreaPath != null && !InteractionAreaPath.IsEmpty)
            {
                _interactionArea = GetNodeOrNull<Area2D>(InteractionAreaPath);
            }
            
            // 尝试常见的路径
            if (_interactionArea == null && _actor != null)
            {
                _interactionArea = _actor.GetNodeOrNull<Area2D>("SpineCharacter/AttackArea");
            }
            
            if (_interactionArea == null && _actor != null)
            {
                _interactionArea = _actor.GetNodeOrNull<Area2D>("AttackArea");
            }
            
            if (_interactionArea == null && _actor != null)
            {
                // 尝试查找任何名为 AttackArea 的子节点
                _interactionArea = _actor.FindChild("AttackArea", recursive: true) as Area2D;
            }
            
            if (_interactionArea == null)
            {
                GameLogger.Warn(nameof(PlayerItemInteractionComponent), 
                    $"{Name}: 未找到 InteractionArea，将使用距离检测模式。拾取范围: {PickupRange} 像素");
            }
            else
            {
                GameLogger.Info(nameof(PlayerItemInteractionComponent), 
                    $"{Name}: InteractionArea 已解析: {_interactionArea.GetPath()}");
            }
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
                GD.Print($"[PlayerItemInteractionComponent] take_up 按键被按下");
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
            GD.Print($"[PlayerItemInteractionComponent] TryHandlePickup 被调用");
            
            if (_actor == null)
            {
                GD.PrintErr("[PlayerItemInteractionComponent] _actor 为 null");
                return false;
            }

            if (InventoryComponent?.HasSelectedItem == true)
            {
                GD.Print($"[PlayerItemInteractionComponent] 玩家已有选中物品，无法拾取");
                return false;
            }

            var actorPosition = _actor.GlobalPosition;
            Node2D? nearestPickable = null;
            float nearestDistanceSq = float.MaxValue;

            // 方法1: 通过 InteractionArea 检测（如果存在）
            if (_interactionArea != null)
            {
                GD.Print($"[PlayerItemInteractionComponent] 使用 InteractionArea 检测，路径: {_interactionArea.GetPath()}");
                var overlappingAreas = _interactionArea.GetOverlappingAreas();
                GD.Print($"[PlayerItemInteractionComponent] InteractionArea 重叠的 Area 数量: {overlappingAreas.Count}");
                nearestPickable = FindNearestPickableFromArea(_interactionArea, actorPosition, ref nearestDistanceSq);
            }
            else
            {
                GD.Print($"[PlayerItemInteractionComponent] InteractionArea 为 null，使用距离检测模式");
            }

            // 方法2: 通过距离检测（备用方案，支持 RigidBodyWorldItemEntity）
            if (nearestPickable == null)
            {
                GD.Print($"[PlayerItemInteractionComponent] 尝试使用距离检测，范围: {PickupRange} 像素");
                nearestPickable = FindNearestPickableByDistance(actorPosition, ref nearestDistanceSq);
            }

            // 执行拾取
            if (nearestPickable != null)
            {
                GD.Print($"[PlayerItemInteractionComponent] 找到可拾取物品: {nearestPickable.Name}, 类型: {nearestPickable.GetType().Name}, 距离: {Mathf.Sqrt(nearestDistanceSq):F2}");
                
                if (nearestPickable is WorldItemEntity worldItem)
                {
                    bool result = worldItem.TryPickupByActor(_actor);
                    GD.Print($"[PlayerItemInteractionComponent] WorldItemEntity.TryPickupByActor 结果: {result}");
                    return result;
                }
                else if (nearestPickable is RigidBodyWorldItemEntity rigidItem)
                {
                    bool result = rigidItem.TryPickupByActor(_actor);
                    GD.Print($"[PlayerItemInteractionComponent] RigidBodyWorldItemEntity.TryPickupByActor 结果: {result}");
                    return result;
                }
                else if (nearestPickable is PickupProperty pickupProp)
                {
                    bool result = pickupProp.TryPickupByActor(_actor);
                    GD.Print($"[PlayerItemInteractionComponent] PickupProperty.TryPickupByActor 结果: {result}");
                    return result;
                }
            }
            else
            {
                GD.Print($"[PlayerItemInteractionComponent] 未找到可拾取物品");
            }

            return false;
        }
        
        private Node2D? FindNearestPickableFromArea(Area2D area, Vector2 actorPosition, ref float nearestDistanceSq)
        {
            Node2D? nearestPickable = null;
            
            // 检查重叠的 Area2D（WorldItemEntity、RigidBodyWorldItemEntity 和 PickupProperty 都使用 TriggerArea/GrabArea）
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
                // 检查是否是 RigidBodyWorldItemEntity（适配 RigidBody2D 场景）
                // 注意：GrabArea 可能是 RigidBody2D 的子节点，需要向上查找
                else if (parent is RigidBodyWorldItemEntity rigidEntity)
                {
                    float distanceSq = actorPosition.DistanceSquaredTo(rigidEntity.GlobalPosition);
                    if (distanceSq < nearestDistanceSq)
                    {
                        nearestDistanceSq = distanceSq;
                        nearestPickable = rigidEntity;
                    }
                }
                // 如果父节点是 RigidBody2D，检查其父节点是否是 RigidBodyWorldItemEntity
                else if (parent is RigidBody2D rigidBody)
                {
                    var grandParent = rigidBody.GetParent();
                    if (grandParent is RigidBodyWorldItemEntity rigidEntityFromBody)
                    {
                        float distanceSq = actorPosition.DistanceSquaredTo(rigidEntityFromBody.GlobalPosition);
                        if (distanceSq < nearestDistanceSq)
                        {
                            nearestDistanceSq = distanceSq;
                            nearestPickable = rigidEntityFromBody;
                        }
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
            
            return nearestPickable;
        }
        
        private Node2D? FindNearestPickableByDistance(Vector2 actorPosition, ref float nearestDistanceSq)
        {
            Node2D? nearestPickable = null;
            float rangeSq = PickupRange * PickupRange;
            
            // 通过场景树查找所有 RigidBodyWorldItemEntity
            var sceneTree = GetTree();
            if (sceneTree != null)
            {
                var allRigidItems = sceneTree.GetNodesInGroup("world_items");
                GD.Print($"[PlayerItemInteractionComponent] 在 'world_items' 组中找到 {allRigidItems.Count} 个节点");
                
                foreach (var node in allRigidItems)
                {
                    if (node is RigidBodyWorldItemEntity rigidItem)
                    {
                        float distanceSq = actorPosition.DistanceSquaredTo(rigidItem.GlobalPosition);
                        bool inRange = rigidItem.IsActorInRange(_actor!);
                        GD.Print($"[PlayerItemInteractionComponent] 检查物品 {rigidItem.Name}: 距离={Mathf.Sqrt(distanceSq):F2}, 在GrabArea范围内={inRange}, 距离范围内={distanceSq < rangeSq}");
                        
                        // 检查玩家是否在物品的 GrabArea 范围内
                        if (inRange)
                        {
                            if (distanceSq < rangeSq && distanceSq < nearestDistanceSq)
                            {
                                nearestDistanceSq = distanceSq;
                                nearestPickable = rigidItem;
                                GD.Print($"[PlayerItemInteractionComponent] 选择物品 {rigidItem.Name} 作为最近的拾取目标");
                            }
                        }
                    }
                }
                
                // 也查找 WorldItemEntity 和 PickupProperty（通过距离）
                var allPickables = sceneTree.GetNodesInGroup("pickables");
                GD.Print($"[PlayerItemInteractionComponent] 在 'pickables' 组中找到 {allPickables.Count} 个节点");
                
                foreach (var node in allPickables)
                {
                    if (node is WorldItemEntity worldItem)
                    {
                        float distanceSq = actorPosition.DistanceSquaredTo(worldItem.GlobalPosition);
                        GD.Print($"[PlayerItemInteractionComponent] 检查 WorldItemEntity {worldItem.Name}: 距离={Mathf.Sqrt(distanceSq):F2}");
                        if (distanceSq < rangeSq && distanceSq < nearestDistanceSq)
                        {
                            nearestDistanceSq = distanceSq;
                            nearestPickable = worldItem;
                        }
                    }
                    else if (node is PickupProperty pickup)
                    {
                        float distanceSq = actorPosition.DistanceSquaredTo(pickup.GlobalPosition);
                        GD.Print($"[PlayerItemInteractionComponent] 检查 PickupProperty {pickup.Name}: 距离={Mathf.Sqrt(distanceSq):F2}");
                        if (distanceSq < rangeSq && distanceSq < nearestDistanceSq)
                        {
                            nearestDistanceSq = distanceSq;
                            nearestPickable = pickup;
                        }
                    }
                }
            }
            else
            {
                GD.PrintErr("[PlayerItemInteractionComponent] GetTree() 返回 null");
            }
            
            return nearestPickable;
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
