using System;
using System.Collections.Generic;
using Godot;
using Kuros.Systems.Stage;

namespace Kuros.UI;

public partial class StageTestMinimap : Control
{
    private static readonly Color UnvisitedColor = new("#103508");
    private static readonly Color VisitedColor = new("#279241");
    private static readonly Color PreviewColor = new("#20551c");
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
    private readonly Dictionary<Vector2I, Vector2I> _roomByCell = new();
    private readonly Dictionary<Vector2I, HashSet<Vector2I>> _neighborsByRoom = new();
    private HashSet<Vector2I>? _previewRooms;
    private IReadOnlyDictionary<Vector2I, (Rect2I Bounds, RoomDefinition? Definition)> _roomInfo =
        new Dictionary<Vector2I, (Rect2I, RoomDefinition?)>();
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

    public override void _Ready()
    {
        if (ResourceLoader.Load<Font>("res://assets/fonts/NotoSansSC-VF.ttf") is { } font)
            AddThemeFontOverride("font", font);
        SetProcess(false);
    }

    internal void Configure(Vector2I dimensions, Vector2I begin, Vector2I end, IReadOnlyCollection<Vector2I> rooms,
        IReadOnlyList<(Vector2 Start, Vector2 End, Vector2I FirstRoom, Vector2I SecondRoom)> walls,
        IReadOnlyDictionary<Vector2I, (Rect2I Bounds, RoomDefinition? Definition)> roomInfo,
        IReadOnlyCollection<(Vector2I First, Vector2I Second)> passages)
    {
        _dimensions = dimensions;
        _begin = begin;
        _end = end;
        _rooms = rooms;
        _walls = walls;
        _roomInfo = roomInfo;
        _visited.Clear();
        _roomByCell.Clear();
        _neighborsByRoom.Clear();

        foreach (var (anchor, (bounds, _)) in roomInfo)
        {
            _neighborsByRoom.Add(anchor, new HashSet<Vector2I>());
            for (int y = bounds.Position.Y; y < bounds.End.Y; y++)
            for (int x = bounds.Position.X; x < bounds.End.X; x++)
                _roomByCell.Add(new Vector2I(x, y), anchor);
        }

        foreach (var (first, second) in passages)
        {
            if (!_roomByCell.TryGetValue(first, out var firstRoom) ||
                !_roomByCell.TryGetValue(second, out var secondRoom) || firstRoom == secondRoom)
                continue;
            _neighborsByRoom[firstRoom].Add(secondRoom);
            _neighborsByRoom[secondRoom].Add(firstRoom);
        }

        _cellPixels = Mathf.Min(58f, Mathf.Min(420f / dimensions.X, 350f / dimensions.Y));
        OffsetLeft = OffsetRight - dimensions.X * _cellPixels;
        OffsetBottom = OffsetTop + dimensions.Y * _cellPixels;
        EnterRoom(roomInfo.TryGetValue(begin, out var entry) ? entry.Bounds : new Rect2I(begin, Vector2I.One));
    }

    internal void EnterRoom(Rect2I room)
    {
        for (int y = room.Position.Y; y < room.End.Y; y++)
        for (int x = room.Position.X; x < room.End.X; x++)
            _visited.Add(new Vector2I(x, y));
        _activeRoom = room;
        _previewRooms = _neighborsByRoom.GetValueOrDefault(room.Position);
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
        float lineWidth = Mathf.Clamp(_cellPixels * 0.065f, 1f, 4f);
        DrawRect(new Rect2(Vector2.Zero, (Vector2)_dimensions * _cellPixels),
            DebugFullMap ? DebugVoidColor : UnvisitedColor);
        if (!DebugFullMap && _previewRooms != null)
            foreach (var room in _previewRooms)
                if (!_visited.Contains(room))
                    DrawRect(ToMapRect(_roomInfo[room].Bounds), PreviewColor);
        if (!DebugFullMap && !_visited.Contains(_end))
            DrawRect(new Rect2((Vector2)_end * _cellPixels, size), EndpointColor);
        foreach (var cell in DebugFullMap ? _rooms : _visited)
            DrawRect(new Rect2((Vector2)cell * _cellPixels, size), GetCellColor(cell));

        foreach (var (start, end, firstRoom, secondRoom) in _walls)
        {
            if (ShowsWallSegments(firstRoom, secondRoom))
                DrawLine(start * _cellPixels, end * _cellPixels, BorderColor, lineWidth);
        }

        foreach (var (room, (bounds, definition)) in _roomInfo)
            if (DebugFullMap || _visited.Contains(room) || room == _begin || room == _end || _previewRooms?.Contains(room) == true)
                DrawRoomLabel(ToMapRect(bounds), definition, room);
    }

    private Rect2 ToMapRect(Rect2I bounds) =>
        new((Vector2)bounds.Position * _cellPixels, (Vector2)bounds.Size * _cellPixels);

    private bool ShowsWallSegments(Vector2I first, Vector2I second) =>
        DebugFullMap || _visited.Contains(first) || _visited.Contains(second);

    private void DrawRoomLabel(Rect2 bounds, RoomDefinition? definition, Vector2I room)
    {
        Vector2 center = bounds.GetCenter();
        if (definition?.ElevatorPreviewIcon is { } icon && icon.GetSize() is { X: > 0f, Y: > 0f } textureSize)
        {
            Vector2 iconSize = textureSize * (_cellPixels * 0.64f / Mathf.Max(textureSize.X, textureSize.Y));
            DrawTextureRect(icon, new Rect2(center - iconSize * 0.5f, iconSize), tile: false);
            return;
        }

        RoomType? type = definition?.RoomType;
        if (type == null)
        {
            if (room == _begin) type = RoomType.Begin;
            else if (room == _end) type = RoomType.End;
        }
        if (type == null) return;

        string typeName = StagePlan.DescribeRoomType(type.Value);
        Font font = GetThemeFont("font");
        int preferredSize = Mathf.Clamp(Mathf.FloorToInt(_cellPixels * 0.30f), 8, 16);
        float availableWidth = bounds.Size.X - Mathf.Max(4f, _cellPixels * 0.14f);
        var label = FitLabel(definition?.ElevatorPreviewName, Mathf.Max(8, preferredSize - 3))
            ?? FitLabel(typeName, Mathf.Max(8, preferredSize - 6));
        label ??= FitLabel(typeName[..1], 6);
        if (label is not { } fitted) return;

        var (text, fontSize, width) = fitted;
        float baseline = (font.GetAscent(fontSize) - font.GetDescent(fontSize)) * 0.5f;
        var position = new Vector2(center.X - width * 0.5f, center.Y + baseline);
        DrawString(font, position + Vector2.One, text, fontSize: fontSize, modulate: Colors.Black);
        DrawString(font, position, text, fontSize: fontSize, modulate: Colors.White);

        (string Text, int Size, float Width)? FitLabel(string? text, int minimumSize)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            for (int size = preferredSize; size >= minimumSize; size--)
            {
                float width = font.GetStringSize(text, fontSize: size).X;
                if (width <= availableWidth) return (text, size, width);
            }
            return null;
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
