using Godot;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 玩家死亡过渡状态，用于播放死亡动画/倒地时间。
    /// </summary>
    public partial class PlayerDyingState : PlayerState
    {
        [Export(PropertyHint.Range, "0,10,0.01")] public float DeathDuration = 1.0f;
        [Export] public bool FreezeMotion = true;

        private float _timer;

        public override void Enter()
        {
            _timer = DeathDuration;
            Player.AttackTimer = 0f;

            if (FreezeMotion)
            {
                Player.Velocity = Vector2.Zero;
                Player.MoveAndSlide();
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (FreezeMotion)
            {
                Player.Velocity = Player.Velocity.MoveToward(Vector2.Zero, Player.Speed * 2f * (float)delta);
                Player.MoveAndSlide();
            }

            _timer -= (float)delta;
            if (_timer <= 0f)
            {
                ChangeState("Dead");
            }
        }
    }
}


