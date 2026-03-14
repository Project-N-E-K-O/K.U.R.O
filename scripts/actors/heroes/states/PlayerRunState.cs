using Godot;
using System;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerRunState : PlayerState
    {
        public float RunAnimationSpeed = 1.0f;
        private float _originalSpeedScale = 1.0f;
        
        public override void Enter()
        {
            Player.NotifyMovementState(Name);
            
            // 使用 PlayAnimation 方法，自动适配 MainCharacter 和 SamplePlayer
            if (Player is MainCharacter mainChar)
            {
                // MainCharacter 使用 Spine 动画
                PlayAnimation(mainChar.RunAnimationName, true, RunAnimationSpeed);
            }
            else
            {
                // SamplePlayer 使用 AnimationPlayer
                if (Actor.AnimPlayer != null)
                {
                    // Save original speed scale before modifying
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    
                    // 使用 PlayAnimation 方法（虽然它会再次检查，但这样可以统一接口）
                    PlayAnimation("animations/run", true, RunAnimationSpeed);
                }
            }
            // Increase speed by changing velocity calculation, not base stat
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving run state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (HandleDialogueGating(delta)) return;
            
            if (Input.IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
            {
                Player.RequestAttackFromState(Name);
                ChangeState("Attack");
                return;
            }
            
            // Stop running if shift is released
            if (!Input.IsActionPressed("run"))
            {
                ChangeState("Walk");
                return;
            }
            
            Vector2 input = GetMovementInput();
            
            if (input == Vector2.Zero)
            {
                ChangeState("Idle");
                return;
            }
            
            // Run Logic (2x Speed)
            Vector2 velocity = Actor.Velocity;
            velocity.X = input.X * (Actor.Speed * 2.0f);
            velocity.Y = input.Y * (Actor.Speed * 2.0f);
            
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

