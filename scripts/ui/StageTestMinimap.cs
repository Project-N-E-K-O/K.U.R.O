using System;
using System.Collections.Generic;
using Godot;

namespace Kuros.UI;

public partial class StageTestMinimap : Control
{
    private static readonly Color UnvisitedColor = new("#103508");
    private static readonly Color VisitedColor = new("#279241");
    private static readonly Color DebugRoomColor = new("#23664b");
    private static readonly Color DebugVoidColor = new("#3b3e40");
    private static readonly Color BorderColor = new("#a8df45");
    private static readonly Color EndpointColor = new("#ffe433");
    private static readonly Color ActiveColor = new("#f32931");
    private static readonly Color ActiveDimColor = new("#9c242a");
    private const float FlashHalfPeriod = 0.32f;

    [Export]
    public bool DebugFullMap
    {
        get => _debugFullMap;
        set
        {
            _debugFullMap = value;
            QueueRedraw();
        }
    }

    private bool _debugFullMap;
    private readonly HashSet<Vector2I> _visited = new();
    private IReadOnlyCollection<Vector2I> _rooms = Array.Empty<Vector2I>();
    private IReadOnlyList<(Vector2 Start, Vector2 End, Vector2I FirstRoom, Vector2I SecondRoom)> _walls =
        Array.Empty<(Vector2, Vector2, Vector2I, Vector2I)>();
    private Vector2I _dimensions;
    private Vector2I _begin;
    private Vector2I _end;
    private Rect2I _activeRoom;
    private float _cellPixels;
    private float _flashElapsed;
    private bool _bright = true;

    public override void _Ready() => SetProcess(false);

    internal void Configure(Vector2I dimensions, Vector2I begin, Vector2I end, IReadOnlyCollection<Vector2I> rooms,
        IReadOnlyList<(Vector2 Start, Vector2 End, Vector2I FirstRoom, Vector2I SecondRoom)> walls)
    {
        _dimensions = dimensions;
        _begin = begin;
        _end = end;
        _rooms = rooms;
        _walls = walls;
        _cellPixels = Mathf.Min(58f, Mathf.Min(420f / dimensions.X, 350f / dimensions.Y));
        OffsetLeft = OffsetRight - dimensions.X * _cellPixels;
        OffsetBottom = OffsetTop + dimensions.Y * _cellPixels;
        EnterRoom(new Rect2I(begin, Vector2I.One));
    }

    internal void EnterRoom(Rect2I room)
    {
        for (int y = room.Position.Y; y < room.End.Y; y++)
        for (int x = room.Position.X; x < room.End.X; x++)
            _visited.Add(new Vector2I(x, y));
        _activeRoom = room;
        _flashElapsed = 0f;
        _bright = true;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _flashElapsed += (float)delta;
        int passedIntervals = (int)(_flashElapsed / FlashHalfPeriod);
        if (passedIntervals == 0) return;
        _flashElapsed -= passedIntervals * FlashHalfPeriod;
        if (passedIntervals % 2 == 0) return;
        _bright = !_bright;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_dimensions.X == 0) return;

        var size = Vector2.One * _cellPixels;
        DrawRect(new Rect2(Vector2.Zero, (Vector2)_dimensions * _cellPixels),
            DebugFullMap ? DebugVoidColor : UnvisitedColor);
        if (!DebugFullMap && !_visited.Contains(_end))
            DrawRect(new Rect2((Vector2)_end * _cellPixels, size), EndpointColor);
        foreach (var cell in DebugFullMap ? _rooms : _visited)
            DrawRect(new Rect2((Vector2)cell * _cellPixels, size), GetCellColor(cell));

        float lineWidth = Mathf.Clamp(_cellPixels * 0.065f, 1f, 4f);
        foreach (var (start, end, firstRoom, secondRoom) in _walls)
        {
            if (DebugFullMap || _visited.Contains(firstRoom) || _visited.Contains(secondRoom))
                DrawLine(start * _cellPixels, end * _cellPixels, BorderColor, lineWidth);
        }
    }

    private Color GetCellColor(Vector2I cell)
    {
        if (_activeRoom.HasPoint(cell)) return _bright ? ActiveColor : ActiveDimColor;
        if (cell == _begin || cell == _end) return EndpointColor;
        if (_visited.Contains(cell)) return VisitedColor;
        return DebugFullMap ? DebugRoomColor : UnvisitedColor;
    }
}
