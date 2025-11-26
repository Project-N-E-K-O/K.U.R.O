using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyB1FatAttackController : EnemyAttackController
    {
        [Export] public string ChargeAttackName { get; set; } = "ChargeEscapeAttack";
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
        [Export(PropertyHint.Range, "0,100,0.1")] public float ChargeAttackWeight { get; set; } = 60f;
        [Export(PropertyHint.Range, "0,100,0.1")] public float MeleeAttackWeight { get; set; } = 40f;

        public string CurrentAttackName { get; private set; } = string.Empty;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            ApplyEnemySpecificWeights();
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

        private void ApplyEnemySpecificWeights()
        {
            TrySetAttackWeight(ChargeAttackName, ChargeAttackWeight);
            TrySetAttackWeight(MeleeAttackName, MeleeAttackWeight);
        }
    }
}


