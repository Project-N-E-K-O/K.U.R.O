using Godot;
using System;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyNormalGuard2AttackController : EnemyAttackController
    {
        [Export] public string SkillAttackName { get; set; } = "MoveAttack";
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
        [Export(PropertyHint.Range, "1,10,1")] public int MeleeCountBeforeCharge { get; set; } = 2;

        public string CurrentAttackName { get; private set; } = string.Empty;

        private int _meleeCountSinceCharge;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _meleeCountSinceCharge = 0;
            ConfigureNextAttack(forceCharge: false);
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);
            CurrentAttackName = attack.Name;

            if (IsAttack(attack.Name, MeleeAttackName))
            {
                _meleeCountSinceCharge++;
                int threshold = Mathf.Max(1, MeleeCountBeforeCharge);
                ConfigureNextAttack(forceCharge: _meleeCountSinceCharge >= threshold);
                return;
            }

            if (IsAttack(attack.Name, SkillAttackName))
            {
                _meleeCountSinceCharge = 0;
                ConfigureNextAttack(forceCharge: false);
            }
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            CurrentAttackName = string.Empty;
        }

        private void ConfigureNextAttack(bool forceCharge)
        {
            if (forceCharge)
            {
                TrySetAttackWeight(SkillAttackName, 1f);
                TrySetAttackWeight(MeleeAttackName, 0f);
                return;
            }

            TrySetAttackWeight(SkillAttackName, 0f);
            TrySetAttackWeight(MeleeAttackName, 1f);
        }

        private static bool IsAttack(string attackName, string expectedName)
        {
            return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        }
    }
}


