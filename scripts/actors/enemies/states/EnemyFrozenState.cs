using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 敌人被冻结或被控制时的通用状态。
    /// </summary>
    public partial class EnemyFrozenState : EnemyState
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float FrozenDuration = 2.0f;

        private float _timer;

        public override void Enter()
        {
            _timer = FrozenDuration;
            Enemy.Velocity = Vector2.Zero;

            if (Enemy.AnimPlayer != null)
            {
                if (Enemy.AnimPlayer.HasAnimation("animations/hit"))
                {
                    Enemy.AnimPlayer.Play("animations/hit");
                }
                else
                {
                    Enemy.AnimPlayer.Play("animations/Idle");
                }
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            Enemy.Velocity = Vector2.Zero;
            Enemy.MoveAndSlide();

            _timer -= (float)delta;
            if (_timer <= 0)
            {
                ChangeState("Idle");
            }
        }
    }
}

