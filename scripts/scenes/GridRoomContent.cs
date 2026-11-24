using System.Collections.Generic;
using Godot;

namespace Kuros.Scenes;

public partial class GridRoomContent : Node2D
{
    // 网格测试实例会移除单独运行时使用的墙面、导航和战斗节点。
    [Export] public NodePath[] StandaloneNodes { get; set; } = System.Array.Empty<NodePath>();
    [Export] public Godot.Collections.Array<GridWallVisual> WallVisuals { get; set; } = new();

    internal void PrepareForGrid(Transform2D roomTransform, IReadOnlyList<Rect2> doorApproaches, uint playerMask)
    {
        foreach (NodePath path in StandaloneNodes)
            GetNodeOrNull(path)?.Free();

        var contentTransform = roomTransform * Transform;
        foreach (Node fixture in GetChildren())
        {
            if (BlocksDoorApproach(fixture, contentTransform, doorApproaches, playerMask))
                fixture.Free();
        }
    }

    private static bool BlocksDoorApproach(Node node, Transform2D transform, IReadOnlyList<Rect2> doorApproaches,
        uint playerMask)
    {
        if (node is Node2D spatial) transform *= spatial.Transform;
        if (node is CollisionShape2D { Disabled: false, Shape: { } shape } &&
            node.GetParent() is PhysicsBody2D body && (body.CollisionLayer & playerMask) != 0)
        {
            Rect2 bounds = transform * shape.GetRect();
            foreach (Rect2 approach in doorApproaches)
                if (approach.Intersects(bounds)) return true;
        }

        foreach (Node child in node.GetChildren())
            if (BlocksDoorApproach(child, transform, doorApproaches, playerMask)) return true;
        return false;
    }
}
