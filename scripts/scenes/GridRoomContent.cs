using Godot;

namespace Kuros.Scenes;

public partial class GridRoomContent : Node2D
{
    /// <summary>与房间样板匹配的占格尺寸，X 为横向，Y 为纵向。</summary>
    [Export] public Vector2I Footprint { get; set; } = Vector2I.One;

    // 移除由楼层生成器提供、仅用于房间独立运行的骨架节点。
    [Export] public NodePath[] StandaloneNodes { get; set; } = System.Array.Empty<NodePath>();
    [Export] public Godot.Collections.Array<GridWallVisual> WallVisuals { get; set; } = new();

    internal void PrepareForGrid()
    {
        foreach (NodePath path in StandaloneNodes)
            GetNodeOrNull(path)?.Free();
    }
}
