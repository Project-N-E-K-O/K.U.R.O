using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// NetAdmin 专用 SimpleMeleeAttack。分离检测区和伤害区。
    /// Warmup 期间持续追踪玩家位置，使 DamageArea 对准目标。
    /// </summary>
    public partial class EnemyNetAdminSimpleMeleeAttack : EnemySimpleMeleeAttack
    {
        // 起手检测区由基类提供：TriggerAreaPath（未配置 = 不做额外限制）。
        // 旧的 DetectionAreaPath 已并入基类；伤害区走基类 DamageAreaPath（未配置 = AttackArea）。
        [Export] public bool TrackTargetDuringWarmup = false;

        private CollisionShape2D? _damageShape;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            // 伤害区走基类统一解析：DamageAreaPath → 回退 AttackArea（等价于原来的 `?? AttackArea`）
            _damageShape = DamageArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInTriggerArea();   // 未配置 TriggerAreaPath = 不做额外限制
        }

        public override void _PhysicsProcess(double delta)
        {
            if (TrackTargetDuringWarmup && IsRunning && Enemy != null && Player != null && _damageShape != null)
            {
                var playerShape = Player.HitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                Vector2 playerTarget = playerShape?.GlobalPosition ?? Player.GlobalPosition;
                Vector2 shapeOffset = _damageShape.GlobalPosition - Enemy.GlobalPosition;
                Vector2 target = playerTarget - shapeOffset;
                float step = Enemy.Speed * (float)delta;
                Enemy.GlobalPosition = new Vector2(
                    Mathf.MoveToward(Enemy.GlobalPosition.X, target.X, step),
                    Mathf.MoveToward(Enemy.GlobalPosition.Y, target.Y, step));
            }

            base._PhysicsProcess(delta);
        }

        protected override void OnActivePhase()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnActive); // entry 独立时机生效；未配置回退模板 SpawnTiming
            if (RequireAnimationHitTrigger)
            {
                _animationHitReady = true;
                return;
            }
            ApplyAttackAreaMaskOverride(DamageArea);
            DealDamage(DamageArea);
            ApplyKnockbackWithArea(DamageArea);
        }

        protected override void OnAnimationHit()
        {
            SpawnEffectAtEnemy(EffectSpawnTiming.OnAnimationHit); // entry 独立时机生效
            ApplyAttackAreaMaskOverride(DamageArea);
            DealDamage(DamageArea);
            ApplyKnockbackWithArea(DamageArea);
        }

        private void ApplyKnockbackWithArea(Area2D? area)
        {
            if (Enemy == null || Player == null) return;
            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance <= 0f) return;
            if (area != null && !Player.IsHitByArea(area)) return;

            TryApplyPlayerKnockback(
                Player,
                distance,
                Mathf.Max(KnockbackDuration, 0.01f),
                Enemy.FacingRight ? Vector2.Right : Vector2.Left,
                area);
        }

    }
}
