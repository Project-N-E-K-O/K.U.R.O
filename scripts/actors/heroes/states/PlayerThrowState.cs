using Godot;
using Kuros.Items.World;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 播放投掷动画，动画结束后才真正投掷物品。
    /// </summary>
    public partial class PlayerThrowState : PlayerState
    {
        public string ThrowAnimation = "throw_holding_item";
        public float ThrowAnimationSpeed = 1.0f;

        private PlayerItemInteractionComponent? _interaction;
        private bool _hasRequestedThrow;
        private bool _animationFinished;
        private float _animRemaining;
        private float _originalSpeedScale = 1.0f;

        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }

        public override void Enter()
        {
            if (_interaction == null)
            {
                GD.PrintErr($"[PlayerThrowState] ItemInteraction 不存在，无法进行投掷");
                ChangeState("Idle");
                return;
            }

            Player.Velocity = Vector2.Zero;
            _hasRequestedThrow = false;
            _animationFinished = false;
            PlayThrowAnimation();
        }

        public override void Exit()
        {
            base.Exit();
            _hasRequestedThrow = false;
            
            // Restore original animation speed when leaving throw state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (_interaction == null)
            {
                ChangeState("Idle");
                return;
            }

            UpdateAnimationState();

            if (_animationFinished && !_hasRequestedThrow)
            {
                GD.Print($"[PlayerThrowState] 动画完成，触发投掷逻辑");
                if (_interaction.TryTriggerThrowAfterAnimation())
                {
                    _hasRequestedThrow = true;
                }
            }

            if (_hasRequestedThrow)
            {
                ChangeState("Idle");
            }
        }

        private void PlayThrowAnimation()
        {
            GD.Print($"[PlayerThrowState] 尝试播放投掷动画: {ThrowAnimation}");
            
            // 首先尝试使用 PlayerState 的 PlayAnimation 方法（支持 Spine 和 AnimationPlayer）
            if (Player is MainCharacter mainChar)
            {
                // MainCharacter 用 Spine 动画
                GD.Print($"[PlayerThrowState] 检测到 MainCharacter，使用 Spine 动画");
                mainChar.PlaySpineAnimation(ThrowAnimation, loop: false, timeScale: ThrowAnimationSpeed);
                
                // 估算动画长度（Spine 不容易获取动画长度，所以设个合理的默认值）
                _animRemaining = 0.64f; // 假设投掷动画大约 1.5 秒
                GD.Print($"[PlayerThrowState] Spine 动画已播放，预计时长: {_animRemaining}s");
            }
            else if (Actor.AnimPlayer != null)
            {
                // 使用 AnimationPlayer
                GD.Print($"[PlayerThrowState] 检测到 AnimationPlayer");
                if (Actor.AnimPlayer.HasAnimation(ThrowAnimation))
                {
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    Actor.AnimPlayer.Play(ThrowAnimation);
                    Actor.AnimPlayer.SpeedScale = ThrowAnimationSpeed;

                    var speed = Mathf.Max(Actor.AnimPlayer.SpeedScale, 0.0001f);
                    _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / speed;
                    GD.Print($"[PlayerThrowState] AnimationPlayer 动画已播放，动画时长: {_animRemaining}s");
                }
                else
                {
                    GD.PrintErr($"[PlayerThrowState] AnimationPlayer 中找不到动画: {ThrowAnimation}");
                    _animationFinished = true;
                }
            }
            else
            {
                GD.PrintErr($"[PlayerThrowState] 无法找到合适的动画播放方式 (MainCharacter={Player is MainCharacter}, AnimPlayer={Actor.AnimPlayer != null})");
                _animationFinished = true;
            }
        }

        private void UpdateAnimationState()
        {
            if (_animationFinished)
            {
                return;
            }

            _animRemaining -= (float)GetPhysicsProcessDeltaTime();
            
            // 检查动画是否播放完成
            if (_animRemaining <= 0f)
            {
                GD.Print($"[PlayerThrowState] 动画计时完成");
                _animationFinished = true;
            }
            else if (Actor.AnimPlayer != null && !Actor.AnimPlayer.IsPlaying())
            {
                GD.Print($"[PlayerThrowState] AnimationPlayer 动画完成");
                _animationFinished = true;
            }
        }
    }
}

