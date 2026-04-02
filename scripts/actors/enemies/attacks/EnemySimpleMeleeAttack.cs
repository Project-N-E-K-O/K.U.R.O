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

        [ExportCategory("Effects")]
        [Export(PropertyHint.Range, "0,2000,1")] public float SimpleMeleeAttackKnockbackDistance = 0f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float SimpleMeleeAttackKnockbackDuration = 0.18f;
        [Export(PropertyHint.Range, "0,6000,1")] public float SimpleMeleeAttackKnockbackSpeed = 0f;

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

            float distance = Mathf.Max(0f, SimpleMeleeAttackKnockbackDistance);
            if ((distance <= 0f && SimpleMeleeAttackKnockbackSpeed <= 0f) || !IsPlayerInsideHitbox())
            {
                return;
            }

            float duration = Mathf.Max(SimpleMeleeAttackKnockbackDuration, 0.01f);
            float speed = SimpleMeleeAttackKnockbackSpeed > 0f
                ? SimpleMeleeAttackKnockbackSpeed
                : distance / duration;

            if (speed <= 0f)
            {
                return;
            }

            Vector2 direction = Player.GlobalPosition - Enemy.GlobalPosition;
            if (direction == Vector2.Zero)
            {
                direction = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            }

            Player.Velocity = direction.Normalized() * speed;
            ApplyFrozenExternalDisplacement(Player, Player.Velocity, duration);
            _activeKnockbackTarget = Player;
            _activeKnockbackTimer = duration;
        }

        private static void ApplyFrozenExternalDisplacement(SamplePlayer player, Vector2 velocity, float duration)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
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

        private bool IsPlayerInsideHitbox()
        {
            if (Player == null) return false;
            if (AttackArea == null) return Enemy.IsPlayerInAttackRange();
            return Player.IsHitByArea(AttackArea);
        }
    }
}

