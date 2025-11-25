using Godot;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 冻结状态：玩家被控制时无法移动或执行输入，直到持续时间结束。
    /// </summary>
    public partial class PlayerFrozenState : PlayerState
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float FrozenDuration = 2.0f;

        private float _timer;
        private bool _externallyHeld;

        public override void Enter()
        {
            _timer = FrozenDuration;
            _externallyHeld = false;
            Actor.Velocity = Vector2.Zero;

            if (Actor.AnimPlayer != null)
            {
                if (Actor.AnimPlayer.HasAnimation("animations/hit"))
                {
                    Actor.AnimPlayer.Play("animations/hit");
                }
                else
                {
                    Actor.AnimPlayer.Play("animations/Idle");
                }
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            Actor.Velocity = Vector2.Zero;
            Actor.MoveAndSlide();

            if (_externallyHeld)
            {
                return;
            }

            _timer -= (float)delta;
            if (_timer <= 0)
            {
                ChangeState("Idle");
            }
        }

        public void BeginExternalHold()
        {
            _timer = FrozenDuration;
            _externallyHeld = true;
        }

        public void EndExternalHold()
        {
            _externallyHeld = false;
			_timer = 0f;
			ChangeState("Idle");
        }
    }
}

