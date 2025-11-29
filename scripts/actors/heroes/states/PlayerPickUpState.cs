using Godot;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 播放拾取动画，动画结束后执行拾取逻辑。
    /// </summary>
    public partial class PlayerPickUpState : PlayerState
    {
        [Export] public string PickAnimation = "animations/pickup";

        private PlayerItemInteractionComponent? _interaction;
        private float _animRemaining;
        private bool _animationFinished;

        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override void Enter()
        {
            Player.Velocity = Vector2.Zero;
            _animationFinished = false;
            PlayAnimation();
        }

        public override void PhysicsUpdate(double delta)
        {
            UpdateAnimationState(delta);

            if (_animationFinished)
            {
                _interaction?.ExecutePickupAfterAnimation();
                ChangeState("Idle");
            }
        }

        private void PlayAnimation()
        {
            if (Actor.AnimPlayer != null && Actor.AnimPlayer.HasAnimation(PickAnimation))
            {
                Actor.AnimPlayer.Play(PickAnimation);
                _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength;
            }
            else
            {
                _animationFinished = true;
            }
        }

        private void UpdateAnimationState(double delta)
        {
            if (_animationFinished || Actor.AnimPlayer == null)
            {
                return;
            }

            _animRemaining -= (float)delta;
            if (_animRemaining <= 0f || !Actor.AnimPlayer.IsPlaying())
            {
                _animationFinished = true;
            }
        }
    }
}

