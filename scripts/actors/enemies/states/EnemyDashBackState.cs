using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 攻击后短暂后撤，结束后回到 Walk。
    /// </summary>
    public partial class EnemyDashBackState : EnemyState
    {
        [Export(PropertyHint.Range, "10,2000,10")]
        public float DashSpeed = 500.0f;

        [Export(PropertyHint.Range, "0.05,2,0.01")]
        public float DashDuration = 0.2f;

        [Export]
        public string AnimationName = "animations/walk";

        [Export]
        public string NextStateName = "Walk";

        private float _timer;
        private Vector2 _dashDirection = Vector2.Zero;

        public override void Enter()
        {
            _timer = Mathf.Max(DashDuration, 0.01f);

            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer != Vector2.Zero)
            {
                _dashDirection = -toPlayer;
            }
            else
            {
                _dashDirection = Enemy.FacingRight ? Vector2.Left : Vector2.Right;
            }

            if (Enemy.AnimPlayer != null && !string.IsNullOrEmpty(AnimationName) && Enemy.AnimPlayer.HasAnimation(AnimationName))
            {
                Enemy.AnimPlayer.Play(AnimationName);
            }
        }

        public override void Exit()
        {
            if (Enemy != null && GodotObject.IsInstanceValid(Enemy))
            {
                Enemy.Velocity = Vector2.Zero;
            }
        }

        public override bool CanExitTo(string nextStateName)
        {
            if (_timer <= 0f)
            {
                return true;
            }

            return nextStateName == "Dying" || nextStateName == "Dead";
        }

        public override void PhysicsUpdate(double delta)
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
            {
                return;
            }

            _timer -= (float)delta;
            Enemy.Velocity = _dashDirection * DashSpeed;
            Enemy.MoveAndSlide();

            if (_timer <= 0f)
            {
                Enemy.Velocity = Vector2.Zero;

                if (Enemy.StateMachine != null)
                {
                    if (!string.IsNullOrEmpty(NextStateName) && Enemy.StateMachine.HasState(NextStateName))
                    {
                        Enemy.StateMachine.ChangeState(NextStateName);
                    }
                    else
                    {
                        Enemy.StateMachine.ChangeState("Walk");
                    }
                }
            }
        }
    }
}
