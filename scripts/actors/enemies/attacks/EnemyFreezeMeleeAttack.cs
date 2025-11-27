using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 基于 EnemySimpleMeleeAttack 的变体：命中时会让玩家进入冻结状态。
    /// </summary>
    public partial class EnemyFreezeMeleeAttack : EnemySimpleMeleeAttack
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float FreezeDurationSeconds = 1.5f;

        protected override void OnActivePhase()
        {
            base.OnActivePhase();
            TryApplyFreeze();
        }

        private void TryApplyFreeze()
        {
            if (Player == null) return;

            bool inRange = AttackArea != null
                ? AttackArea.OverlapsBody(Player)
                : Enemy.IsPlayerInAttackRange();

            if (!inRange) return;

            var frozenState = Player.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
            if (frozenState == null) return;

            frozenState.FrozenDuration = FreezeDurationSeconds;
            Player.StateMachine?.ChangeState("Frozen");
        }
    }
}

