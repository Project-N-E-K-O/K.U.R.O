using Godot;
using System;
using Kuros.Systems.FSM;
using Kuros.Core.Effects;
using Kuros.Utils;
using Kuros.Core.Stats;

namespace Kuros.Core
{
    public partial class GameActor : CharacterBody2D
    {
        public event Action<int, int>? HealthChanged;

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
        [Export] public CharacterStatProfile? StatProfile { get; private set; }

        // Exposed state for States to use
        public int CurrentHealth { get; protected set; }
        public float AttackTimer { get; set; } = 0.0f;
        public bool FacingRight { get; protected set; } = true;
        public AnimationPlayer? AnimPlayer => _animationPlayer;
        
        protected Node2D _spineCharacter = null!;
        protected Sprite2D _sprite = null!;
        protected AnimationPlayer _animationPlayer = null!;
        private Color _spineDefaultModulate = Colors.White;
        private Color _spriteDefaultModulate = Colors.White;

        private bool _deathStarted = false;
        private bool _deathFinalized = false;

        public bool IsDeathSequenceActive => _deathStarted && !_deathFinalized;
        public bool IsDead => _deathFinalized;

        public override void _Ready()
        {
            CurrentHealth = MaxHealth;
            
            // Node fetching
            _spineCharacter = GetNodeOrNull<Node2D>("SpineCharacter");
            if (_spineCharacter == null)
            {
                _spineCharacter = GetNodeOrNull<Node2D>("SpineSprite");
            }
            if (_spineCharacter != null)
            {
                _spineDefaultModulate = _spineCharacter.Modulate;
            }
            _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
            if (_sprite != null)
            {
                _spriteDefaultModulate = _sprite.Modulate;
            }
            
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

            EffectController ??= GetNodeOrNull<EffectController>("EffectController");
            if (EffectController == null)
            {
                EffectController = new EffectController
                {
                    Name = "EffectController"
                };
                AddChild(EffectController);
            }

            ApplyStatProfile();
            NotifyHealthChanged();
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
            NotifyHealthChanged();

            GameLogger.Info(nameof(GameActor), $"{Name} took {damage} damage! Health: {CurrentHealth}");
            
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

        /// <summary>
        /// 恢复或设置血量（用于加载存档等场景）
        /// </summary>
        public void RestoreHealth(int health, int maxHealth = -1)
        {
            if (maxHealth > 0)
            {
                MaxHealth = maxHealth;
            }
            CurrentHealth = Mathf.Clamp(health, 0, MaxHealth);
            NotifyHealthChanged();
            GameLogger.Info(nameof(GameActor), $"{Name} health restored to {CurrentHealth}/{MaxHealth}");
        }

        protected virtual void Die()
        {
            if (_deathStarted) return;

            _deathStarted = true;

            if (StateMachine != null && StateMachine.HasState("Dying"))
            {
                StateMachine.ChangeState("Dying");
            }
            else
            {
                FinalizeDeath();
            }
        }

        public void FinalizeDeath()
        {
            if (_deathFinalized) return;

            _deathFinalized = true;
            OnDeathFinalized();
        }

        protected virtual void OnDeathFinalized()
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

        private void ApplyStatProfile()
        {
            if (StatProfile == null)
            {
                return;
            }

            foreach (var modifier in StatProfile.GetModifiers())
            {
                if (modifier == null || string.IsNullOrWhiteSpace(modifier.StatId)) continue;
                ApplyStatModifier(modifier);
            }

            if (EffectController == null)
            {
                return;
            }

            foreach (var effectScene in StatProfile.GetAttachedEffectScenes())
            {
                if (effectScene == null) continue;
                EffectController.AddEffectFromScene(effectScene);
            }
        }

        protected virtual void ApplyStatModifier(StatModifier modifier)
        {
            switch (modifier.StatId.ToLowerInvariant())
            {
                case "max_health":
                    MaxHealth = (int)MathF.Round(ApplyStatOperation(MaxHealth, modifier));
                    CurrentHealth = MaxHealth;
                    NotifyHealthChanged();
                    break;
                case "attack_damage":
                    AttackDamage = ApplyStatOperation(AttackDamage, modifier);
                    break;
                case "speed":
                    Speed = ApplyStatOperation(Speed, modifier);
                    break;
            }
        }

        private static float ApplyStatOperation(float baseValue, StatModifier modifier)
        {
            return modifier.Operation switch
            {
                StatOperation.Add => baseValue + modifier.Value,
                StatOperation.Multiply => baseValue * modifier.Value,
                _ => baseValue
            };
        }

        protected virtual void FlashDamageEffect()
        {
            Node2D? visualNode = _spineCharacter ?? _sprite;
            if (visualNode == null) return;

            Color baseColor = visualNode == _spineCharacter ? _spineDefaultModulate : _spriteDefaultModulate;
            visualNode.Modulate = new Color(1f, 0f, 0f);

            var tween = CreateTween();
            tween.TweenInterval(0.1);
            Node2D targetNode = visualNode;
            tween.TweenCallback(Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(targetNode)) return;
                targetNode.Modulate = baseColor;
            }));
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

            if (_sprite != null)
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

        protected void NotifyHealthChanged()
        {
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }
}
