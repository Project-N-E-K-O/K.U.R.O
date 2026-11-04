using Godot;
using System.Collections.Generic;

namespace Kuros.Environments;

/// <summary>平台公共判定：脚底坐标、承托查询、身体形状、扫掠与每帧位移去重。</summary>
public static class PlatformCarryPhysics
{
    private static readonly NodePath DefaultGroundProbe = new("GroundProbe");

    /// <summary>角色使用约定的 GroundProbe，无该节点时退回根坐标。</summary>
    public static Vector2 CharacterGroundPosition(Node2D root) => GroundPosition(root, DefaultGroundProbe);

    /// <summary>优先显式脚底节点，其次身体形状原点，再次备用节点，最后根坐标。均返回世界坐标。</summary>
    public static Vector2 GroundPosition(Node2D root, NodePath probePath, PhysicsBody2D? body = null, Node2D? fallback = null)
    {
        if (!probePath.IsEmpty && root.GetNodeOrNull<Node2D>(probePath) is { } probe) return probe.GlobalPosition;
        return body != null ? BodyGroundPosition(body) : fallback?.GlobalPosition ?? root.GlobalPosition;
    }

    public static bool IsSupportEnabled(FallSupportArea? support)
        => GodotObject.IsInstanceValid(support) && support!.IsInsideTree()
            && !support.IsQueuedForDeletion() && support.SupportEnabled;

    /// <summary>一次脚底点查询，统一过滤无效或停用承托；不要求承托属于移动平台。</summary>
    public static IEnumerable<FallSupportArea> SupportsAt(World2D world, Vector2 foot)
    {
        var hits = FallPhysics.At(world, foot, FallSupportArea.Layer);
        foreach (var hit in hits)
            if (hit["collider"].AsGodotObject() is FallSupportArea support && IsSupportEnabled(support)) yield return support;
    }

    /// <summary>连续承载使用：当前位置是否位于指定平台的有效承托面。</summary>
    public static bool IsOnPlatform(World2D world, Vector2 foot, FallMovingPlatform platform)
    {
        if (!IsSupportEnabled(platform.SupportArea)) return false;
        foreach (var support in SupportsAt(world, foot))
            if (support == platform.SupportArea) return true;
        return false;
    }

    /// <summary>一次绑定使用：返回脚下可附着的平台；重叠时按实例 ID 确定性选择。</summary>
    public static FallMovingPlatform? FindPlatformAt(World2D world, Vector2 foot)
    {
        FallMovingPlatform? selected = null;
        foreach (var support in SupportsAt(world, foot))
        {
            var platform = PlatformForSupport(support);
            if (platform != null && (selected == null || platform.GetInstanceId() < selected.GetInstanceId())) selected = platform;
        }
        return selected;
    }

    public static FallMovingPlatform? PlatformForSupport(FallSupportArea support)
    {
        for (Node? node = support.GetParent(); node != null; node = node.GetParent())
            if (node is FallMovingPlatform platform)
                return platform.SupportArea == support && platform.CanAttachEffects ? platform : null;
        return null;
    }

    // ShapeOwner 只包含该 PhysicsBody 的实际身体形状，不包含子级 AttackArea / HitArea。
    public static bool HasEnabledBodyShape(PhysicsBody2D body)
    {
        foreach (uint owner in body.GetShapeOwners())
            if (!body.IsShapeOwnerDisabled(owner) && body.ShapeOwnerGetShapeCount(owner) > 0) return true;
        return false;
    }

    public static Vector2 BodyGroundPosition(PhysicsBody2D body)
    {
        foreach (uint owner in body.GetShapeOwners())
            if (!body.IsShapeOwnerDisabled(owner) && body.ShapeOwnerGetShapeCount(owner) > 0)
                return (body.GlobalTransform * body.ShapeOwnerGetTransform(owner)).Origin;
        return body.GlobalPosition;
    }

    /// <summary>静置家具没有可用 MoveAndCollide，使用它实际身体形状扫掠，阻挡时截短位移。</summary>
    public static Vector2 ClipMotion(PhysicsBody2D body, Vector2 motion, uint obstacleMask, Godot.Collections.Array<Rid> exclude)
    {
        float fraction = 1f;
        foreach (uint owner in body.GetShapeOwners())
        {
            if (body.IsShapeOwnerDisabled(owner)) continue;
            for (int i = 0; i < body.ShapeOwnerGetShapeCount(owner); i++)
            {
                var result = body.GetWorld2D().DirectSpaceState.CastMotion(new PhysicsShapeQueryParameters2D
                {
                    Shape = body.ShapeOwnerGetShape(owner, i),
                    Transform = body.GlobalTransform * body.ShapeOwnerGetTransform(owner),
                    Motion = motion, CollisionMask = obstacleMask, Exclude = exclude,
                    CollideWithAreas = false, CollideWithBodies = true, Margin = 0.001f
                });
                if (result.Length > 0) fraction = Mathf.Min(fraction, result[0]);
            }
        }
        return motion * fraction;
    }

    public static void CollectBodyRids(Node node, Godot.Collections.Array<Rid> result)
    {
        if (node is PhysicsBody2D body) result.Add(body.GetRid());
        foreach (Node child in node.GetChildren()) CollectBodyRids(child, result);
    }

    // 重叠平台在同一物理帧只能携带一个对象一次；每帧清空，不保留已释放对象引用。
    private static readonly HashSet<ulong> ClaimedPassengers = new();
    private static ulong _claimFrame = ulong.MaxValue;
    public static bool TryClaim(Node passenger)
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (_claimFrame != frame) { ClaimedPassengers.Clear(); _claimFrame = frame; }
        return ClaimedPassengers.Add(passenger.GetInstanceId());
    }
}
