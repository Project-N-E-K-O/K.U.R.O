using System.Collections.Generic;
using Godot;
using Kuros.Managers;
using Kuros.UI;
using WallBoundary = Kuros.Scenes.GridWallVisual.BoundaryKind;
using WallSide = Kuros.Scenes.GridWallVisual.WallSide;

namespace Kuros.Scenes;

public partial class StageTest : Node2D
{
    [Export(PropertyHint.Range, "3,31,2,or_greater")] public int Columns { get; set; } = 5;
    [Export(PropertyHint.Range, "1,31,1,or_greater")] public int Rows { get; set; } = 5;
    [Export] public PackedScene? RoomScene { get; set; }
    [Export] public Godot.Collections.Array<PackedScene> LargeRoomScenes { get; set; } = new();
    [Export] public Godot.Collections.Array<PackedScene> RoomContentScenes { get; set; } = new();
    [Export] public GridFloorWallSet? FloorWallSet { get; set; }
    [Export] public long LayoutSeed { get; set; }
    [Export] public Godot.Collections.Array<Vector2I> BlockedCells { get; set; } = new();
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int RandomBlockedMin { get; set; }
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int RandomBlockedMax { get; set; }
    /// <summary>从入口到出口最少经过的门洞数。0 表示使用网格本身的最短距离。</summary>
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int MinimumRouteLength { get; set; }
    /// <summary>随机留空时，优先尝试切断中间直线的概率。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float DetourChance { get; set; }

    // 坐标从左上角 (0, 0) 开始。每条共享边由西侧或北侧格登记一次。
    [Export] public Godot.Collections.Array<Vector2I> ClosedEastEdges { get; set; } = new();
    [Export] public Godot.Collections.Array<Vector2I> ClosedSouthEdges { get; set; } = new();

    private const float PlayerClearance = 1f;
    private const float HorizontalWallModuleLength = 1900f;
    private const float VerticalWallModuleLength = 1000f;
    private const float ExtraPassageChance = 0.25f;
    private const int BlockLayoutAttempts = 8;
    private const int PassageLayoutAttempts = 24;
    private static readonly Color WallColor = new("#627460");
    private static readonly Vector2I[] Directions = { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };

    private readonly HashSet<Vector2I> _rooms = new();
    private readonly HashSet<(Vector2I First, Vector2I Second)> _passages = new();
    private readonly List<Rect2> _wallRects = new();
    private readonly List<(Vector2 Start, Vector2 End, Vector2I FirstRoom, Vector2I SecondRoom)> _mapWalls = new();
    private readonly Dictionary<Vector2I, List<Rect2>> _doorApproaches = new();
    private readonly Dictionary<Vector2I, GridRoomContent> _contents = new();

    private CharacterBody2D _player = null!;
    private StaticBody2D _walls = null!;
    private Node2D _wallVisuals = null!;
    private Node2D _interiorWallVisuals = null!;
    private StageTestMinimap _minimap = null!;
    private CameraZoneManager _cameraZones = null!;
    private HashSet<Vector2I> _blocked = null!;
    private HashSet<Vector2I> _closedEast = null!;
    private HashSet<Vector2I> _closedSouth = null!;
    private Vector2 _cellSize;
    private readonly Dictionary<Vector2I, LargeRoomPlacement> _largeRoomsByCell = new();
    private Vector2 _beginDoorSpan;
    private Vector2 _endDoorSpan;
    private Rect2 _playerFootprint;
    private Vector2 MinimumFloorSize => _playerFootprint.Size + Vector2.One * (2f * PlayerClearance);
    private Vector2I _currentRoom;
    private Vector2I BeginCell => new(0, Rows / 2);
    private Vector2I EndCell => new(Columns - 1, Rows / 2);

    private readonly record struct RoomGeometry(Vector2 Size, Vector2 Center, Rect2 Floor,
        Vector2 BeginEastDoorSpan, Vector2 EndWestDoorSpan);
    private readonly record struct LargeRoomTemplate(PackedScene Scene, RoomGeometry Geometry, Vector2I Footprint);
    private readonly record struct LargeRoomPlacement(LargeRoomTemplate Template, Rect2I Bounds);
    private readonly record struct WallFace(Vector2 Inset, GridWallVisual? Visual, Vector2 VerticalArtRange = default);
    private readonly record struct WallModules(float Start, float Length, int Count)
    {
        public float BoundaryAt(int index) => Start + index * Length;

        public static WallModules CenteredIn(float from, float to, float length)
        {
            float available = Mathf.Max(0f, to - from);
            int count = Mathf.FloorToInt(available / length);
            return new WallModules(from + (available - count * length) * 0.5f, length, count);
        }
    }
    private readonly record struct WallEdge(
        (Vector2I First, Vector2I Second) Cells, WallFace First, WallFace Second, WallBoundary Boundary,
        WallModules? Modules);

    public override void _Ready()
    {
        _player = GetNode<CharacterBody2D>("World/MainCharacter");
        _walls = GetNode<StaticBody2D>("World/Walls");
        _wallVisuals = GetNode<Node2D>("World/WallVisuals");
        _interiorWallVisuals = GetNode<Node2D>("World/InteriorWallVisuals");
        _minimap = GetNode<StageTestMinimap>("HUD/Minimap");
        _cameraZones = GetNode<CameraZoneManager>("CameraZoneManager");
        if (Columns < 3 || Columns % 2 == 0 || Rows < 1)
        {
            StopTest("列数须为至少 3 的奇数，行数须大于 0。");
            return;
        }
        if (RandomBlockedMin < 0 || RandomBlockedMax < RandomBlockedMin)
        {
            StopTest("随机留空范围须满足 0 ≤ Random Blocked Min ≤ Random Blocked Max。");
            return;
        }

        var playerShape = _player.GetNode<CollisionShape2D>("CollisionShape2D");
        _playerFootprint = playerShape.Transform * playerShape.Shape.GetRect();
        string? roomError = ReadRoomTemplate(RoomScene, out var roomGeometry);
        if (roomError != null)
        {
            StopTest(roomError);
            return;
        }
        _cellSize = roomGeometry.Size;
        _beginDoorSpan = roomGeometry.BeginEastDoorSpan;
        _endDoorSpan = roomGeometry.EndWestDoorSpan;
        if (!FitsEndpointDoor(_beginDoorSpan, roomGeometry.Floor.Position.Y, roomGeometry.Floor.End.Y) ||
            !FitsEndpointDoor(_endDoorSpan, roomGeometry.Floor.Position.Y, roomGeometry.Floor.End.Y))
        {
            StopTest("Begin 东门或 End 西门的门位超出可通行区域，或宽度不足，请检查样板的 Endpoint Doors。");
            return;
        }
        var largeRooms = new List<LargeRoomTemplate>();
        foreach (var scene in LargeRoomScenes)
        {
            if (scene == null) continue;
            roomError = ReadRoomTemplate(scene, out var largeGeometry);
            if (roomError != null)
            {
                StopTest($"大房间样板 {scene.ResourcePath} 配置有误。{roomError}");
                return;
            }

            Vector2 footprint = largeGeometry.Size / _cellSize;
            var largeFootprint = (Vector2I)footprint.Round();
            if (largeFootprint.X < 1 || largeFootprint.Y < 1 || largeFootprint == Vector2I.One ||
                !footprint.IsEqualApprox((Vector2)largeFootprint))
            {
                StopTest("大房间的 AreaSize 须占用两个或更多完整基础格。");
                return;
            }
            largeRooms.Add(new LargeRoomTemplate(scene, largeGeometry, largeFootprint));
        }

        _blocked = new HashSet<Vector2I>(BlockedCells);
        _closedEast = new HashSet<Vector2I>(ClosedEastEdges);
        _closedSouth = new HashSet<Vector2I>(ClosedSouthEdges);
        Vector2I begin = BeginCell;
        Vector2I end = EndCell;
        using var random = new RandomNumberGenerator();
        if (LayoutSeed == 0)
            random.Randomize();
        else
            random.Seed = unchecked((ulong)LayoutSeed);

        if (!TryGenerateLayout(begin, end, random, out int placed, out int requestedBlocked)) return;
        foreach (var template in largeRooms)
            PlaceLargeRoom(begin, end, template, roomGeometry.Floor, random);
        ReportLayout(placed, requestedBlocked, begin, end);

        var roomInstances = BuildRooms(roomGeometry.Center);
        SelectRoomContents(roomInstances);
        BuildWalls(roomInstances, random);
        MountRoomContents(roomInstances);
        var beginFloor = roomInstances[begin].GetWalkableBounds(new Rect2((Vector2)begin * _cellSize, _cellSize));
        var clearance = Vector2.One * PlayerClearance;
        _player.Position = CellCenter(begin).Clamp(
            beginFloor.Position - _playerFootprint.Position + clearance,
            beginFloor.End - _playerFootprint.End - clearance);
        _player.ResetPhysicsInterpolation();
        _minimap.Configure(new Vector2I(Columns, Rows), begin, end, _rooms, _mapWalls);
        _currentRoom = begin;
        _cameraZones.SwitchToZone(GetZoneName(begin), zoomDuration: 0f);
        if (_cameraZones.TargetCamera is CameraFollow cameraFollow)
        {
            cameraFollow.SnapToTarget();
            cameraFollow.ResetPhysicsInterpolation();
        }
        _cameraZones.TargetCamera?.ForceUpdateScroll();
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 gridPosition = _player.Position / _cellSize;
        var cell = new Vector2I(Mathf.FloorToInt(gridPosition.X), Mathf.FloorToInt(gridPosition.Y));
        if (!_rooms.Contains(cell)) return;
        Vector2I room = GetRoomBounds(cell).Position;
        if (room == _currentRoom) return;
        _currentRoom = room;
        _cameraZones.SwitchToZone(GetZoneName(room), zoomDuration: 0f);
        _minimap.EnterRoom(GetRoomBounds(room));
    }

    public override void _Draw()
    {
        foreach (Rect2 wall in _wallRects)
            DrawRect(wall, WallColor);
    }

    private string? ReadRoomTemplate(PackedScene? scene, out RoomGeometry geometry)
    {
        geometry = default;
        var instance = scene?.Instantiate();

        try
        {
            if (instance is not GridRoomTemplate sample)
                return "房间样板根节点须挂载 GridRoomTemplate。";

            var area = sample.GetNodeOrNull<Area2D>("AreaSize");
            var shape = area?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            var cameraZone = sample.GetNodeOrNull<CameraZoneArea>("CameraZone");
            if (shape?.Shape is not RectangleShape2D rect || area == null ||
                rect.Size.X <= 0f || rect.Size.Y <= 0f ||
                cameraZone?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape is not RectangleShape2D)
                return "房间样板须含有效的矩形 AreaSize 和 CameraZone。";

            if (!sample.HasPositiveWallDepths)
                return "房间四边的墙深须大于 0。";

            Rect2 floor = sample.GetWalkableBounds(new Rect2(Vector2.Zero, rect.Size));
            Vector2 floorSize = floor.Size;
            if (!(floorSize.X >= MinimumFloorSize.X && floorSize.Y >= MinimumFloorSize.Y))
                return "房间墙深过大，剩余地板无法容纳玩家碰撞体。";

            geometry = new RoomGeometry(rect.Size, area.Position + shape.Position, floor,
                sample.BeginEastDoorSpan, sample.EndWestDoorSpan);
            return null;
        }
        finally
        {
            instance?.Free();
        }
    }

    private Vector2 CellCenter(Vector2I cell) => ((Vector2)cell + Vector2.One * 0.5f) * _cellSize;

    private Rect2I GetRoomBounds(Vector2I cell) =>
        _largeRoomsByCell.TryGetValue(cell, out var placement) ? placement.Bounds : new Rect2I(cell, Vector2I.One);

    private string GetZoneName(Vector2I cell) => $"StageTest_{GetInstanceId()}_R{cell.Y}_C{cell.X}";

    private Dictionary<Vector2I, GridRoomTemplate> BuildRooms(Vector2 roomCenter)
    {
        var container = GetNode<Node2D>("World/Rooms");
        var instances = new Dictionary<Vector2I, GridRoomTemplate>(_rooms.Count);
        foreach (var (cell, placement) in _largeRoomsByCell)
        {
            if (cell == placement.Bounds.Position)
                AddRoom(placement.Template.Scene, placement.Bounds, placement.Template.Geometry.Center);
        }

        foreach (Vector2I cell in _rooms)
        {
            if (!instances.ContainsKey(cell))
                AddRoom(RoomScene!, new Rect2I(cell, Vector2I.One), roomCenter);
        }
        return instances;

        void AddRoom(PackedScene scene, Rect2I bounds, Vector2 center)
        {
            Vector2I cell = bounds.Position;
            var room = scene.Instantiate<GridRoomTemplate>();
            room.Name = $"Room_R{cell.Y}_C{cell.X}";
            room.Position = ((Vector2)cell + (Vector2)bounds.Size * 0.5f) * _cellSize - center;
            var zone = room.GetNode<CameraZoneArea>("CameraZone");
            zone.ZoneName = GetZoneName(cell);
            // 以跨格为切换时机，包含 AreaSize 和 CameraZone 之间的接缝。
            zone.Monitoring = false;
            container.AddChild(room);
            _cameraZones.RegisterZone(zone.ZoneName, zone.ComputeWorldBounds(), zone.ZoomLevel);
            foreach (var occupied in CellsIn(bounds))
                instances.Add(occupied, room);
        }
    }

    private bool IsInsideGrid(Vector2I cell) => cell.X >= 0 && cell.X < Columns && cell.Y >= 0 && cell.Y < Rows;

    private void SelectRoomContents(Dictionary<Vector2I, GridRoomTemplate> rooms)
    {
        var contentPool = new List<PackedScene>();
        foreach (var scene in RoomContentScenes)
            if (scene != null) contentPool.Add(scene);
        if (contentPool.Count == 0) return;

        int next = 0;
        foreach (Vector2I cell in rooms.Keys)
        {
            if (cell == BeginCell || cell == EndCell || GetRoomBounds(cell).Size != Vector2I.One) continue;
            var instance = contentPool[next].Instantiate();
            next = (next + 1) % contentPool.Count;
            if (instance is not GridRoomContent content)
            {
                GD.PushWarning($"[Stage_test] 房间内容 {instance.SceneFilePath} 的根节点须挂载 GridRoomContent。");
                instance.Free();
                continue;
            }

            _contents.Add(cell, content);
        }
    }

    private void MountRoomContents(Dictionary<Vector2I, GridRoomTemplate> rooms)
    {
        foreach (var (cell, content) in _contents)
        {
            if (!_doorApproaches.TryGetValue(cell, out var approaches)) approaches = new List<Rect2>();
            GridRoomTemplate room = rooms[cell];
            Transform2D roomTransform = _walls.GlobalTransform.AffineInverse() * room.GlobalTransform;
            content.PrepareForGrid(roomTransform, approaches, _player.CollisionMask);
            room.AddChild(content);
        }
    }

    private bool IsAvailableCell(Vector2I cell) => IsInsideGrid(cell) && !_blocked.Contains(cell);

    private static (Vector2I First, Vector2I Second) SharedEdge(Vector2I cell, Vector2I neighbor) =>
        cell.X > neighbor.X || cell.Y > neighbor.Y ? (neighbor, cell) : (cell, neighbor);

    private bool IsForcedClosed(Vector2I first, Vector2I second) =>
        (first.Y == second.Y ? _closedEast : _closedSouth).Contains(first);

    private bool CanOpenEdge(Vector2I cell, Vector2I neighbor)
    {
        var (first, second) = SharedEdge(cell, neighbor);
        return IsAvailableCell(neighbor) && !IsForcedClosed(first, second);
    }

    private bool TryGenerateLayout(Vector2I begin, Vector2I end, RandomNumberGenerator random, out int placed,
        out int requestedBlocked)
    {
        placed = 0;
        requestedBlocked = 0;
        if (!IsAvailableCell(begin) || !IsAvailableCell(end))
        {
            StopTest("入口和 Boss／出口不能设为禁放格。");
            return false;
        }

        var beginExit = begin + Vector2I.Right;
        if (!CanOpenEdge(begin, beginExit))
        {
            StopTest($"Begin 房通过东侧接入地图，请保留东邻格 {beginExit} 并解除两房之间的封边。");
            return false;
        }

        var bossEntrance = end + Vector2I.Left;
        if (!CanOpenEdge(end, bossEntrance))
        {
            StopTest($"Boss 房只能从西侧进入，请保留西邻格 {bossEntrance} 并解除两房之间的封边。");
            return false;
        }

        var candidates = new List<Vector2I>();
        int roomCount = 2;
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Columns; x++)
        {
            var cell = new Vector2I(x, y);
            if (cell == begin || cell == end || !IsAvailableCell(cell)) continue;
            roomCount++;
            if (cell != beginExit && cell != bossEntrance) candidates.Add(cell);
        }

        var manualDistances = FindRoomDistances(beginExit, passagesOnly: false, excludeEndpoints: true);
        if (manualDistances.Count != roomCount - 2)
        {
            StopTest("绕开 Begin 和 Boss 房时，有普通房间无法从 Begin 东邻格到达，请检查手动禁放格和封闭边。");
            return false;
        }

        int directLength = end.X - begin.X;
        int requestedLength = Mathf.Max(MinimumRouteLength, directLength);
        // 四向网格中，每次绕路增加偶数步。
        int longestPossible = roomCount - 1 - (roomCount - 1 - directLength) % 2;
        if (requestedLength > longestPossible)
        {
            StopTest($"当前保留格最多只能支持 {longestPossible} 个门洞的最短路线，请降低 Minimum Route Length 或增加可用格。");
            return false;
        }
        int minLength = requestedLength + (requestedLength - directLength) % 2;
        requestedBlocked = RandomBlockedMin == RandomBlockedMax
            ? RandomBlockedMin
            : random.RandiRange(RandomBlockedMin, RandomBlockedMax);
        int targetBlocked = Mathf.Min(requestedBlocked, roomCount - (minLength + 1));
        bool needsLongerPath = minLength > directLength;
        bool canGoStraight = manualDistances[bossEntrance] + 2 == directLength;
        bool preferDetour = targetBlocked > 0 && canGoStraight &&
            DetourChance > 0f && random.Randf() < DetourChance;

        var fixedBlocked = new HashSet<Vector2I>(_blocked);
        int passageAttempts = needsLongerPath ? PassageLayoutAttempts : 1;
        int longestFound = 0;
        // 留空过多时分档减少随机空格，优先满足最短路线设置。
        for (int blockTarget = targetBlocked; ; blockTarget -= Mathf.Max(1, blockTarget / 4))
        {
            int blockAttempts = needsLongerPath && blockTarget > 0 ? BlockLayoutAttempts : 1;
            for (int attempt = 0; attempt < blockAttempts; attempt++)
            {
                _blocked.Clear();
                _blocked.UnionWith(fixedBlocked);
                placed = AddRandomBlockedCells(candidates, beginExit, roomCount, blockTarget, preferDetour, random);
                if (TryPlanPassages(begin, end, beginExit, bossEntrance, minLength, passageAttempts, random, ref longestFound))
                    return true;

                if (placed == 0) break;
            }

            if (_blocked.Count == fixedBlocked.Count) break;
        }

        StopTest($"本次生成未达到至少 {MinimumRouteLength} 个门洞的最短路线，尝试中最长为 {longestFound} 个门洞。");
        return false;
    }

    private int AddRandomBlockedCells(List<Vector2I> candidates, Vector2I beginExit, int roomCount,
        int target, bool preferDetour, RandomNumberGenerator random)
    {
        if (target == 0) return 0;

        var pending = new List<Vector2I>(candidates);
        int placed = 0;
        if (preferDetour)
        {
            var middle = pending.FindAll(cell => cell.Y == beginExit.Y);
            while (middle.Count > 0)
            {
                int pick = random.RandiRange(0, middle.Count - 1);
                if (TryBlockCell(middle[pick], beginExit, roomCount - 1))
                {
                    placed++;
                    break;
                }
                middle.RemoveAt(pick);
            }
        }

        for (int i = 0; i < pending.Count && placed < target; i++)
        {
            int pick = random.RandiRange(i, pending.Count - 1);
            (pending[i], pending[pick]) = (pending[pick], pending[i]);
            if (TryBlockCell(pending[i], beginExit, roomCount - placed - 1)) placed++;
        }
        return placed;
    }

    private bool TryBlockCell(Vector2I cell, Vector2I beginExit, int remainingRooms)
    {
        if (!_blocked.Add(cell)) return false;
        if (FindRoomDistances(beginExit, passagesOnly: false, excludeEndpoints: true).Count == remainingRooms - 2)
            return true;

        _blocked.Remove(cell);
        return false;
    }

    private Dictionary<Vector2I, int> FindRoomDistances(Vector2I begin, bool passagesOnly, bool excludeEndpoints = false)
    {
        var distances = new Dictionary<Vector2I, int> { [begin] = 0 };
        var pending = new Queue<Vector2I>();
        pending.Enqueue(begin);
        while (pending.TryDequeue(out Vector2I cell))
        {
            int nextDistance = distances[cell] + 1;
            foreach (Vector2I direction in Directions)
            {
                var next = cell + direction;
                if (excludeEndpoints && (next == BeginCell || next == EndCell)) continue;
                bool hasPassage = passagesOnly ? _passages.Contains(SharedEdge(cell, next)) : CanOpenEdge(cell, next);
                if (!hasPassage || !distances.TryAdd(next, nextDistance)) continue;
                pending.Enqueue(next);
                if (passagesOnly && _largeRoomsByCell.TryGetValue(next, out var placement))
                {
                    // 大房间内部可自由移动，所有占用格都只计一次进门。
                    foreach (var inside in CellsIn(placement.Bounds))
                        if (distances.TryAdd(inside, nextDistance)) pending.Enqueue(inside);
                }
            }
        }

        return distances;
    }

    private bool TryPlanPassages(Vector2I begin, Vector2I end, Vector2I beginExit, Vector2I bossEntrance, int minLength, int attempts,
        RandomNumberGenerator random, ref int longestFound)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            _rooms.Clear();
            _passages.Clear();
            GenerateConnectedRooms(beginExit, random);
            int length = FindRoomDistances(beginExit, passagesOnly: true)[bossEntrance] + 2;
            longestFound = Mathf.Max(longestFound, length);
            if (length < minLength) continue;

            _rooms.Add(begin);
            _passages.Add(SharedEdge(begin, beginExit));
            _rooms.Add(end);
            _passages.Add(SharedEdge(bossEntrance, end));
            AddExtraPassages(begin, end, minLength, random);
            return true;
        }

        return false;
    }

    private void AddExtraPassages(Vector2I begin, Vector2I end, int minLength, RandomNumberGenerator random)
    {
        foreach (var room in _rooms)
        {
            TryAddPassage(room, room + Vector2I.Right);
            TryAddPassage(room, room + Vector2I.Down);
        }

        void TryAddPassage(Vector2I first, Vector2I second)
        {
            var edge = (first, second);
            if (first == begin || second == begin || first == end || second == end || !CanOpenEdge(first, second) ||
                _passages.Contains(edge) || random.Randf() >= ExtraPassageChance)
                return;

            _passages.Add(edge);
            if (minLength > end.X - begin.X && FindRoomDistances(begin, passagesOnly: true)[end] < minLength)
                _passages.Remove(edge);
        }
    }

    private void ReportLayout(int placed, int requestedBlocked, Vector2I begin, Vector2I end)
    {
        int length = FindRoomDistances(begin, passagesOnly: true)[end];
        GD.Print($"[Stage_test] 入口到出口的最短路线经过 {length} 个门洞。随机留空目标 {requestedBlocked} 格，实际 {placed} 格。");
        if (placed < requestedBlocked)
            GD.PushWarning("[Stage_test] 受可用格、连通性和最短路线限制，实际随机留空少于目标。");
    }

    private void GenerateConnectedRooms(Vector2I beginExit, RandomNumberGenerator random)
    {
        var path = new Stack<Vector2I>();
        var candidates = new List<Vector2I>(Directions.Length);
        _rooms.Add(beginExit);
        path.Push(beginExit);
        while (path.TryPeek(out Vector2I cell))
        {
            candidates.Clear();
            foreach (Vector2I direction in Directions)
            {
                var next = cell + direction;
                if (next != BeginCell && next != EndCell && !_rooms.Contains(next) && CanOpenEdge(cell, next))
                    candidates.Add(next);
            }

            if (candidates.Count == 0)
            {
                path.Pop();
                continue;
            }

            Vector2I connected = candidates[random.RandiRange(0, candidates.Count - 1)];
            _rooms.Add(connected);
            _passages.Add(SharedEdge(cell, connected));
            path.Push(connected);
        }
    }

    private static IEnumerable<Vector2I> CellsIn(Rect2I bounds)
    {
        for (int y = bounds.Position.Y; y < bounds.End.Y; y++)
        for (int x = bounds.Position.X; x < bounds.End.X; x++)
            yield return new Vector2I(x, y);
    }

    private void PlaceLargeRoom(Vector2I begin, Vector2I end, LargeRoomTemplate template, Rect2 normalFloor,
        RandomNumberGenerator random)
    {
        var candidates = new List<Rect2I>();
        foreach (Vector2I cell in _rooms)
        {
            var bounds = new Rect2I(cell, template.Footprint);
            if (bounds.End.X > Columns || bounds.End.Y > Rows || bounds.HasPoint(begin) || bounds.HasPoint(end))
                continue;
            if (!CanUseLargeRoomBounds(bounds, normalFloor, template.Geometry.Floor)) continue;

            var candidate = new LargeRoomPlacement(template, bounds);
            RememberLargeRoom(candidate);
            if (FindRoomDistances(begin, passagesOnly: true)[end] >= MinimumRouteLength)
                candidates.Add(bounds);
            foreach (var occupied in CellsIn(bounds))
                _largeRoomsByCell.Remove(occupied);
        }

        if (candidates.Count == 0)
        {
            GD.PushWarning($"[Stage_test] 当前布局无法为 {template.Scene.ResourcePath} 留出符合门洞宽度和最短路线要求的位置。");
            return;
        }
        RememberLargeRoom(new LargeRoomPlacement(template, candidates[random.RandiRange(0, candidates.Count - 1)]));
    }

    private void RememberLargeRoom(LargeRoomPlacement placement)
    {
        foreach (var cell in CellsIn(placement.Bounds))
            _largeRoomsByCell.Add(cell, placement);
    }

    private bool CanUseLargeRoomBounds(Rect2I bounds, Rect2 normalFloor, Rect2 largeFloor)
    {
        foreach (var cell in CellsIn(bounds))
        {
            if (!_rooms.Contains(cell) || _largeRoomsByCell.ContainsKey(cell) ||
                (cell.X + 1 < bounds.End.X && IsForcedClosed(cell, cell + Vector2I.Right)) ||
                (cell.Y + 1 < bounds.End.Y && IsForcedClosed(cell, cell + Vector2I.Down)))
                return false;
        }

        largeFloor.Position += (Vector2)bounds.Position * _cellSize;
        foreach (var (first, second) in _passages)
        {
            bool firstInside = bounds.HasPoint(first);
            if (firstInside == bounds.HasPoint(second)) continue;

            Vector2 along = first.X == second.X ? Vector2.Right : Vector2.Down;
            Vector2 start = (Vector2)second * _cellSize;
            Vector2I otherCell = firstInside ? second : first;
            var otherFloor = normalFloor;
            if (_largeRoomsByCell.TryGetValue(otherCell, out var other))
            {
                otherCell = other.Bounds.Position;
                otherFloor = other.Template.Geometry.Floor;
            }
            otherFloor.Position += (Vector2)otherCell * _cellSize;
            var (from, to) = GetPassageRange(start, start + along * _cellSize, along, largeFloor, otherFloor);
            if (to - from < MinimumFloorSize.Dot(along)) return false;
            if (TryGetEndpointDoor(first, second, out Vector2 door) &&
                !FitsEndpointDoor(door, from - start.Y, to - start.Y))
                return false;
        }
        return true;
    }

    private bool FitsEndpointDoor(Vector2 span, float from, float to) =>
        span == Vector2.Zero || (span.X >= from && span.Y <= to && span.Y - span.X >= MinimumFloorSize.Y);

    private bool TryGetEndpointDoor(Vector2I first, Vector2I second, out Vector2 span)
    {
        if (first == BeginCell && second == first + Vector2I.Right)
            span = _beginDoorSpan;
        else if (second == EndCell && first == second + Vector2I.Left)
            span = _endDoorSpan;
        else
        {
            span = default;
            return false;
        }

        return true;
    }

    private static (float Start, float End) GetPassageRange(Vector2 start, Vector2 end, Vector2 along, Rect2 firstFloor,
        Rect2 secondFloor)
    {
        float from = Mathf.Max(start.Dot(along), Mathf.Max(firstFloor.Position.Dot(along), secondFloor.Position.Dot(along)));
        float to = Mathf.Min(end.Dot(along), Mathf.Min(firstFloor.End.Dot(along), secondFloor.End.Dot(along)));
        return (from, to);
    }

    private Rect2 GetRoomFloor(GridRoomTemplate template, Vector2I cell)
    {
        Rect2I bounds = GetRoomBounds(cell);
        return template.GetWalkableBounds(new Rect2((Vector2)bounds.Position * _cellSize, (Vector2)bounds.Size * _cellSize));
    }

    private void BuildWalls(Dictionary<Vector2I, GridRoomTemplate> rooms, RandomNumberGenerator random)
    {
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x <= Columns; x++)
        {
            var west = new Vector2I(x - 1, y);
            var east = new Vector2I(x, y);
            var start = new Vector2(x, y);
            AddEdge(start, start + Vector2.Down, (west, east), rooms, random);
        }

        for (int y = 0; y <= Rows; y++)
        for (int x = 0; x < Columns; x++)
        {
            var north = new Vector2I(x, y - 1);
            var south = new Vector2I(x, y);
            var start = new Vector2(x, y);
            AddEdge(start, start + Vector2.Right, (north, south), rooms, random);
        }
    }

    private void AddEdge(Vector2 start, Vector2 end, (Vector2I First, Vector2I Second) cells,
        Dictionary<Vector2I, GridRoomTemplate> rooms, RandomNumberGenerator random)
    {
        rooms.TryGetValue(cells.First, out var first);
        rooms.TryGetValue(cells.Second, out var second);
        if (first == second) return;

        bool vertical = start.X == end.X;
        Vector2 along = vertical ? Vector2.Down : Vector2.Right;
        Vector2 across = vertical ? Vector2.Right : Vector2.Down;
        float firstDepth = (vertical ? first?.EastWallDepth : first?.SouthWallDepth) ?? 0f;
        float secondDepth = (vertical ? second?.WestWallDepth : second?.NorthWallDepth) ?? 0f;
        Vector2 worldStart = start * _cellSize;
        Vector2 worldEnd = end * _cellSize;
        WallBoundary boundary = first != null && second != null
            ? WallBoundary.Interior
            : ClassifyUnoccupiedBoundary(first == null ? cells.First : cells.Second);
        var firstFace = new WallFace(-across * firstDepth,
            FindWallVisual(cells.First, vertical ? WallSide.East : WallSide.South, boundary));
        var secondFace = new WallFace(across * secondDepth,
            FindWallVisual(cells.Second, vertical ? WallSide.West : WallSide.North, boundary));

        bool hasPassage = first != null && second != null && _passages.Contains(cells);
        (float Start, float End) passageRange = default;
        WallModules? moduleLayout = null;
        if (first != null && second != null)
        {
            var firstFloor = GetRoomFloor(first, cells.First);
            var secondFloor = GetRoomFloor(second, cells.Second);
            passageRange = GetPassageRange(worldStart, worldEnd, along, firstFloor, secondFloor);
            if (vertical)
            {
                // 门洞由两间房共用；墙面各自避开本房间的南北墙脚。
                firstFace = firstFace with { VerticalArtRange = new Vector2(firstFloor.Position.Y, firstFloor.End.Y) };
                secondFace = secondFace with { VerticalArtRange = new Vector2(secondFloor.Position.Y, secondFloor.End.Y) };
                moduleLayout = WallModules.CenteredIn(passageRange.Start, passageRange.End, VerticalWallModuleLength);
            }
            else
                moduleLayout = WallModules.CenteredIn(worldStart.X, worldEnd.X, HorizontalWallModuleLength);
        }
        var edge = new WallEdge(cells, firstFace, secondFace, boundary, moduleLayout);

        if (!hasPassage)
        {
            AddWall(worldStart, worldEnd, edge);
            return;
        }

        var (gapFrom, gapTo) = passageRange;
        if (TryGetEndpointDoor(cells.First, cells.Second, out Vector2 door))
        {
            // 零向量使用相邻房间共同可通行的整段边界。
            if (door != Vector2.Zero)
            {
                gapFrom = worldStart.Y + door.X;
                gapTo = worldStart.Y + door.Y;
            }
        }
        else if (moduleLayout is { } modules)
        {
            float minimumWidth = MinimumFloorSize.Dot(along);
            var openings = new List<(float Start, float End)>();
            for (int from = 0; from < modules.Count; from++)
            for (int to = from + 1; to <= modules.Count; to++)
            {
                // 美术省略整个模块，碰撞开口在房间拐角处仍受共同可走范围约束。
                float openingStart = Mathf.Max(gapFrom, modules.BoundaryAt(from));
                float openingEnd = Mathf.Min(gapTo, modules.BoundaryAt(to));
                if (openingEnd - openingStart >= minimumWidth)
                    openings.Add((openingStart, openingEnd));
            }

            // 特殊尺寸容不下足够的整块模块时使用共同净宽，保留已生成的连通路线。
            if (openings.Count > 0)
                (gapFrom, gapTo) = openings[random.RandiRange(0, openings.Count - 1)];
        }
        Vector2 gapStart = vertical ? new Vector2(worldStart.X, gapFrom) : new Vector2(gapFrom, worldStart.Y);
        Vector2 gapEnd = vertical ? new Vector2(worldStart.X, gapTo) : new Vector2(gapTo, worldStart.Y);
        AddDoorApproach(cells.First, gapStart, gapEnd, edge.First.Inset);
        AddDoorApproach(cells.Second, gapStart, gapEnd, edge.Second.Inset);
        AddWall(worldStart, gapStart, edge);
        AddWall(gapEnd, worldEnd, edge);
    }

    private void AddDoorApproach(Vector2I cell, Vector2 start, Vector2 end, Vector2 inset)
    {
        if (!_contents.ContainsKey(cell)) return;
        var direction = inset.Normalized();
        // 从接缝穿过整段墙深，再留一个玩家身位用于进入房间。
        Vector2 depth = inset + direction * _playerFootprint.Size.Dot(direction.Abs());
        var bounds = new Rect2(start + depth.Min(Vector2.Zero), end - start + depth.Abs());
        if (!_doorApproaches.TryGetValue(cell, out var approaches))
        {
            approaches = new List<Rect2>();
            _doorApproaches.Add(cell, approaches);
        }
        approaches.Add(bounds);
    }

    private GridWallVisual? FindWallVisual(Vector2I cell, WallSide side, WallBoundary boundary)
    {
        _contents.TryGetValue(cell, out var content);
        return GridWallVisual.Find(content?.WallVisuals, side, boundary)
            ?? GridWallVisual.Find(FloorWallSet?.WallVisuals, side, boundary);
    }

    private WallBoundary ClassifyUnoccupiedBoundary(Vector2I neighbor)
    {
        if (!IsInsideGrid(neighbor)) return WallBoundary.Exterior;
        return BlockedCells.Contains(neighbor) ? WallBoundary.FixedVoid : WallBoundary.RandomVoid;
    }

    private void AddWall(Vector2 start, Vector2 end, WallEdge edge)
    {
        if (start.IsEqualApprox(end)) return;
        _mapWalls.Add((start / _cellSize, end / _cellSize, edge.Cells.First, edge.Cells.Second));
        var span = end - start;
        var bounds = new Rect2(start + edge.First.Inset, span + edge.Second.Inset - edge.First.Inset);
        var wall = new CollisionShape2D
        {
            Position = bounds.GetCenter(),
            Shape = new RectangleShape2D { Size = bounds.Size }
        };
        if (edge.Boundary == WallBoundary.FixedVoid) wall.SetMeta("borders_fixed_void", true);
        _walls.AddChild(wall);
        if (edge.Modules is { } modules)
        {
            Vector2 along = span.X == 0f ? Vector2.Down : Vector2.Right;
            AddInteriorWallFace(start, end, edge.Second, modules, along);
            AddInteriorWallFace(start, end, edge.First, modules, along);
            return;
        }
        AddWallFace(start, span, edge.First);
        AddWallFace(start, span, edge.Second);
    }

    private void AddInteriorWallFace(Vector2 start, Vector2 end, WallFace face, WallModules modules, Vector2 along)
    {
        float edgeStart = start.Dot(along);
        float visibleStart = edgeStart;
        float visibleEnd = end.Dot(along);
        if (along == Vector2.Down)
        {
            visibleStart = Mathf.Max(visibleStart, face.VerticalArtRange.X);
            visibleEnd = Mathf.Min(visibleEnd, face.VerticalArtRange.Y);
        }
        float capStart = visibleStart;
        for (int i = 0; i < modules.Count; i++)
        {
            float moduleStart = modules.BoundaryAt(i);
            float moduleEnd = modules.BoundaryAt(i + 1);
            if (moduleStart < visibleStart || moduleEnd > visibleEnd) continue;

            AddWallCap(start + along * (capStart - edgeStart), along * (moduleStart - capStart), face);
            AddWallFace(start + along * (moduleStart - edgeStart), along * modules.Length, face, centerOnModule: true);
            capStart = moduleEnd;
        }
        AddWallCap(start + along * (capStart - edgeStart), along * (visibleEnd - capStart), face);
    }

    private void AddWallCap(Vector2 start, Vector2 span, WallFace face)
    {
        if (span.X <= 0f && span.Y <= 0f) return;
        var bounds = new Rect2(start + face.Inset.Min(Vector2.Zero), span + face.Inset.Abs());
        AddWallFill(bounds, WallColor, "WallCap");
    }

    private void AddWallFace(Vector2 start, Vector2 span, WallFace face, bool centerOnModule = false)
    {
        if (face.Inset == Vector2.Zero) return;
        GridWallVisual? art = face.Visual;
        var bounds = new Rect2(start + face.Inset.Min(Vector2.Zero), span + face.Inset.Abs());
        Vector2 direction = span.X == 0f ? Vector2.Down : Vector2.Right;
        float length = span.Dot(direction);
        Vector2 mirror = new(art?.FlipHorizontally == true ? -1f : 1f, 1f);
        Vector2 anchor = centerOnModule ? bounds.GetCenter() : start + face.Inset;
        if (art?.VisualScene is { } scene && art.HasValidLength && scene.CanInstantiate())
        {
            var instance = scene.Instantiate();
            if (instance is Node2D visual)
            {
                int count = art.RepeatScene && !centerOnModule
                    ? Mathf.Max(1, Mathf.FloorToInt(length / art.ReferenceLength))
                    : 1;
                Vector2 step = direction * art.ReferenceLength;
                for (int i = 0; i < count; i++)
                {
                    var wall = i == 0 ? visual : scene.Instantiate<Node2D>();
                    Vector2 localAnchor = art.FootAnchor;
                    if (centerOnModule && wall.GetNodeOrNull<Marker2D>("WallAnchor") is { } marker)
                        localAnchor = wall.Transform * marker.Position;
                    AddWallArt(wall, anchor + step * i, localAnchor, mirror, centerOnModule);
                }
                return;
            }
            instance.Free();
        }
        if (art?.TiledTexture is { } texture)
        {
            float spacing = art.ReferenceLength;
            if (art.HasValidLength)
            {
                int count = centerOnModule ? 1 : Mathf.Max(1, Mathf.FloorToInt(length / spacing));
                Vector2 step = direction * spacing;
                for (int i = 0; i < count; i++)
                {
                    var tile = new Sprite2D
                    {
                        Name = "WallTile",
                        Texture = texture,
                        Centered = false
                    };
                    AddWallArt(tile, anchor + step * i, art.FootAnchor, mirror, centerOnModule);
                }
                return;
            }
        }
        if (art != null && art.FillColor.A > 0f)
        {
            AddWallFill(bounds, art.FillColor, "WallFill");
            return;
        }

        _wallRects.Add(bounds);
    }

    private void AddWallArt(Node2D visual, Vector2 anchor, Vector2 localAnchor, Vector2 mirror, bool interior)
    {
        // Z 层和排序原点由墙体场景根节点设置。这里仅应用摆放和镜像。
        var placement = new Transform2D(new Vector2(mirror.X, 0f), Vector2.Down, anchor);
        Transform2D visualTransform = visual.Transform;
        visualTransform.Origin -= localAnchor;
        visual.Transform = placement * visualTransform;
        var parent = interior ? _interiorWallVisuals : _wallVisuals;
        parent.AddChild(visual, forceReadableName: true);
    }

    private void AddWallFill(Rect2 bounds, Color color, string name)
    {
        _wallVisuals.AddChild(new ColorRect
        {
            Name = name,
            Position = bounds.Position,
            Size = bounds.Size,
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore
        }, forceReadableName: true);
    }

    private void StopTest(string message)
    {
        GD.PushError($"[Stage_test] {message}");
        _player.ProcessMode = ProcessModeEnum.Disabled;
        SetPhysicsProcess(false);
        GetNode<Label>("HUD/LayoutError").Text = message;
    }
}
