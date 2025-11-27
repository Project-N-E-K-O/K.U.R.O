using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerRunState : PlayerState
    {
        public override void Enter()
        {
            Player.NotifyMovementState(Name);
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.Play("animations/run");
                var anim = Actor.AnimPlayer.GetAnimation("animations/run");
                if (anim != null) anim.LoopMode = Animation.LoopModeEnum.Linear;
            }
            // Increase speed by changing velocity calculation, not base stat
        }

        public override void PhysicsUpdate(double delta)
        {
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
            
            // Run Logic (1.5x Speed)
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

