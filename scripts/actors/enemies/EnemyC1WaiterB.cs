using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Core;

namespace Kuros.Actors.Enemies
{
    /// <summary>
    /// C1 侍者 B：通过攻击控制器驱动三段攻击，并在受击与转向时加入短暂硬直。
    /// </summary>
    public partial class EnemyC1WaiterB : SampleEnemy
    {
        [Export(PropertyHint.Range, "0.1,2,0.1")] public float TurnCooldownSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0.1,2,0.1")] public float HitStunDuration { get; set; } = 0.5f;
        [Export] public NodePath AttackControllerPath { get; set; } = new("StateMachine/Attack/AttackController");

        private float _turnTimer;
        private float _stunTimer;
        private EnemyAttackController? _attackController;

        public EnemyC1WaiterB()
        {
            MaxHealth = 30;
        }

        public override void _Ready()
        {
            base._Ready();
            CurrentHealth = MaxHealth;
            _attackController = GetNodeOrNull<EnemyAttackController>(AttackControllerPath);
            if (_attackController == null)
            {
                GD.PushWarning($"{Name}: AttackController not found at {AttackControllerPath}.");
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            TickTimers((float)delta);

            if (_stunTimer > 0f)
            {
                return;
            }

            UpdateFacing();
        }

        public override void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null)
        {
            base.TakeDamage(damage, attackOrigin, attacker);
            if (damage <= 0)
            {
                return;
            }

            _stunTimer = HitStunDuration;
        }

        private void TickTimers(float delta)
        {
            if (_turnTimer > 0f)
            {
                _turnTimer -= delta;
            }

            if (_stunTimer > 0f)
            {
                _stunTimer -= delta;
            }
        }

        private void UpdateFacing()
        {
            var player = PlayerTarget;
            if (player == null)
            {
                return;
            }

            bool faceRight = player.GlobalPosition.X >= GlobalPosition.X;
            if (faceRight == FacingRight)
            {
                return;
            }

            if (_turnTimer > 0f)
            {
                return;
            }

            FlipFacing(faceRight);
            _turnTimer = TurnCooldownSeconds;
        }
    }
}

