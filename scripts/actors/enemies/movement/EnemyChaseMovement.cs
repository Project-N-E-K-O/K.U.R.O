using Godot;
using Godot.Collections;
using Kuros.Core.Effects;

public partial class EnemyChaseMovement : Node
{
    private const string MovementMetaKey = "__movement_component_registered";

    [Export] public float DetectionRangeOverride = -1f;
    [Export] public string IdleStateName = "Idle";
    [Export] public string WalkStateName = "Walk";
    private static readonly StringName AttackStateName = new("Attack");
    private static readonly StringName HitStateName = new("Hit");
    private static readonly StringName FrozenStateName = new("Frozen");
    private static readonly StringName CooldownStateName = new("CooldownFrozen");

    [Export] public Array<StringName> BlockedStates { get; set; } = new Array<StringName>
    {
        AttackStateName,
        HitStateName,
        FrozenStateName,
        CooldownStateName
    };

    protected SampleEnemy? Enemy;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;

        Enemy = GetParent<SampleEnemy>();
        if (Enemy == null)
        {
            GD.PushWarning($"{Name}: EnemyChaseMovement must be a child of SampleEnemy.");
            QueueFree();
            return;
        }

        if (Enemy.HasMeta(MovementMetaKey))
        {
            GD.PushWarning($"{Name}: {Enemy.Name} already has a movement component. Removing duplicate.");
            QueueFree();
            Enemy = null;
            return;
        }

        Enemy.SetMeta(MovementMetaKey, this);

    }

    public override void _ExitTree()
    {
        if (Enemy != null && Enemy.HasMeta(MovementMetaKey))
        {
            var ownerVariant = Enemy.GetMeta(MovementMetaKey);
            if (ownerVariant.VariantType == Variant.Type.Object)
            {
                var owner = ownerVariant.As<Node>();
                if (owner == this)
                {
                    Enemy.RemoveMeta(MovementMetaKey);
                }
            }
            else
            {
                Enemy.RemoveMeta(MovementMetaKey);
            }
        }

        base._ExitTree();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint() || Enemy == null) return;
        if (Enemy.StateMachine == null) return;

        var player = Enemy.PlayerTarget;
        if (player == null) return;

        string currentState = Enemy.StateMachine.CurrentState?.Name ?? string.Empty;
        if (IsBlocked(currentState))
        {
            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * (float)delta);
            Enemy.MoveAndSlide();
            return;
        }

        float detectionRange = DetectionRangeOverride > 0 ? DetectionRangeOverride : Enemy.DetectionRange;
        Vector2 toPlayer = player.GlobalPosition - Enemy.GlobalPosition;
        float distance = toPlayer.Length();

        if (distance <= detectionRange)
        {
            EnsureState(WalkStateName, currentState);
            Vector2 direction = toPlayer == Vector2.Zero ? Vector2.Zero : toPlayer.Normalized();
            Enemy.Velocity = direction * Enemy.Speed;

            if (direction.X != 0)
            {
                Enemy.FlipFacing(direction.X > 0);
            }
        }
        else
        {
            EnsureState(IdleStateName, currentState);
            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2 * (float)delta);
        }

        Enemy.MoveAndSlide();
        Enemy.ClampPositionToScreen();
    }

    private bool IsBlocked(string stateName)
    {
        foreach (var blocked in BlockedStates)
        {
            if (blocked == stateName) return true;
        }
        return false;
    }

    private void EnsureState(string targetState, string currentState)
    {
        if (string.IsNullOrEmpty(targetState)) return;
        if (currentState == targetState) return;
        Enemy?.StateMachine?.ChangeState(targetState);
    }

}

