using Godot;

namespace Kuros.Scenes;

[Tool]
public partial class GridRoomTemplate : Node2D
{
    private float _northWallDepth = 650f;
    private float _southWallDepth = 125f;
    private float _westWallDepth = 125f;
    private float _eastWallDepth = 125f;

    [ExportGroup("Wall Depths")]
    [Export(PropertyHint.Range, "1,3250,1,or_greater,suffix:px")]
    public float NorthWallDepth
    {
        get => _northWallDepth;
        set { _northWallDepth = value; QueueRedraw(); }
    }

    [Export(PropertyHint.Range, "1,3250,1,or_greater,suffix:px")]
    public float SouthWallDepth
    {
        get => _southWallDepth;
        set { _southWallDepth = value; QueueRedraw(); }
    }

    [Export(PropertyHint.Range, "1,5750,1,or_greater,suffix:px")]
    public float WestWallDepth
    {
        get => _westWallDepth;
        set { _westWallDepth = value; QueueRedraw(); }
    }

    [Export(PropertyHint.Range, "1,5750,1,or_greater,suffix:px")]
    public float EastWallDepth
    {
        get => _eastWallDepth;
        set { _eastWallDepth = value; QueueRedraw(); }
    }

    [ExportGroup("Endpoint Doors")]
    /// <summary>Begin 东门的起止 Y，以房间北边界为原点，单位为像素。零向量开放两房共同可通行的整段。</summary>
    [Export] public Vector2 BeginEastDoorSpan { get; set; }
    /// <summary>End 西门的起止 Y，以房间北边界为原点，单位为像素。零向量开放两房共同可通行的整段。</summary>
    [Export] public Vector2 EndWestDoorSpan { get; set; }

    internal bool HasPositiveWallDepths =>
        NorthWallDepth > 0f && SouthWallDepth > 0f && WestWallDepth > 0f && EastWallDepth > 0f;

    internal Rect2 GetWalkableBounds(Rect2 roomBounds)
    {
        var topLeft = new Vector2(WestWallDepth, NorthWallDepth);
        var bottomRight = new Vector2(EastWallDepth, SouthWallDepth);
        return new Rect2(roomBounds.Position + topLeft, roomBounds.Size - topLeft - bottomRight);
    }

    public override void _Draw()
    {
        if (!Engine.IsEditorHint()) return;

        var area = GetNodeOrNull<Area2D>("AreaSize");
        var shape = area?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        if (area == null || shape?.Shape is not RectangleShape2D rect) return;

        var roomBounds = new Rect2(area.Position + shape.Position - rect.Size * 0.5f, rect.Size);
        DrawRect(roomBounds, new Color("#69a9d9"), filled: false, width: -1f);
        Rect2 walkable = GetWalkableBounds(roomBounds);
        if (walkable.HasArea())
            DrawRect(walkable, new Color("#edb26b"), filled: false, width: -1f);
    }
}
