using Godot;
using System;
using Kuros.Core;
using Kuros.Utils;

public partial class SampleEnemy : GameActor
{
	[ExportCategory("Debug")]
	[Export] public bool EnableStateDebugOverlay = false;
	[Export] public Vector2 DebugOverlayOffset = new(-90f, -90f);
	[Export(PropertyHint.Range, "8,128,1")] public int DebugOverlayFontSize = 14;
	[Export] public Color DebugOverlayColor = new(1f, 0.95f, 0.2f, 1f);

	[ExportCategory("Detection")]
	[Export] public Area2D? DetectionArea { get; private set; }
	[Export(PropertyHint.Range, "200,1000,10")] public float AttackRangeCheckDistance = 500f;
	
	[ExportCategory("Attack")]
	[Export] public Area2D? AttackArea { get; private set; }
	
	[ExportCategory("Score")]
	[Export] public int ScoreValue = 10;
	
	private SamplePlayer? _player;
	private bool _scoreGranted;
	private string _debugOverlayText = string.Empty;
	
	// public SampleEnemy()
	// {
	// 	Speed = 150.0f;
	// 	AttackDamage = 10.0f;
	// 	AttackCooldown = 1.5f;
	// 	MaxHealth = 50;
	// }
	
	public override void _Ready()
	{
		base._Ready();
		if (!IsInGroup("enemies"))
		{
			AddToGroup("enemies");
		}
		
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
		UpdateDebugOverlayText();
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!EnableStateDebugOverlay) return;

		UpdateDebugOverlayText();
		QueueRedraw();
	}

	public override void _Draw()
	{
		base._Draw();
		if (!EnableStateDebugOverlay) return;

		var font = ThemeDB.FallbackFont;
		if (font == null) return;

		DrawString(font, DebugOverlayOffset, _debugOverlayText, HorizontalAlignment.Left, -1f, DebugOverlayFontSize, DebugOverlayColor);
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
		return _player.IsHitByArea(AttackArea);
	}

	/// <summary>
	/// 检查玩家是否正在攻击（处于 Attack 状态）。
	/// </summary>
	public bool IsPlayerAttacking()
	{
		RefreshPlayerReference();
		return _player?.StateMachine?.CurrentState?.Name == "Attack";
	}

	/// <summary>
	/// 检查本敌人是否在玩家的攻击范围内（基于固定距离检测，不依赖玩家AttackArea碰撞体）。
	/// </summary>
	public bool IsEnemyInPlayerAttackRange()
	{
		RefreshPlayerReference();
		if (_player == null) return false;
		
		float distanceToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);
		return distanceToPlayer <= AttackRangeCheckDistance;
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
		if (_player != null && AttackArea != null && _player.IsHitByArea(AttackArea))
		{
			_player.TakeDamage((int)AttackDamage, GlobalPosition, this);
			GameLogger.Info(nameof(SampleEnemy), "Enemy attacked player via HitArea.");
		}
	}
	
	public override void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null)
	{
		base.TakeDamage(damage, attackOrigin, attacker);
		// If we want to play hit animation manually since base FSM logic might not cover enemy without state machine
		if (_animationPlayer != null)
		{
			 _animationPlayer.Play("animations/hit");
		}
	}

	public override void TakeDamage(int damage, Vector2? attackOrigin, GameActor? attacker, Kuros.Core.Events.DamageSource damageSource)
	{
		base.TakeDamage(damage, attackOrigin, attacker, damageSource);
		if (_animationPlayer != null)
		{
			_animationPlayer.Play("animations/hit");
		}
	}
	
	protected override void Die()
	{
		GameLogger.Info(nameof(SampleEnemy), "Enemy died!");
		base.Die();
	}

		protected override void OnDeathFinalized()
		{
			RefreshPlayerReference();
			if (!_scoreGranted && _player != null)
			{
				_player.AddScore(ScoreValue);
				_scoreGranted = true;
			}

			base.OnDeathFinalized();
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

	private void UpdateDebugOverlayText()
	{
		string stateName = StateMachine?.CurrentState?.Name ?? "None";
		_debugOverlayText = $"{Name} | State: {stateName} | HP: {CurrentHealth}/{MaxHealth} | SH: {CurrentShield}";
	}
}
