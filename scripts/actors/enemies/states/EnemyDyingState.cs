using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 敌人死亡过渡状态：用于播放击倒动画/特效，随后切换到 Dead 状态。
    /// </summary>
    public partial class EnemyDyingState : EnemyState
    {
        [Export(PropertyHint.Range, "0,10,0.01")] public float DeathDuration = 0.8f;
        [Export] public bool FreezeMotion = true;

        private float _timer;

        public override void Enter()
        {
            _timer = DeathDuration;
            Enemy.AttackTimer = 0f;

            if (FreezeMotion)
            {
                Enemy.Velocity = Vector2.Zero;
                Enemy.MoveAndSlide();
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (FreezeMotion)
            {
                Enemy.Velocity = Vector2.Zero;
                Enemy.MoveAndSlide();
            }

            _timer -= (float)delta;
            if (_timer <= 0f)
            {
                ChangeState("Dead");
            }
        }
    }
}


