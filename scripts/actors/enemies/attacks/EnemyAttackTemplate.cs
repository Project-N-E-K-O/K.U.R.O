using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 攻击特效生成时机
    /// </summary>
    public enum EffectSpawnTiming
    {
        OnActive,
        OnAnimationHit,
        OnRecovery
    }

    /// <summary>
    /// 基础敌人攻击模板。封装预热-生效-恢复的攻击流程，并提供可重写的钩子。
    /// 继承此类即可快速实现不同的攻击类型（近战、投射、范围等）。
    /// </summary>
    public partial class EnemyAttackTemplate : Node
    {
        private enum AttackPhase
        {
            Idle,
            Warmup,
            Active,
            Recovery
        }

        [ExportCategory("Meta")]
        [Export] public string AttackName = "DefaultAttack";

        [ExportCategory("Timing (s)")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float WarmupDuration = 0.2f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ActiveDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float RecoveryDuration = 0.35f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float CooldownDuration = 1.0f;

        [ExportCategory("Combat")]
        [Export(PropertyHint.Range, "0,180,1")] public float MaxAllowedAngleToPlayer = 135.0f;
        [Export] public string AnimationName = "animations/attack";
        [Export] public NodePath AttackAreaPath = new NodePath();

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
        [Export(PropertyHint.Range, "0,6000,1")] public float KnockbackSpeed = 0f;

        [ExportCategory("Animation Sync")]
        [Export] public bool RequireAnimationHitTrigger = false;
        [Export] public bool AllowMultipleAnimationHits = false;

        [ExportCategory("Interrupt")]
        [Export] public bool EnableSuperArmor = false;

        [ExportCategory("Collision Override")]
        [Export] public bool IgnoreEnemyCollisionDuringAttack = false;
        [Export(PropertyHint.Range, "1,32,1")] public int EnemyCollisionLayerIndex = 2;

        [ExportCategory("Effect")]
        [Export] public PackedScene? EffectScene = null;
        [Export] public Vector2 EffectOffset = Vector2.Zero;
        [Export] public EffectSpawnTiming SpawnTiming = EffectSpawnTiming.OnActive;

        protected SampleEnemy Enemy { get; private set; } = null!;
        protected SamplePlayer? Player => Enemy.PlayerTarget;
        protected Area2D? AttackArea { get; private set; }

        private AttackPhase _phase = AttackPhase.Idle;
        private float _phaseTimer = 0.0f;
        private float _cooldownTimer = 0.0f;
        protected bool _animationHitReady = false;
        private bool _pendingAnimationHitFromWarmup;
        private bool? _previousIgnoreHitStateOnDamage;
        private uint _cachedCollisionMask;
        private bool _hasCollisionMaskOverride;

        public bool IsRunning => _phase != AttackPhase.Idle;
        public bool IsOnCooldown => _cooldownTimer > 0.0f;

        public virtual void Initialize(SampleEnemy enemy)
        {
            Enemy = enemy;

            if (!string.IsNullOrEmpty(AttackAreaPath.ToString()))
            {
                AttackArea = Enemy.GetNodeOrNull<Area2D>(AttackAreaPath);
            }

            if (AttackArea == null && Enemy.AttackArea != null)
            {
                AttackArea = Enemy.AttackArea;
            }

            OnInitialized();
        }

        protected virtual void OnInitialized() { }

        public virtual bool CanStart()
        {
            if (Enemy == null || Player == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (Enemy.AttackTimer > 0) return false;

            if (!Enemy.IsPlayerWithinDetectionRange())
            {
                return false;
            }

            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer == Vector2.Zero) return false;

            Vector2 facing = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            float angle = Mathf.RadToDeg(facing.AngleTo(toPlayer));
            return angle <= MaxAllowedAngleToPlayer;
        }

        public bool TryStart()
        {
            if (!CanStart()) return false;

            _cooldownTimer = CooldownDuration;
            Enemy.AttackTimer = Mathf.Max(Enemy.AttackTimer, CooldownDuration);
            _animationHitReady = false;
            _pendingAnimationHitFromWarmup = false;

            OnAttackStarted();
            SetPhase(AttackPhase.Warmup);
            return true;
        }

        public void Tick(double delta)
        {
            if (_cooldownTimer > 0.0f)
            {
                _cooldownTimer -= (float)delta;
            }

            if (_phase == AttackPhase.Idle) return;

            _phaseTimer -= (float)delta;
            if (_phaseTimer <= 0.0f)
            {
                AdvancePhase();
            }
        }

        public void Cancel(bool clearCooldown = false)
        {
            if (clearCooldown)
            {
                _cooldownTimer = 0.0f;
                Enemy.AttackTimer = 0.0f;
            }

            if (_phase != AttackPhase.Idle)
            {
                SetPhase(AttackPhase.Idle);
            }
        }

        public override void _ExitTree()
        {
            RestoreEnemyCollisionMask();
            base._ExitTree();
        }

        protected virtual void OnAttackStarted()
        {
            ApplyEnemyCollisionMaskOverride();

            if (EnableSuperArmor && Enemy != null)
            {
                _previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
                Enemy.IgnoreHitStateOnDamage = true;
            }

            if (Enemy != null && !string.IsNullOrEmpty(AnimationName))
            {
                Enemy.AnimPlayer?.Play(AnimationName);
            }
        }

        protected virtual void OnWarmupStarted()
        {
            Enemy.Velocity = Vector2.Zero;
        }

        protected virtual void OnActivePhase()
        {
            if (SpawnTiming == EffectSpawnTiming.OnActive)
            {
                SpawnEffectAtEnemy();
            }

            if (RequireAnimationHitTrigger)
            {
                _animationHitReady = true;
                return;
            }

            PerformAttackNow();
        }

        protected virtual void OnRecoveryStarted()
        {
            if (SpawnTiming == EffectSpawnTiming.OnRecovery)
            {
                SpawnEffectAtEnemy();
            }

            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed);
            _animationHitReady = false;
        }

        protected virtual void OnAttackFinished()
        {
            RestoreEnemyCollisionMask();

            if (Enemy != null && _previousIgnoreHitStateOnDamage.HasValue)
            {
                Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
            }

            _previousIgnoreHitStateOnDamage = null;
        }

        private void ApplyEnemyCollisionMaskOverride()
        {
            if (!IgnoreEnemyCollisionDuringAttack || Enemy == null || _hasCollisionMaskOverride)
            {
                return;
            }

            int clampedLayer = Mathf.Clamp(EnemyCollisionLayerIndex, 1, 32);
            uint enemyLayerBit = 1u << (clampedLayer - 1);
            _cachedCollisionMask = Enemy.CollisionMask;
            Enemy.CollisionMask = _cachedCollisionMask & ~enemyLayerBit;
            _hasCollisionMaskOverride = true;
        }

        private void RestoreEnemyCollisionMask()
        {
            if (Enemy == null || !_hasCollisionMaskOverride)
            {
                return;
            }

            Enemy.CollisionMask = _cachedCollisionMask;
            _hasCollisionMaskOverride = false;
        }

        protected virtual bool ShouldHoldRecoveryPhase()
        {
            return false;
        }

        protected void ForceEnterRecoveryPhase()
        {
            if (_phase == AttackPhase.Active)
            {
                SetPhase(AttackPhase.Recovery);
            }
        }

        private void SetPhase(AttackPhase phase)
        {
            _phase = phase;
            switch (phase)
            {
                case AttackPhase.Warmup:
                    _phaseTimer = WarmupDuration;
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = ActiveDuration;
                    OnActivePhase();
                    TryConsumePendingAnimationHit();
                    break;
                case AttackPhase.Recovery:
                    _phaseTimer = RecoveryDuration;
                    OnRecoveryStarted();
                    break;
                case AttackPhase.Idle:
                    _phaseTimer = 0.0f;
                    OnAttackFinished();
                    break;
            }

            if (_phase != AttackPhase.Idle && _phaseTimer <= 0.0f)
            {
                AdvancePhase();
            }
        }

        private void AdvancePhase()
        {
            switch (_phase)
            {
                case AttackPhase.Warmup:
                    SetPhase(AttackPhase.Active);
                    break;
                case AttackPhase.Active:
                    _animationHitReady = false;
                    _pendingAnimationHitFromWarmup = false;
                    SetPhase(AttackPhase.Recovery);
                    break;
                case AttackPhase.Recovery:
                    if (ShouldHoldRecoveryPhase())
                    {
                        _phaseTimer = 0.05f;
                        return;
                    }

                    SetPhase(AttackPhase.Idle);
                    break;
            }
        }

        protected void PerformAttackNow()
        {
            Enemy.PerformAttack();
        }

        /// <summary>
        /// Spine 帧事件 hit 到达时执行的逻辑。
        /// 默认调用 PerformAttackNow()，子类可覆写以追加击退等额外效果。
        /// 仅在 RequireAnimationHitTrigger = true 时才会被 TriggerAnimationHit 调用。
        /// </summary>
        protected virtual void OnAnimationHit()
        {
            if (SpawnTiming == EffectSpawnTiming.OnAnimationHit)
            {
                SpawnEffectAtEnemy();
            }

            PerformAttackNow();
        }

        public void TriggerAnimationHit()
        {
            GD.Print($"[TriggerAnimationHit] RequireAnimationHitTrigger={RequireAnimationHitTrigger}, _animationHitReady={_animationHitReady}, AllowMultipleAnimationHits={AllowMultipleAnimationHits}");
            if (!RequireAnimationHitTrigger)
            {
                GD.Print("[TriggerAnimationHit] RequireAnimationHitTrigger is false, skip");
                return;
            }

            if (!_animationHitReady)
            {
                if (_phase == AttackPhase.Warmup)
                {
                    _pendingAnimationHitFromWarmup = true;
                    GD.Print("[TriggerAnimationHit] _animationHitReady is false during Warmup, buffer this hit");
                    return;
                }

                GD.Print("[TriggerAnimationHit] _animationHitReady is false, skip");
                return;
            }

            GD.Print("[TriggerAnimationHit] Calling OnAnimationHit()");
            OnAnimationHit();

            if (!AllowMultipleAnimationHits)
            {
                _animationHitReady = false;
                GD.Print("[TriggerAnimationHit] Set _animationHitReady = false");
            }
        }

        private void TryConsumePendingAnimationHit()
        {
            if (!RequireAnimationHitTrigger)
            {
                _pendingAnimationHitFromWarmup = false;
                return;
            }

            if (!_pendingAnimationHitFromWarmup || !_animationHitReady)
            {
                return;
            }

            GD.Print("[TriggerAnimationHit] Consume buffered warmup hit");
            OnAnimationHit();
            _pendingAnimationHitFromWarmup = false;

            if (!AllowMultipleAnimationHits)
            {
                _animationHitReady = false;
            }
        }

        protected bool TryApplyPlayerKnockback(SamplePlayer player, float distance, float duration, float configuredSpeed, Vector2 fallbackDirection)
        {
            if (Enemy == null || player == null)
            {
                return false;
            }

            if (player is Kuros.Actors.Heroes.MainCharacter mainCharacter && mainCharacter.IsHitInvincible)
            {
                if (!mainCharacter.ConsumePendingHitKnockback())
                {
                    return false;
                }
            }

            float clampedDuration = Mathf.Max(duration, 0.01f);
            float clampedDistance = Mathf.Max(0f, distance);
            float clampedConfiguredSpeed = Mathf.Max(0f, configuredSpeed);
            if (clampedDistance <= 0f && clampedConfiguredSpeed <= 0f)
            {
                return false;
            }

            float speed = clampedConfiguredSpeed > 0f ? clampedConfiguredSpeed : clampedDistance / clampedDuration;
            if (speed <= 0f)
            {
                return false;
            }

            Vector2 direction = player.GlobalPosition - Enemy.GlobalPosition;
            if (direction == Vector2.Zero)
            {
                direction = fallbackDirection != Vector2.Zero
                    ? fallbackDirection
                    : (Enemy.FacingRight ? Vector2.Right : Vector2.Left);
            }

            Vector2 knockbackVelocity = direction.Normalized() * speed;
            player.Velocity = knockbackVelocity;
            ApplyFrozenExternalDisplacement(player, knockbackVelocity, clampedDuration);
            return true;
        }

        protected static void ApplyFrozenExternalDisplacement(SamplePlayer player, Vector2 velocity, float duration)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<Kuros.Actors.Heroes.States.PlayerFrozenState>("Frozen");
            if (frozenState == null)
            {
                return;
            }

            if (player.StateMachine?.CurrentState != frozenState)
            {
                return;
            }

            if (!frozenState.AllowExternalDisplacementWhileFrozen)
            {
                return;
            }

            frozenState.ApplyExternalDisplacement(velocity, duration);
        }

        /// <summary>
        /// 在敌人位置生成特效（支持 Node2D 和 ActorEffect）。
        /// </summary>
        protected virtual void SpawnEffectAtEnemy()
        {
            if (EffectScene == null || Enemy == null)
            {
                return;
            }

            try
            {
                var effect = EffectScene.Instantiate();
                
                // 根据敌人朝向调整偏移（X 轴翻转）
                Vector2 adjustedOffset = EffectOffset;
                if (!Enemy.FacingRight && EffectOffset.X != 0)
                {
                    adjustedOffset.X = -EffectOffset.X;
                }
                
                Vector2 spawnPos = Enemy.GlobalPosition + adjustedOffset;

                if (effect is Node2D node2D)
                {
                    // 世界坐标生成（如烟雾、粒子等视觉效果）
                    Enemy.GetParent()?.AddChild(node2D);
                    node2D.GlobalPosition = spawnPos;
                }
                else if (effect is Kuros.Core.Effects.ActorEffect actorEffect)
                {
                    // ActorEffect 应用到敌人身上
                    if (Enemy.EffectController != null)
                    {
                        Enemy.ApplyEffect(actorEffect);
                    }
                    else
                    {
                        actorEffect.QueueFree();
                    }
                }
                else
                {
                    effect?.QueueFree();
                }
            }
            catch (System.Exception ex)
            {
                GD.PushWarning($"[{AttackName}] 无法生成攻击特效: {ex.Message}");
            }
        }
    }
}

