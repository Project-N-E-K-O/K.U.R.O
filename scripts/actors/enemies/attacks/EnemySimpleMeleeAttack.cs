using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 示例：基于碰撞盒的简单近战攻击。
    /// 只有当玩家位于 AttackArea 内时才会触发，每次生效造成可配置的伤害。
    /// 通过 AttackIntervalSeconds 控制连续攻击的节奏。
    /// </summary>
    public partial class EnemySimpleMeleeAttack : EnemyAttackTemplate
    {
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float AttackIntervalSeconds = 1.5f;

        [Export(PropertyHint.Range, "1,200,1")]
        public int Damage = 10;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            CooldownDuration = AttackIntervalSeconds;
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;
            return IsPlayerInsideHitbox();
        }

        protected override void OnAttackStarted()
        {
            CooldownDuration = AttackIntervalSeconds;
            base.OnAttackStarted();
        }

        protected override void OnActivePhase()
        {
            float originalDamage = Enemy.AttackDamage;
            Enemy.AttackDamage = Damage;
            base.OnActivePhase();
            Enemy.AttackDamage = originalDamage;
        }

        private bool IsPlayerInsideHitbox()
        {
            if (Player == null) return false;
            if (AttackArea == null) return Enemy.IsPlayerInAttackRange();
            return AttackArea.OverlapsBody(Player);
        }
    }
}

