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

            if (Input.IsActionJustPressed("take_up"))
            {
                TriggerPickupState();
            }
        }

        public bool TryTriggerThrowAfterAnimation()
        {
            return TryHandleDrop(DropDisposition.Throw, skipAnimation: true);
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

            if (!InventoryComponent.HasHeldItem)
            {
                GameLogger.Info(nameof(PlayerItemInteractionComponent), "当前没有持有物品，无法执行丢弃/投掷。");
                return false;
            }

            if (!skipAnimation && disposition == DropDisposition.Throw)
            {
                TriggerThrowState();
                return false;
            }

            var stack = InventoryComponent.TakeHeldItemStack();
            if (stack == null || stack.IsEmpty)
            {
                return false;
            }

            var spawnPosition = ComputeSpawnPosition(disposition);
            var entity = WorldItemSpawner.SpawnFromStack(this, stack, spawnPosition);

            if (entity == null)
            {
                InventoryComponent.TryReturnHeldItem(stack);
                return false;
            }

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

        internal bool ExecutePickupAfterAnimation() => TryHandlePickup();

        private void TriggerPickupState()
        {
            if (InventoryComponent?.HasHeldItem == true)
            {
                GameLogger.Info(nameof(PlayerItemInteractionComponent), "已持有物品，无法重复拾取。");
                return;
            }

            if (_actor?.StateMachine == null)
            {
                TryHandlePickup();
                return;
            }

            _actor.StateMachine.ChangeState("PickUp");
        }

        private bool TryHandlePickup()
        {
            if (_actor == null)
            {
                return false;
            }

            if (InventoryComponent?.HasHeldItem == true)
            {
                GameLogger.Info(nameof(PlayerItemInteractionComponent), "已持有物品，无法拾取新的物品。");
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

        private void TriggerThrowState()
        {
            if (_actor?.StateMachine == null)
            {
                return;
            }

            _actor.StateMachine.ChangeState(ThrowStateName);
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

