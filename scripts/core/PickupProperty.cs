using Godot;
using System;

namespace Kuros.Core
{
    /// <summary>
    ///     Base prototype for interactive pickup items. Requires a child Area2D named TriggerArea
    ///     (or assign via inspector) which detects GameActor bodies. When the player presses the
    ///     take_up action inside the trigger, the item attaches to the actor.
    /// </summary>
    public partial class PickupProperty : Node2D
    {
        [ExportGroup("Pickup")]
        [Export] public Area2D TriggerArea { get; private set; } = null!;
        [Export] public bool AutoDisableTriggerOnPickup = true;

        [ExportGroup("Attachment")]
        [Export] public NodePath AttachmentPointPath = new NodePath();
        [Export] public Vector2 AttachedLocalOffset = Vector2.Zero;

        protected GameActor? FocusedActor { get; private set; }
        protected GameActor? OwningActor { get; private set; }
        protected bool IsPicked { get; private set; }

        private bool _initialMonitoring = true;
        private bool _initialMonitorable = true;
        private uint _initialCollisionLayer;
        private uint _initialCollisionMask;

        public override void _Ready()
        {
            if (TriggerArea == null)
            {
                TriggerArea = GetNodeOrNull<Area2D>("TriggerArea") ??
                              throw new InvalidOperationException($"{Name} is missing TriggerArea reference.");
            }

            TriggerArea.BodyEntered += OnBodyEntered;
            TriggerArea.BodyExited += OnBodyExited;

            _initialMonitoring = TriggerArea.Monitoring;
            _initialMonitorable = TriggerArea.Monitorable;
            _initialCollisionLayer = TriggerArea.CollisionLayer;
            _initialCollisionMask = TriggerArea.CollisionMask;

            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            if (IsPicked || FocusedActor == null)
            {
                return;
            }

            if (Input.IsActionJustPressed("take_up"))
            {
                HandlePickupRequest(FocusedActor);
            }
        }

        protected virtual void HandlePickupRequest(GameActor actor)
        {
            if (IsPicked)
            {
                return;
            }

            IsPicked = true;
            OwningActor = actor;

            if (AutoDisableTriggerOnPickup && TriggerArea != null)
            {
                DisableTriggerArea();
            }

            AttachToActor(actor);
            OnPicked(actor);
        }

        protected virtual void AttachToActor(GameActor actor)
        {
            Node targetParent = actor;

            if (AttachmentPointPath.GetNameCount() > 0)
            {
                var explicitTarget = actor.GetNodeOrNull<Node>(AttachmentPointPath);
                if (explicitTarget != null)
                {
                    targetParent = explicitTarget;
                    GD.Print($"Successfully found attachment target: {explicitTarget.Name} at path {explicitTarget.GetPath()}");
                }
                else
                {
                    GD.PrintErr($"Failed to find attachment target at path: {AttachmentPointPath} relative to {actor.Name}");
                }
            }

            MoveToParent(targetParent);
            Position = AttachedLocalOffset;
            Rotation = 0; // Reset rotation to align with the bone
        }

        protected virtual void OnPicked(GameActor actor)
        {
            GD.Print($"{Name} picked by {actor.Name}");
        }

        protected virtual void OnPutDown(GameActor actor) { }

        protected virtual void OnActorEnter(GameActor actor) { }

        protected virtual void OnActorExit(GameActor actor) { }

        private void OnBodyEntered(Node2D body)
        {
            if (body is GameActor actor)
            {
                FocusedActor = actor;
                OnActorEnter(actor);
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (body == FocusedActor)
            {
                OnActorExit(FocusedActor);
                FocusedActor = null;
            }
        }

        protected void MoveToParent(Node newParent)
        {
            var currentParent = GetParent();
            if (currentParent == newParent)
            {
                return;
            }

            currentParent?.RemoveChild(this);
            newParent.AddChild(this);
        }

        protected void RestoreTriggerAreaState()
        {
            if (TriggerArea == null)
            {
                return;
            }

            TriggerArea.Monitoring = _initialMonitoring;
            TriggerArea.Monitorable = _initialMonitorable;
            TriggerArea.CollisionLayer = _initialCollisionLayer;
            TriggerArea.CollisionMask = _initialCollisionMask;
        }

        protected void DisableTriggerArea()
        {
            if (TriggerArea == null)
            {
                return;
            }

            TriggerArea.Monitoring = false;
            TriggerArea.Monitorable = false;
            TriggerArea.CollisionLayer = 0;
            TriggerArea.CollisionMask = 0;
        }

        protected bool PutDown(GameActor actor, Vector2 dropOffset = default, bool reactivateTrigger = true)
        {
            if (!IsPicked || OwningActor != actor)
            {
                return false;
            }

            var dropParent = GetDropParent(actor);
            MoveToParent(dropParent);
            GlobalPosition = ComputeDropPosition(actor, dropOffset);

            IsPicked = false;
            OwningActor = null;
            FocusedActor = null;

            if (reactivateTrigger)
            {
                RestoreTriggerAreaState();
                FocusedActor = actor;
                OnActorEnter(actor);
            }

            OnPutDown(actor);

            return true;
        }

        protected virtual Node GetDropParent(GameActor actor)
        {
            return actor.GetTree().CurrentScene ?? actor.GetParent() ?? GetParent() ?? actor;
        }

        protected virtual Vector2 ComputeDropPosition(GameActor actor, Vector2 dropOffset)
        {
            return actor.GlobalPosition + dropOffset;
        }
    }
}

