using Godot;
using System;
using Kuros.Core;
using Kuros.Systems.FSM;
using Kuros.Actors.Heroes.States;
using Kuros.Actors.Heroes;
using Kuros.Systems.Inventory;

public partial class SamplePlayer : GameActor
{
	[ExportCategory("Combat")]
	[Export] public Area2D AttackArea { get; private set; } = null!;
	public PlayerFrozenState? FrozenState { get; private set; }
	public PlayerInventoryComponent? InventoryComponent { get; private set; }
	public InventoryContainer? Backpack => InventoryComponent?.Backpack;
	
	[ExportCategory("UI")]
	[Export] public Label StatsLabel { get; private set; } = null!; // Drag & Drop in Editor
	
	private int _score = 0;
	private string _pendingAttackSourceState = string.Empty;
	public string LastMovementStateName { get; private set; } = "Idle";
	
	// Signal for UI updates (Alternative to direct reference)
	[Signal] public delegate void StatsChangedEventHandler(int health, int score);

	public override void _Ready()
	{
		base._Ready();
		AddToGroup("player");
		
		// Fallback: Try to find nodes if not assigned in editor (Backward compatibility)
		if (AttackArea == null) AttackArea = GetNodeOrNull<Area2D>("AttackArea");
		if (FrozenState == null) FrozenState = StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
		if (StatsLabel == null) StatsLabel = GetNodeOrNull<Label>("../UI/PlayerStats");
		if (InventoryComponent == null) InventoryComponent = GetNodeOrNull<PlayerInventoryComponent>("Inventory");
		
		UpdateStatsUI();
	}
	
	public void RequestAttackFromState(string stateName)
	{
		_pendingAttackSourceState = stateName;
	}

	public string ConsumeAttackRequestSource()
	{
		string source = _pendingAttackSourceState;
		_pendingAttackSourceState = string.Empty;
		return source;
	}

	public void NotifyMovementState(string stateName)
	{
		LastMovementStateName = stateName;
	}
	
	// Override FlipFacing to handle AttackArea flipping correctly when turning
	public override void FlipFacing(bool faceRight)
	{
		base.FlipFacing(faceRight);
		
		// If AttackArea is NOT a child of the flipped sprite/spine, we must flip it manually here.
		// This is better than doing it in PerformAttackCheck because physics has time to update.
		if (AttackArea != null)
		{
			 // We assume the AttackArea is centered or offset. If offset, we flip the offset.
			 // Check if AttackArea parent is NOT the flipped visual (to avoid double flipping)
			 if (AttackArea.GetParent() != _spineCharacter && AttackArea.GetParent() != _sprite)
			 {
				 var areaPos = AttackArea.Position;
				 float absX = Mathf.Abs(areaPos.X);
				 AttackArea.Position = new Vector2(faceRight ? absX : -absX, areaPos.Y);
				 
				 // Optional: Flip scale too if the shape is asymmetric
				 var areaScale = AttackArea.Scale;
				 float absScaleX = Mathf.Abs(areaScale.X);
				 AttackArea.Scale = new Vector2(faceRight ? absScaleX : -absScaleX, areaScale.Y);
			 }
		}
	}
	
	public void PerformAttackCheck()
	{
		// Reset timer just in case, though State usually manages cooldown entry
		AttackTimer = AttackCooldown;
		
		GD.Print($"=== Player attacking frame! ===");
		
		int hitCount = 0;
		
		if (AttackArea != null)
		{
			// REMOVED: Manual Position flipping here. It's now handled in FlipFacing or via Scene Hierarchy.
            
            var bodies = AttackArea.GetOverlappingBodies();
            foreach (var body in bodies)
            {
                if (body is SampleEnemy enemy)
                {
                    enemy.TakeDamage((int)AttackDamage);
                    hitCount++;
                    GD.Print($"Hit enemy: {enemy.Name}");
                }
            }
        }
        else
        {
            GD.PrintErr("AttackArea is missing! Assign it in Inspector.");
        }
        
        if (hitCount == 0)
        {
            GD.Print("No enemies hit!");
        }
    }
    
    public override void TakeDamage(int damage)
    {
		_pendingAttackSourceState = string.Empty;
        base.TakeDamage(damage);
        UpdateStatsUI();
    }
    
    public void AddScore(int points)
    {
        _score += points;
        UpdateStatsUI();
    }
    
    private void UpdateStatsUI()
    {
        // Emit signal for decoupled UI systems
        EmitSignal(SignalName.StatsChanged, CurrentHealth, _score);
        
        // Direct update if label is assigned (Simple approach)
        if (StatsLabel != null)
        {
            StatsLabel.Text = $"Player HP: {CurrentHealth}\nScore: {_score}";
        }
    }
    
    protected override void Die()
    {
        GD.Print("Player died! Game Over!");
        GetTree().ReloadCurrentScene();
    }
}
