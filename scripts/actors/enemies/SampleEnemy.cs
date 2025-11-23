using Godot;
using System;
using Kuros.Core;

public partial class SampleEnemy : GameActor
{
    [Export] public float DetectionRange = 300.0f;
    [Export] public int ScoreValue = 10;
    
    [Export] public Area2D AttackArea { get; private set; } = null!;
    
    private SamplePlayer? _player;
    private RandomNumberGenerator _rng = new RandomNumberGenerator();
    private float _hitStunTimer = 0.0f; // Re-declared here as it was removed from base
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
        _rng.Randomize();
        
        // Try to find AttackArea if not assigned
        if (AttackArea == null) AttackArea = GetNodeOrNull<Area2D>("AttackArea");
        
        // Find player
        var parent = GetParent();
        if (parent != null)
        {
            _player = parent.GetNodeOrNull<SamplePlayer>("Player");
        }
    }
    
    public SamplePlayer? PlayerTarget => _player;
    
    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        
        // If a StateMachine is present, defer movement/AI to states.
        if (StateMachine != null)
        {
            return;
        }

        if (_player == null) return;
        
        if (_hitStunTimer > 0)
        {
             _hitStunTimer -= (float)delta;
        }
        
        // Distance check
        Vector2 playerPos = _player.GlobalPosition;
        Vector2 enemyPos = GlobalPosition;
        float distanceToPlayer = playerPos.DistanceTo(enemyPos);
        
        Vector2 velocity = Velocity;
        
        if (_hitStunTimer > 0)
        {
            velocity = Vector2.Zero;
            Velocity = velocity;
            MoveAndSlide();
            return;
        }
        
        // AI Logic
        if (distanceToPlayer <= DetectionRange)
        {
            // Always face player if detected, regardless of attack state
            Vector2 direction = (playerPos - enemyPos).Normalized();
            if (direction.X != 0)
            {
                FlipFacing(direction.X > 0);
            }

            bool canAttack = false;

            // Priority 1: Check actual collision overlap if Area exists
            if (AttackArea != null)
            {
                if (AttackArea.OverlapsBody(_player))
                {
                    canAttack = true;
                }
            }
            // Priority 2: Fallback to distance
            else if (distanceToPlayer <= FALLBACK_ATTACK_RANGE + 10.0f)
            {
                canAttack = true;
            }

            if (canAttack)
            {
                // Stop and attack
                velocity = Vector2.Zero;
                
                if (AttackTimer <= 0 && _hitStunTimer <= 0) // Used Property AttackTimer from base
                {
                    PerformAttack();
                }
            }
            else
            {
                // Chase
                velocity = direction * Speed;
            }
        }
        else
        {
            // Idle behavior (slow down)
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed * 2);
            velocity.Y = Mathf.MoveToward(Velocity.Y, 0, Speed * 2);
        }
        
        ClampPositionToScreen();
        
        Velocity = velocity;
        MoveAndSlide();
    }
    
    public bool IsPlayerWithinDetectionRange(float extraMargin = 0.0f)
    {
        if (_player == null) return false;
        float limit = DetectionRange + extraMargin;
        return _player.GlobalPosition.DistanceTo(GlobalPosition) <= limit;
    }
    
    public bool IsPlayerInAttackRange()
    {

        if (_player == null) return false;

        
        if (AttackArea != null)
        {
            return AttackArea.OverlapsBody(_player);
        }

        
        return _player.GlobalPosition.DistanceTo(GlobalPosition) <= FALLBACK_ATTACK_RANGE + 10.0f;
    }
    
    public Vector2 GetDirectionToPlayer()
    {
        if (_player == null) return Vector2.Zero;
        
        Vector2 direction = (_player.GlobalPosition - GlobalPosition);
        return direction == Vector2.Zero ? Vector2.Zero : direction.Normalized();
    }
    
    public void PerformAttack()
    {
        AttackTimer = AttackCooldown; 
        GD.Print("Enemy PerformAttack");
        
        // Use AttackArea for detection if available
        if (AttackArea != null)
        {
            // Flip AttackArea based on facing direction
            var areaPos = AttackArea.Position;
            AttackArea.Position = new Vector2(FacingRight ? Mathf.Abs(areaPos.X) : -Mathf.Abs(areaPos.X), areaPos.Y);
            
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
        else
        {
            // Fallback to old distance logic
             if (_player != null)
            {
                _player.TakeDamage((int)AttackDamage);
                GD.Print("Enemy attacked player (Fallback)!");
            }
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
        // Enemy has shorter stun
        _hitStunTimer = 0.3f;
        
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
}
