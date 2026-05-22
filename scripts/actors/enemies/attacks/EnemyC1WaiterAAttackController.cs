using Godot;
using System;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// C1 服务员 A 的攻击控制器。
    /// 
    /// 攻击切换逻辑（每帧轮询）：
    ///   - 玩家在近战 AttackArea 内 → SimpleMeleeAttack（权重 100），打断当前 ThrowAttack
    ///   - 玩家不在近战 AttackArea 内 → ThrowAttack（权重 100），当前近战自然结束后生效
    /// </summary>
    public partial class EnemyC1WaiterAAttackController : EnemyAttackController
    {
        /// <summary>近战攻击名字。</summary>
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";

        /// <summary>远程攻击名字。</summary>
        [Export] public string ThrowAttackName { get; set; } = "ThrowAttack";

        public string CurrentAttackName { get; private set; } = string.Empty;

        private bool _playerInMeleeRange;

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            SyncWeightsToRange();
        }

        private void SyncWeightsToRange()
        {
            if (Enemy == null) return;

            bool inMelee = Enemy.IsPlayerInAttackRange();
            if (inMelee == _playerInMeleeRange) return;

            _playerInMeleeRange = inMelee;

            TrySetAttackWeight(MeleeAttackName, inMelee ? 100f : 0f);
            TrySetAttackWeight(ThrowAttackName, inMelee ? 0f : 100f);

            // 玩家进入近战范围时，若正在投掷则打断立即切换
            if (inMelee && IsAttack(CurrentAttackName, ThrowAttackName))
                ForceQueueNextAttack("PlayerEnteredMeleeRange");
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);
            CurrentAttackName = attack.Name;
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            CurrentAttackName = string.Empty;
        }

        private static bool IsAttack(string attackName, string expectedName)
            => attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
    }
}


