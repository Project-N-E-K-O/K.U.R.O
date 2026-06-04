using Godot;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 播放拾取动画，动画结束后执行拾取逻辑。
    /// 若无拾取动画（如 Spine 角色），则立即执行拾取，不打断当前动画。
    /// </summary>
    public partial class PlayerPickUpState : PlayerState
    {
        public string PickAnimation = "animations/pickup";
        public float PickUpAnimationSpeed = 1.0f;
        public float PickUpAnimationTotalTime = 0.3f;

        private PlayerItemInteractionComponent? _interaction;
        private float _animRemaining;
        private bool _animationFinished;
        private float _originalSpeedScale = 1.0f;

        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override bool CanEnterFrom(string? currentStateName)
        {
            // 攻击、投掷等状态有自己的动画，进入 PickUp 会导致动画被打断。
            // 这些状态中拾取动作直接执行，不走状态切换。
            if (currentStateName is "Attack" or "Throw" or "Hit" or "Dying" or "Dead" or "Frozen")
            {
                return false;
            }
            return base.CanEnterFrom(currentStateName);
        }

        public override void Enter()
        {
            _animationFinished = false;

            if (Actor.AnimPlayer != null)
            {
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
            }

            if (!TryPlayPickupAnimation())
            {
                _animationFinished = true;
            }
            else
            {
                Player.Velocity = Vector2.Zero;
            }
        }

        public override void Exit()
        {
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
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

        private bool TryPlayPickupAnimation()
        {
            if (Player is MainCharacter)
            {
                // MainCharacter 使用 Spine 动画，当前 spine 中没有拾取动画，
                // 跳过以保留上一个状态的动画不被中断。
                // 若将来添加了拾取动画，改为：
                //   var mc = (MainCharacter)Player;
                //   mc.PlaySpineAnimation(PickAnimation, false, PickUpAnimationSpeed);
                //   _animRemaining = PickUpAnimationTotalTime / PickUpAnimationSpeed;
                //   return true;
                return false;
            }

            if (Actor.AnimPlayer != null && Actor.AnimPlayer.HasAnimation(PickAnimation))
            {
                Actor.AnimPlayer.Play(PickAnimation);
                Actor.AnimPlayer.SpeedScale = PickUpAnimationSpeed;
                var speed = Mathf.Max(PickUpAnimationSpeed, 0.0001f);
                _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / speed;
                return true;
            }

            return false;
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

