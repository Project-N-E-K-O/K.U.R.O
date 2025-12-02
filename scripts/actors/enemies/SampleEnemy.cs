using Godot;
using System;
using Kuros.Core;
using Kuros.Utils;

public partial class SampleEnemy : GameActor
{
    [ExportCategory("Detection")]
    [Export] public Area2D? DetectionArea { get; private set; }
    
    [ExportCategory("Attack")]
    [Export] public Area2D? AttackArea { get; private set; }
    
    [ExportCategory("Score")]
    [Export] public int ScoreValue = 10;
    
    private SamplePlayer? _player;
    
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
        
        // Try to find areas if not assigned (they are nested under Sprite2D in the scene)
        if (AttackArea == null) 
        {
            AttackArea = GetNodeOrNull<Area2D>("Sprite2D/AttackArea");
            if (AttackArea == null) GD.PrintErr("AttackArea not found at Sprite2D/AttackArea");
        }
        if (DetectionArea == null) 
        {
            DetectionArea = GetNodeOrNull<Area2D>("Sprite2D/DetectionArea");
            if (DetectionArea == null) GD.PrintErr("DetectionArea not found at Sprite2D/DetectionArea");
        }
        RefreshPlayerReference();
    }
    
    public SamplePlayer? PlayerTarget => _player;
    
    /// <summary>
    /// 检查玩家是否在检测范围内。使用 DetectionArea 碰撞检测。
    /// </summary>
    public bool IsPlayerWithinDetectionRange()
    {
        RefreshPlayerReference();
        if (_player == null || DetectionArea == null) return false;
        return DetectionArea.OverlapsBody(_player);
    }
    
    /// <summary>
    /// 检查玩家是否在攻击范围内。使用 AttackArea 碰撞检测。
    /// </summary>
    public bool IsPlayerInAttackRange()
    {
        RefreshPlayerReference();
        if (_player == null || AttackArea == null) return false;
        return AttackArea.OverlapsBody(_player);
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
        GameLogger.Info(nameof(SampleEnemy), "Enemy PerformAttack");
        
        RefreshPlayerReference();
        if (AttackArea != null)
        {
            var bodies = AttackArea.GetOverlappingBodies();
            foreach (var body in bodies)
            {
                if (body is SamplePlayer player)
                {
                    player.TakeDamage((int)AttackDamage);
                    GameLogger.Info(nameof(SampleEnemy), "Enemy attacked player via Area2D!");
                }
            }
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
            GameLogger.Info(nameof(SampleEnemy), "Enemy died!");
            
            if (_player != null)
            {
                _player.AddScore(ScoreValue);
            }
            
            QueueFree();
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
