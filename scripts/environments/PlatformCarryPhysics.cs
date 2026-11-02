using Godot;
using System.Collections.Generic;

namespace Kuros.Environments;

public static class PlatformCarryPhysics
{
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
