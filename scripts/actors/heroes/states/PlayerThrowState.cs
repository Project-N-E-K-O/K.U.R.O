using Godot;
using Kuros.Items.World;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 投掷状态：播放投掷动画，动画结束后真正投掷物品
    /// 投掷完成后根据是否还有可投掷物品决定转到 IdleHolding 或 Idle
    /// </summary>
    public partial class PlayerThrowState : PlayerState
{
    public string ThrowAnimation = "throw_holding_item";
    public float ThrowAnimationSpeed = 1f;
    public float ThrowTriggerTime = 0.3f;  // 投掷触发时间点
    public float ThrowAnimationTotalTime = 0.64f;  // 动画总时长

    private PlayerItemInteractionComponent? _interaction;
    private bool _hasRequestedThrow;
    private bool _animationFinished;
    private float _animRemaining;
    private float _throwTriggerRemaining;  // 投掷触发倒计时
    private float _originalSpeedScale = 1.0f;

    protected override void _ReadyState()
    {
        base._ReadyState();
        _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
    }

    public override void Enter()
    {
        //GD.Print($"[PlayerThrowState] 进入投掷状态");

        if (_interaction == null)
        {
            GD.PrintErr($"[PlayerThrowState] ItemInteraction 不存在，无法进行投掷");
            ChangeState("Idle");
            return;
        }

        Player.Velocity = Vector2.Zero;
        _hasRequestedThrow = false;
        _animationFinished = false;
        _throwTriggerRemaining = ThrowTriggerTime;
        PlayThrowAnimation();
    }

    public override void Exit()
    {
        base.Exit();
        _hasRequestedThrow = false;

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

        // 第一阶段：0.3秒时触发投掷，但不结束状态
        if (!_hasRequestedThrow && _throwTriggerRemaining <= 0f)
        {
            GD.Print($"[PlayerThrowState] 0.3秒触发投掷");
            _interaction.TryTriggerThrowAfterAnimation();
            _hasRequestedThrow = true;
        }

        // 第二阶段：动画完整播放完毕后再切换状态
        if (_animationFinished)
        {
            var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
            if (selectedStack != null && !selectedStack.IsEmpty && selectedStack.Item.IsThrowable)
            {
                GD.Print($"[PlayerThrowState] 还有可投掷物品，返回 IdleHolding");
                ChangeState("IdleHolding");
            }
            else
            {
                GD.Print($"[PlayerThrowState] 没有可投掷物品，返回 Idle");
                ChangeState("Idle");
            }
        }
    }

    private void PlayThrowAnimation()
    {
        //GD.Print($"[PlayerThrowState] 播放投掷动画: {ThrowAnimation}");

        if (Player is MainCharacter mainChar)
        {
            mainChar.PlaySpineAnimation(ThrowAnimation, loop: false, timeScale: ThrowAnimationSpeed);
            _animRemaining = ThrowAnimationTotalTime / ThrowAnimationSpeed;
            //GD.Print($"[PlayerThrowState] Spine 动画已播放，预计时长: {_animRemaining}s");
        }
        else if (Actor.AnimPlayer != null)
        {
            if (Actor.AnimPlayer.HasAnimation(ThrowAnimation))
            {
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                Actor.AnimPlayer.Play(ThrowAnimation);
                Actor.AnimPlayer.SpeedScale = ThrowAnimationSpeed;

                var speed = Mathf.Max(Actor.AnimPlayer.SpeedScale, 0.0001f);
                _animRemaining = (float)Actor.AnimPlayer.CurrentAnimationLength / speed;
                //GD.Print($"[PlayerThrowState] AnimationPlayer 动画已播放，时长: {_animRemaining}s");
            }
            else
            {
                //GD.PrintErr($"[PlayerThrowState] 找不到动画: {ThrowAnimation}");
                _animationFinished = true;
            }
        }
        else
        {
            //GD.PrintErr($"[PlayerThrowState] 无法找到动画播放方式");
            _animationFinished = true;
        }
    }

    private void UpdateAnimationState()
    {
        float delta = (float)GetPhysicsProcessDeltaTime();

        // 投掷触发倒计时
        if (_throwTriggerRemaining > 0f)
        {
            _throwTriggerRemaining -= delta;
        }

        // 动画总时长倒计时
        if (!_animationFinished)
        {
            _animRemaining -= delta;

            if (_animRemaining <= 0f)
            {
                _animationFinished = true;
            }
            else if (Actor.AnimPlayer != null && !Actor.AnimPlayer.IsPlaying())
            {
                _animationFinished = true;
            }
        }
    }
}
}

