using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 示例：基于碰撞盒的简单近战攻击。
    /// 只有当玩家位于 AttackArea 内时才会触发，每次生效造成可配置的伤害。
    /// 通过 AttackIntervalSeconds 控制连续攻击的节奏。
    /// </summary>
    public partial class EnemySimpleMeleeAttack : EnemyAttackTemplate
    {
        [ExportCategory("Basic Attack Settings")]
        [Export(PropertyHint.Range, "1,200,1")] public int Damage = 10;

        private SamplePlayer? _activeKnockbackTarget;
        private float _activeKnockbackTimer;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);

            if (_activeKnockbackTarget == null || _activeKnockbackTimer <= 0f)
            {
                return;
            }

            _activeKnockbackTimer -= (float)delta;
            if (_activeKnockbackTimer > 0f)
            {
                return;
            }

            if (GodotObject.IsInstanceValid(_activeKnockbackTarget))
            {
                _activeKnockbackTarget.Velocity = Vector2.Zero;
            }

            _activeKnockbackTarget = null;
            _activeKnockbackTimer = 0f;
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInsideHitbox();
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
        }

        protected override void OnActivePhase()
        {
            float originalDamage = Enemy.AttackDamage;
            Enemy.AttackDamage = Damage;

            base.OnActivePhase();

            // 启用动画事件触发时，击退在 OnAnimationHit 中执行。
            if (!RequireAnimationHitTrigger)
            {
                ApplySimpleMeleeAttackKnockback();
            }

            Enemy.AttackDamage = originalDamage;
        }

        protected override void OnAnimationHit()
        {
            // 先让基类执行伤害（PerformAttackNow）
            base.OnAnimationHit();
            // 再追加击退
            ApplySimpleMeleeAttackKnockback();
        }

        private void ApplySimpleMeleeAttackKnockback()
        {
            if (Enemy == null || Player == null)
            {
                return;
            }

            float distance = Mathf.Max(0f, KnockbackDistance);
            if ((distance <= 0f && KnockbackSpeed <= 0f) || !IsPlayerInsideHitbox())
            {
                return;
            }

            float duration = Mathf.Max(KnockbackDuration, 0.01f);
            bool applied = TryApplyPlayerKnockback(
                Player,
                distance,
                duration,
                KnockbackSpeed,
                Enemy.FacingRight ? Vector2.Right : Vector2.Left);

            if (!applied)
            {
                return;
            }

            _activeKnockbackTarget = Player;
            _activeKnockbackTimer = duration;
        }

        private bool IsPlayerInsideHitbox()
        {
            if (Player == null) return false;
            if (AttackArea == null) return Enemy.IsPlayerInAttackRange();
            return Player.IsHitByArea(AttackArea);
        }
    }
}

