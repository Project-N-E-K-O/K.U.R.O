using Godot;
using Kuros.Systems.FSM;

namespace Kuros.Actors.Heroes.States;

/// <summary>地形强制状态：仅接受 Controller 已验证的请求，不受 Stun 免疫影响。</summary>
public partial class PlayerFallState : PlayerState, IPriorityInterruptState
{
    private PlayerFallController Controller => Player.GetNode<PlayerFallController>("FallController");
    public override bool CanEnterFrom(string? previous) => Controller.HasPendingFall && !Actor.IsDead && !Actor.IsDeathSequenceActive;
    public bool CanInterrupt(string? previous) => CanEnterFrom(previous);
    public override void Enter() => Controller.BeginFall();
    public override void Exit() => Controller.EndFall();
    public override bool CanExitTo(string next) => next == "Dead" || next == "Dying" || (Controller.Completing && next == "Idle");
    public override void PhysicsUpdate(double delta)
    {
        Actor.Velocity = Vector2.Zero;
        Controller.TickFall((float)delta);
    }
    public override void HandleInput(InputEvent @event) { }
}
