using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyB1ThinAttackController : EnemyAttackController
    {
        [Export] public string FreezeAttackName { get; set; } = "FreezeMeleeAttack";
        [Export] public string ComboAttackName { get; set; } = "MultiStrikeAttack";

        private bool _freezeAttackTriggered;

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);

            if (_freezeAttackTriggered)
            {
                return;
            }

            if (attack?.Name != FreezeAttackName)
            {
                return;
            }

            _freezeAttackTriggered = true;
            TrySetAttackWeight(FreezeAttackName, 0f);
            TrySetAttackWeight(ComboAttackName, 1f);
        }
    }
}


