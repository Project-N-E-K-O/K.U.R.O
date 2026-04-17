using Godot;
using System;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 持握可投掷物品的奔跑状态
    /// 在此状态下可以投掷物品，停止移动则返回 IdleHolding
    /// </summary>
    public partial class PlayerRunHoldingState : PlayerState
    {
        public float RunHoldingAnimationSpeed = 1.0f;
        private float _originalSpeedScale = 1.0f;
        private PlayerItemInteractionComponent? _interaction;
        
        protected override void _ReadyState()
        {
            base._ReadyState();
            _interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
        }
        
        public override void Enter()
        {
            Player.NotifyMovementState(Name);
            //GD.Print($"[PlayerRunHoldingState] 进入持握奔跑状态");
            
            // 播放奔跑动画
            if (Player is MainCharacter mainChar)
            {
                PlayAnimation(mainChar.RunHoldingAnimationName, true, RunHoldingAnimationSpeed);
            }
            else
            {
                if (Actor.AnimPlayer != null)
                {
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    PlayAnimation("animations/run", true, RunHoldingAnimationSpeed);
                }
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
            if (HandleDialogueGating(delta)) return;

            // 检查是否还有可投掷物品
            var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
            if (selectedStack == null || selectedStack.IsEmpty || !selectedStack.Item.IsThrowable)
            {
                //GD.Print($"[PlayerRunHoldingState] 物品不可投掷或已消失，返回 Run");
                ChangeState("Run");
                return;
            }
            
            // 检查投掷按键
            if (Input.IsActionJustPressed("throw"))
            {
                //GD.Print($"[PlayerRunHoldingState] 投掷按键被按下");
                ChangeState("Throw");
                return;
            }
            
            // 检查移动输入
            Vector2 input = GetMovementInput();
            if (input == Vector2.Zero)
            {
                //GD.Print($"[PlayerRunHoldingState] 停止移动，转换到 IdleHolding");
                ChangeState("IdleHolding");
                return;
            }

            // 检查是否停止奔跑
            // if (!IsActionPressed("run"))
            // {
            //     GD.Print($"[PlayerRunHoldingState] 停止奔跑，转换到 Walk");
            //     ChangeState("Walk");
            //     return;
            // }

            // 检查攻击按键
            if (IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
            {
                Player.RequestAttackFromState(Name);
                ChangeState("Throw");
                return;
            }
            
            // 奔跑移动逻辑（x倍速度）
            Vector2 velocity = Actor.Velocity;
            velocity.X = input.X * (Actor.Speed * 1.5f);
            velocity.Y = input.Y * (Actor.Speed * 1.5f);
            
            Actor.Velocity = velocity;
            
            if (input.X != 0)
            {
                Actor.FlipFacing(input.X > 0);
            }
            
            Actor.MoveAndSlide();
            Actor.ClampPositionToScreen();
        }
    }
}
