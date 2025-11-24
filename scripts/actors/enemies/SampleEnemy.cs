using Godot;
using System;
using Kuros.Core;

public partial class SampleEnemy : GameActor
{
    [Export] public float DetectionRange = 300.0f;
    [Export] public int ScoreValue = 10;
    
    [Export] public Area2D AttackArea { get; private set; } = null!;
    
    private SamplePlayer? _player;
    private const float FALLBACK_ATTACK_RANGE = 80.0f;
    
    public SampleEnemy()
    {
        Speed = 150.0f;
        AttackDamage = 10.0f;
        AttackCooldown = 1.5f;
        MaxHealth = 50;
    }
    
    public override void _Ready()
    {
        base._Ready();
        
        // Try to find AttackArea if not assigned
        if (AttackArea == null) AttackArea = GetNodeOrNull<Area2D>("AttackArea");
        
        RefreshPlayerReference();
    }
    
    public SamplePlayer? PlayerTarget => _player;
    
    public bool IsPlayerWithinDetectionRange(float extraMargin = 0.0f)
    {
        RefreshPlayerReference();
        if (_player == null) return false;
        float limit = DetectionRange + extraMargin;
        return _player.GlobalPosition.DistanceTo(GlobalPosition) <= limit;
    }
    
    public bool IsPlayerInAttackRange()
        {
        RefreshPlayerReference();
        if (_player == null) return false;
            if (AttackArea != null)
            {
            return AttackArea.OverlapsBody(_player);
                }
        return _player.GlobalPosition.DistanceTo(GlobalPosition) <= FALLBACK_ATTACK_RANGE + 10.0f;
            }

    public Vector2 GetDirectionToPlayer()
    {
        RefreshPlayerReference();
        if (_player == null) return Vector2.Zero;
        Vector2 direction = (_player.GlobalPosition - GlobalPosition);
        return direction == Vector2.Zero ? Vector2.Zero : direction.Normalized();
    }
    
    public void PerformAttack()
    {
        AttackTimer = AttackCooldown; 
        GD.Print("Enemy PerformAttack");
        
        RefreshPlayerReference();
        if (AttackArea != null)
        {
            var bodies = AttackArea.GetOverlappingBodies();
            foreach (var body in bodies)
            {
                if (body is SamplePlayer player)
                {
                    player.TakeDamage((int)AttackDamage);
                    GD.Print("Enemy attacked player via Area2D!");
                }
            }
        }
        else if (_player != null)
            {
                _player.TakeDamage((int)AttackDamage);
                GD.Print("Enemy attacked player (Fallback)!");
        }
        
        // Attack visual effect (scaling)
        Node2D? visualNode = _spineCharacter ?? (Node2D?)_sprite;
        if (visualNode != null)
        {
            var originalScale = visualNode.Scale;
            var targetScale = new Vector2(
                originalScale.X * 1.3f,
                originalScale.Y * 1.3f
            );
            var tween = CreateTween();
            tween.TweenProperty(visualNode, "scale", targetScale, 0.15);
            tween.TweenProperty(visualNode, "scale", originalScale, 0.15);
        }
    }
    
    public override void TakeDamage(int damage)
    {
        base.TakeDamage(damage);
        // If we want to play hit animation manually since base FSM logic might not cover enemy without state machine
        if (_animationPlayer != null)
        {
             _animationPlayer.Play("animations/hit");
        }
    }
    
    protected override void Die()
    {
        GD.Print("Enemy died!");
        
        if (_player != null)
        {
            _player.AddScore(ScoreValue);
        }
        
        // Shrink and disappear
        Node2D? visualNode = _spineCharacter ?? (Node2D?)_sprite;
        if (visualNode != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(visualNode, "scale", Vector2.Zero, 0.3);
            tween.TweenCallback(Callable.From(QueueFree));
        }
        else
        {
            QueueFree();
        }
    }
    private void RefreshPlayerReference()
    {
        if (_player != null && IsInstanceValid(_player)) return;
        _player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
        if (_player == null)
        {
            _player = GetTree().Root.FindChild("Player", true, false) as SamplePlayer;
        }
    }
}
