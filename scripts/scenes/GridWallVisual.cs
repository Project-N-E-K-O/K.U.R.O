using Godot;

namespace Kuros.Scenes;

[GlobalClass]
public partial class GridWallVisual : Resource
{
    public enum WallSide { North, East, South, West }
    public enum BoundaryKind { Interior = 0, Exterior = 1, FixedVoid = 2, Any = 3 }

    [Export] public WallSide Side { get; set; }
    [Export] public BoundaryKind Boundary { get; set; }
    [Export] public PackedScene? VisualScene { get; set; }
    /// <summary>使用多份场景实例拼接较长的墙段。</summary>
    [Export] public bool RepeatScene { get; set; }
    /// <summary>用于重复拼接的平面墙面素材，贴图左上角为局部原点。</summary>
    [Export] public Texture2D? TiledTexture { get; set; }
    [Export] public bool FlipHorizontally { get; set; }
    /// <summary>未指定场景时，使用墙段实际碰撞范围铺设纯色。</summary>
    [Export] public Color FillColor { get; set; } = Colors.Transparent;
    /// <summary>美术场景沿墙方向的原始长度，用于按原尺寸重复摆放。</summary>
    [Export(PropertyHint.Range, "1,10000,1,or_greater,suffix:px")]
    public float ReferenceLength { get; set; } = 3250f;
    /// <summary>素材的备用对齐点。内外墙场景可用 WallAnchor 标记中心。</summary>
    [Export] public Vector2 FootAnchor { get; set; }

    internal bool HasValidLength => float.IsFinite(ReferenceLength) && ReferenceLength > 0f;

    internal static GridWallVisual? Find(Godot.Collections.Array<GridWallVisual>? visuals,
        WallSide side, BoundaryKind boundary)
    {
        if (visuals == null) return null;
        GridWallVisual? fallback = null;
        foreach (var visual in visuals)
        {
            if (visual == null || visual.Side != side ||
                (visual.Boundary != boundary && visual.Boundary != BoundaryKind.Any)) continue;
            if (visual.FillColor.A <= 0f &&
                (!visual.HasValidLength || (visual.TiledTexture == null && visual.VisualScene?.CanInstantiate() != true))) continue;

            if (visual.Boundary == boundary) return visual;
            fallback ??= visual;
        }
        return fallback;
    }
}
