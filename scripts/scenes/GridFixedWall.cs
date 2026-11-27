using Godot;

namespace Kuros.Scenes;

[GlobalClass]
public partial class GridFixedWall : Node2D
{
    [Export] public GridWallVisual.WallSide Side { get; set; }

    /// <summary>整块墙覆盖的每个格段都必须匹配，Any 接受所有边界。</summary>
    [Export] public GridWallVisual.BoundaryKind Boundary { get; set; } = GridWallVisual.BoundaryKind.Any;

    /// <summary>沿该侧从左到右或从上到下数的基础格段，从 0 开始。</summary>
    [Export(PropertyHint.Range, "0,31,1,or_greater")] public int FirstSegment { get; set; }

    /// <summary>同一块完整美术覆盖的格段数，0 表示从 FirstSegment 到该侧末端。</summary>
    [Export(PropertyHint.Range, "0,31,1,or_greater")] public int SegmentCount { get; set; }

    [Export] public bool ReplaceOppositeFace { get; set; }

    /// <summary>可选的背面美术。所有覆盖段的对面都有房间且未自带固定墙时显示。</summary>
    [Export] public Node2D? BackFace { get; set; }
}
