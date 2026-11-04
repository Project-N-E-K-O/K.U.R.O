using Godot;
using Kuros.Fx;

namespace Kuros.Environments;

/// <summary>坠落公共空间查询和独立替身生成；不包含角色状态、伤害或掉落规则。</summary>
public static class FallPhysics
{
    public readonly record struct GroundResult(FallRegion? Activation, FallRegion? Hazard, bool Supported,
        FallSupportArea? Support = null, FallMovingPlatform? Platform = null);

    public static Godot.Collections.Array<Godot.Collections.Dictionary> At(World2D world, Vector2 point, uint mask)
        => world.DirectSpaceState.IntersectPoint(new PhysicsPointQueryParameters2D
        { Position = point, CollisionMask = mask, CollideWithAreas = true, CollideWithBodies = false }, 128);

    public static GroundResult Query(World2D world, Vector2 point, bool includeActivation = false)
    {
        FallRegion? activation = null, hazard = null;
        bool supported = false;
        FallSupportArea? selectedSupport = null;
        FallMovingPlatform? platform = null;
        uint mask = FallHazardArea.Layer | FallSupportArea.Layer | (includeActivation ? FallActivationArea.Layer : 0u);
        foreach (var hit in At(world, point, mask))
        {
            switch (hit["collider"].AsGodotObject())
            {
                case FallActivationArea yellow when yellow.Region is { } region:
                    if (activation == null || region.GetInstanceId() < activation.GetInstanceId()) activation = region;
                    break;
                case FallHazardArea red when red.Region is { } region:
                    if (hazard == null || region.GetInstanceId() < hazard.GetInstanceId()) hazard = region;
                    break;
                case FallSupportArea green when PlatformCarryPhysics.IsSupportEnabled(green):
                    supported = true;
                    if (selectedSupport == null || green.GetInstanceId() < selectedSupport.GetInstanceId()) selectedSupport = green;
                    var candidate = PlatformCarryPhysics.PlatformForSupport(green);
                    if (candidate != null && (platform == null || candidate.GetInstanceId() < platform.GetInstanceId())) platform = candidate;
                    break;
            }
        }
        return new(activation, hazard, supported, selectedSupport, platform);
    }

    public static Node2D? SpawnVisual(Node context, PackedScene? scene, string animation, Vector2 position,
        float duration, bool? facingRight = null, bool autoCleanup = true)
    {
        if (scene == null) return null;
        var instance = scene.Instantiate();
        if (instance is not Node2D visual)
        { instance.Free(); GD.PushWarning("[Fall] 坠落替身根节点必须是 Node2D。"); return null; }
        if (visual is DirectionalSceneEffect effect)
        { effect.AnimationName = animation; if (facingRight.HasValue) effect.FacingRight = facingRight.Value; }
        visual.TopLevel = true;
        visual.Position = position;
        visual.Visible = true;
        (context.GetTree().CurrentScene ?? context.GetTree().Root).AddChild(visual);
        visual.GlobalPosition = position;
        var animator = visual.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (animator != null && animator.HasAnimation(animation))
        { animator.SpeedScale = (float)animator.GetAnimation(animation).Length / Mathf.Max(0.1f, duration); animator.Play(animation); }
        if (autoCleanup)
        {
            var cleanup = visual.CreateTween();
            cleanup.TweenInterval(Mathf.Max(0.1f, duration));
            cleanup.TweenCallback(Callable.From(visual.QueueFree));
        }
        return visual;
    }

    public static void DisableCollisions(Node node, bool stopProcessing = false)
    {
        if (stopProcessing) node.ProcessMode = Node.ProcessModeEnum.Disabled;
        if (node is CollisionObject2D body) { body.CollisionLayer = 0; body.CollisionMask = 0; }
        if (stopProcessing && node is Area2D area) area.SetDeferred(Area2D.PropertyName.Monitoring, false);
        foreach (Node child in node.GetChildren()) DisableCollisions(child, stopProcessing);
    }
}
