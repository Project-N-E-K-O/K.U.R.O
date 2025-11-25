using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyB1FatAttackController : EnemyAttackController
    {
        [Export] public string ChargeAttackName { get; set; } = "ChargeEscapeAttack";
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
        [Export(PropertyHint.Range, "0,100,0.1")] public float ChargeAttackWeight { get; set; } = 60f;
        [Export(PropertyHint.Range, "0,100,0.1")] public float MeleeAttackWeight { get; set; } = 40f;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            ApplyEnemySpecificWeights();
        }

        private void ApplyEnemySpecificWeights()
        {
            TrySetAttackWeight(ChargeAttackName, ChargeAttackWeight);
            TrySetAttackWeight(MeleeAttackName, MeleeAttackWeight);
        }
    }
}


