using Godot;
using System;
using Kuros.Systems.FSM;
using Kuros.Core.Effects;

namespace Kuros.Core
{
    public partial class GameActor : CharacterBody2D
    {
        [ExportCategory("Stats")]
        [Export] public float Speed = 300.0f;
        [Export] public float AttackDamage = 25.0f;
        // [Export] public float AttackRange = 100.0f; // Removed: Deprecated, rely on AttackArea logic
        [Export] public float AttackCooldown = 0.5f;
        [Export] public int MaxHealth = 100;
        [Export] public bool FaceLeftByDefault = false;
        
        [ExportCategory("Components")]
        [Export] public StateMachine StateMachine { get; private set; } = null!;
        [Export] public EffectController EffectController { get; private set; } = null!;

        // Exposed state for States to use
        public int CurrentHealth { get; protected set; }
        public float AttackTimer { get; set; } = 0.0f;
        public bool FacingRight { get; protected set; } = true;
        public AnimationPlayer? AnimPlayer => _animationPlayer;
        
        protected Node2D _spineCharacter = null!;
        protected Sprite2D _sprite = null!;
        protected AnimationPlayer _animationPlayer = null!;

        public override void _Ready()
        {
            CurrentHealth = MaxHealth;
            
            // Node fetching
            _spineCharacter = GetNodeOrNull<Node2D>("SpineCharacter");
            _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
            
            if (_spineCharacter != null)
            {
                _animationPlayer = _spineCharacter.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            }
            
            // Initialize StateMachine if manually assigned or found
            if (StateMachine == null)
            {
                StateMachine = GetNodeOrNull<StateMachine>("StateMachine");
            }

            if (StateMachine != null)
            {
                StateMachine.Initialize(this);
            }

            if (EffectController == null)
            {
                EffectController = GetNodeOrNull<EffectController>("EffectController");
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (AttackTimer > 0) AttackTimer -= (float)delta;
            
            // FSM handles logic, but we can keep global helpers here
            // If using FSM, ensure it is processed either here or by itself (Node process)
            // StateMachine._PhysicsProcess is called automatically by Godot if it's in the tree
        }

        public virtual void TakeDamage(int damage)
        {
            CurrentHealth -= damage;
            CurrentHealth = Mathf.Max(CurrentHealth, 0);
            
            GD.Print($"{Name} took {damage} damage! Health: {CurrentHealth}");
            
            FlashDamageEffect();

            if (CurrentHealth <= 0)
            {
                Die();
            }
            else
            {
                // Force state change to Hit
                if (StateMachine != null)
                {
                    StateMachine.ChangeState("Hit");
                }
            }
        }

        protected virtual void Die()
        {
            EffectController?.ClearAll();
            QueueFree();
        }

        public void ApplyEffect(ActorEffect effect)
        {
            EffectController?.AddEffect(effect);
        }

        public void RemoveEffect(string effectId)
        {
            var effect = EffectController?.GetEffect(effectId);
            if (effect != null)
            {
                EffectController?.RemoveEffect(effect);
            }
        }

        protected virtual void FlashDamageEffect()
        {
            Node2D visualNode = _spineCharacter ?? (Node2D)_sprite;
            if (visualNode != null)
            {
                var originalColor = visualNode.Modulate;
                visualNode.Modulate = new Color(1, 0, 0); 
                
                var tween = CreateTween();
                tween.TweenInterval(0.1);
                tween.TweenCallback(Callable.From(() => visualNode.Modulate = originalColor));
            }
        }

        public virtual void FlipFacing(bool faceRight)
        {
            if (FacingRight == faceRight) return;
            
            FacingRight = faceRight;
            
            // Calculate the correct X scale sign based on direction and default facing
            // If faceRight is requested:
            //   - Default Right: Scale should be positive
            //   - Default Left: Scale should be negative
            float sign = faceRight ? 1.0f : -1.0f;
            if (FaceLeftByDefault) sign *= -1.0f;
            
            if (_spineCharacter != null)
            {
                var scale = _spineCharacter.Scale;
                float absX = Mathf.Abs(scale.X);
                _spineCharacter.Scale = new Vector2(absX * sign, scale.Y);
            }
            else if (_sprite != null)
            {
                // Prefer Scale flipping over FlipH, so children (like AttackArea) flip too
                var scale = _sprite.Scale;
                float absX = Mathf.Abs(scale.X);
                _sprite.Scale = new Vector2(absX * sign, scale.Y);
            }
        }
        
        public void ClampPositionToScreen(float margin = 50f, float bottomOffset = 150f)
        {
             var screenSize = GetViewportRect().Size;
             GlobalPosition = new Vector2(
                Mathf.Clamp(GlobalPosition.X, margin, screenSize.X - margin),
                Mathf.Clamp(GlobalPosition.Y, margin, screenSize.Y - bottomOffset)
            );
        }
    }
}
