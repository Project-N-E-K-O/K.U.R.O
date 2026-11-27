using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Managers;
using Kuros.Systems.Stage;
using Kuros.Systems.Cutscene;
using Kuros.UI;
using WallBoundary = Kuros.Scenes.GridWallVisual.BoundaryKind;
using WallSide = Kuros.Scenes.GridWallVisual.WallSide;

namespace Kuros.Scenes;

internal sealed class GridFloorGenerator
{
    private const float PlayerClearance = 1f;
    private const float HorizontalWallModuleLength = 1600f;
    private const float VerticalWallModuleLength = 1100f;
    private const float ExtraPassageChance = 0.25f;
    private const int BlockLayoutAttempts = 8;
    private const int PassageLayoutAttempts = 24;
    private static readonly Color WallColor = new("#627460");
    private static readonly Vector2I[] Directions = { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };

    private readonly HashSet<Vector2I> _rooms = new();
    private readonly HashSet<(Vector2I First, Vector2I Second)> _passages = new();
    private readonly List<Rect2> _solidWallBounds = new();
    private readonly Dictionary<(Vector2I Cell, Vector2I Side), Vector2> _interiorWallArtRanges = new();
    private readonly List<(Vector2 Start, Vector2 End, Vector2I FirstRoom, Vector2I SecondRoom)> _mapWalls = new();
    private readonly Dictionary<Vector2I, GridRoomContent> _contents = new();
    private readonly Dictionary<Vector2I, CameraZoneArea> _roomCameraAreas = new();
    private readonly HashSet<(Vector2I Cell, Vector2I Direction)> _fixedWallFaces = new();
    private readonly HashSet<(Vector2I Cell, Vector2I Direction)> _drawnExteriorSideWalls = new();

    private readonly GridFloorConfig _config;
    private readonly long _seed;
    private readonly List<RoomPlacement> _placements = new();
    private readonly Dictionary<Vector2I, RoomContentSelection> _plannedContents = new();
    private readonly List<Node> _mountedRoots = new();
    private ulong _wallRandomSeed;
    private ulong _wallRandomState;
    private int _placedBlocked;
    private int _requestedBlocked;
    private Node2D _world = null!;
    private Node2D _roomContainer = null!;
    private CharacterBody2D _player = null!;
    private StaticBody2D _walls = null!;
    private Node2D _wallVisuals = null!;
    private Node2D _debugWallFills = null!;
    private Node2D _interiorWallVisuals = null!;
    private StageTestMinimap _minimap = null!;
    private CameraZoneManager _cameraZones = null!;
    private CutsceneManager? _cutscenes;
    private bool _ownsMinimap;
    private HashSet<Vector2I> _blocked = null!;
    private HashSet<Vector2I> _closedEast = null!;
    private HashSet<Vector2I> _closedSouth = null!;
    private Vector2 _cellSize;
    private readonly Dictionary<Vector2I, LargeRoomPlacement> _largeRoomsByCell = new();
    private Vector2 _beginDoorSpan;
    private Vector2 _endDoorSpan;
    private PackedScene? _beginRoomScene;
    private PackedScene? _endRoomScene;
    private FixedRoomGeometry? _beginRoomGeometry;
    private FixedRoomGeometry? _endRoomGeometry;
    private Rect2 _playerFootprint;
    private Vector2 MinimumFloorSize => _playerFootprint.Size + Vector2.One * (2f * PlayerClearance);
    private Vector2I _currentRoom;
    private Vector2I BeginCell => new(0, _config.Rows / 2);
    private Vector2I EndCell => new(_config.Columns - 1, _config.Rows / 2);

    private readonly record struct RoomGeometry(Vector2 Size, Vector2 Center, Rect2 Floor,
        Vector2 BeginEastDoorSpan, Vector2 EndWestDoorSpan);
    private readonly record struct FixedRoomGeometry(Vector2 Anchor, Vector2 DoorSpan, float JoinDepth);
    private readonly record struct LargeRoomTemplate(PackedScene Scene, RoomGeometry Geometry, Vector2I Footprint);
    private readonly record struct LargeRoomPlacement(LargeRoomTemplate Template, Rect2I Bounds);
    private readonly record struct RoomPlacement(PackedScene Scene, Rect2I Bounds, Vector2 Center);
    private readonly record struct RoomContentSelection(PackedScene Scene, RoomDefinition? Definition);
    private enum RoomSeam { None, HorizontalWall, VerticalWall }
    private readonly record struct WallFace(Vector2 Inset, GridWallVisual? Visual, Vector2 ArtRange = default, bool Draw = true);
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

    internal IReadOnlyList<string> RoomPaths { get; private set; } = System.Array.Empty<string>();

    internal GridFloorGenerator(GridFloorConfig config, long seed)
    {
        _config = config;
        _seed = seed;
    }

    internal bool TryPlan(CharacterBody2D player, out string error)
    {
        error = "";
        if (_config.Columns < 3 || _config.Columns % 2 == 0 || _config.Rows < 1)
        {
            error = "列数须为至少 3 的奇数，行数须大于 0。";
            return false;
        }
        if (_config.RandomBlockedMin < 0 || _config.RandomBlockedMax < _config.RandomBlockedMin)
        {
            error = "随机留空范围须满足 0 ≤ Random Blocked Min ≤ Random Blocked Max。";
            return false;
        }

        if (player.GetNodeOrNull<CollisionShape2D>("CollisionShape2D") is not { Shape: { } collision } playerShape)
        {
            error = "网格楼层需要玩家的 CollisionShape2D。";
            return false;
        }
        _playerFootprint = playerShape.Transform * collision.GetRect();
        string? roomError = ReadRoomTemplate(_config.RoomScene, out var roomGeometry);
        if (roomError != null)
        {
            error = roomError;
            return false;
        }
        _cellSize = roomGeometry.Size;
        roomError = ResolveFixedRoom(_config.BeginRoomDefinition, _config.BeginRoomScene, begin: true,
                roomGeometry.Floor, out _beginRoomScene, out _beginRoomGeometry)
            ?? ResolveFixedRoom(_config.EndRoomDefinition, _config.EndRoomScene, begin: false,
                roomGeometry.Floor, out _endRoomScene, out _endRoomGeometry);
        if (roomError != null)
        {
            error = roomError;
            return false;
        }
        _beginDoorSpan = _beginRoomGeometry?.DoorSpan ?? roomGeometry.BeginEastDoorSpan;
        _endDoorSpan = _endRoomGeometry?.DoorSpan ?? roomGeometry.EndWestDoorSpan;
        if (!FitsEndpointDoor(_beginDoorSpan, roomGeometry.Floor.Position.Y, roomGeometry.Floor.End.Y) ||
            !FitsEndpointDoor(_endDoorSpan, roomGeometry.Floor.Position.Y, roomGeometry.Floor.End.Y))
        {
            error = "Begin 东门或 End 西门的门位超出可通行区域，或宽度不足，请检查样板的 Endpoint Doors。";
            return false;
        }
        var largeRooms = new List<LargeRoomTemplate>();
        foreach (var scene in _config.LargeRoomScenes)
        {
            if (scene == null) continue;
            roomError = ReadRoomTemplate(scene, out var largeGeometry);
            if (roomError != null)
            {
                error = $"大房间样板 {scene.ResourcePath} 配置有误。{roomError}";
                return false;
            }

            Vector2 footprint = largeGeometry.Size / _cellSize;
            var largeFootprint = (Vector2I)footprint.Round();
            if (largeFootprint.X < 1 || largeFootprint.Y < 1 || largeFootprint == Vector2I.One ||
                !footprint.IsEqualApprox((Vector2)largeFootprint))
            {
                error = "大房间的 AreaSize 须占用两个或更多完整基础格。";
                return false;
            }
            largeRooms.Add(new LargeRoomTemplate(scene, largeGeometry, largeFootprint));
        }

        _blocked = new HashSet<Vector2I>(_config.BlockedCells);
        _closedEast = new HashSet<Vector2I>(_config.ClosedEastEdges);
        _closedSouth = new HashSet<Vector2I>(_config.ClosedSouthEdges);
        Vector2I begin = BeginCell;
        Vector2I end = EndCell;
        using var random = new RandomNumberGenerator();
        if (_seed == 0)
            random.Randomize();
        else
            random.Seed = unchecked((ulong)_seed);

        if (!TryGenerateLayout(begin, end, random, out _placedBlocked, out _requestedBlocked, out error)) return false;
        foreach (var template in largeRooms)
            PlaceLargeRoom(begin, end, template, roomGeometry.Floor, random);

        foreach (var (cell, placement) in _largeRoomsByCell)
            if (cell == placement.Bounds.Position)
                _placements.Add(new RoomPlacement(placement.Template.Scene, placement.Bounds, placement.Template.Geometry.Center));

        foreach (Vector2I cell in _rooms)
            if (GetFixedRoomGeometry(cell) == null && !_largeRoomsByCell.ContainsKey(cell))
                _placements.Add(new RoomPlacement(_config.RoomScene!, new Rect2I(cell, Vector2I.One), roomGeometry.Center));

        SelectRoomContents();
        RoomPaths = _placements.Select(placement => placement.Scene)
            .Concat(_plannedContents.Values.Select(content => content.Scene))
            .Append(_beginRoomScene)
            .Append(_endRoomScene)
            .Select(scene => scene?.ResourcePath)
            .OfType<string>()
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct()
            .ToArray();
        // 墙体开口使用规划结束时的随机状态，提交候选方案时无需重新抽布局。
        _wallRandomSeed = random.Seed;
        _wallRandomState = random.State;
        return true;
    }

    internal void Mount(Node2D world, CharacterBody2D player, CameraZoneManager cameraZones,
        Vector2? landingPosition = null, StageTestMinimap? minimap = null)
    {
        _world = world;
        _player = player;
        _cameraZones = cameraZones;
        _cutscenes = world.GetParent().GetNodeOrNull<CutsceneManager>("CutsceneManager");
        _ownsMinimap = minimap == null;
        BindContainers(minimap);

        Vector2I begin = BeginCell;
        Vector2I end = EndCell;
        ReportLayout(_placedBlocked, _requestedBlocked, begin, end);

        using var random = new RandomNumberGenerator { Seed = _wallRandomSeed, State = _wallRandomState };
        var roomInstances = BuildRooms();
        foreach (var (cell, selection) in _plannedContents)
        {
            var content = selection.Scene.Instantiate<GridRoomContent>();
            content.PrepareForGrid();
            _contents.Add(cell, content);
        }
        Node2D? beginRoom = null;
        Node2D? endRoom = null;
        if (_beginRoomGeometry is { } beginGeometry)
            beginRoom = MountFixedRoom(_beginRoomScene!, begin, beginGeometry, "CBegin", eastDoor: true);
        if (_endRoomGeometry is { } endGeometry)
            endRoom = MountFixedRoom(_endRoomScene!, end, endGeometry, "CEnd", eastDoor: false);
        ConfigureFixedWalls(roomInstances, beginRoom, endRoom);
        BuildWalls(roomInstances, random);
        BuildWallCornerAnchors(roomInstances);
        MountRoomContents(roomInstances);

        _player.GetNodeOrNull<Kuros.Actors.Heroes.PlayerFallController>("FallController")?.ResetSafeHistory();
        if (landingPosition is { } target)
            _player.GlobalPosition = target;
        else if (beginRoom != null)
            _player.GlobalPosition = beginRoom.GetNode<Node2D>("PlayerSpawnPoint").GlobalPosition;
        else
        {
            var beginFloor = GetRoomFloor(roomInstances[begin], begin);
            var clearance = Vector2.One * PlayerClearance;
            _player.Position = CellCenter(begin).Clamp(
                beginFloor.Position - _playerFootprint.Position + clearance,
                beginFloor.End - _playerFootprint.End - clearance);
        }
        _player.ForceUpdateTransform();
        _player.ResetPhysicsInterpolation();
        _world.GetNodeOrNull<Kuros.Companions.P2CompanionController>("P2")?.SnapToPlayer();
        _minimap.Configure(new Vector2I(_config.Columns, _config.Rows), begin, end, _rooms, _mapWalls,
            GetMinimapRooms(), _passages);
        if (_ownsMinimap)
            _minimap.PivotOffset = new Vector2(_minimap.OffsetRight - _minimap.OffsetLeft, 0f);
        _currentRoom = begin;
        SwitchRoomCamera(begin);
        if (landingPosition.HasValue) UpdatePlayerRoom();
        if (_cameraZones.TargetCamera is CameraFollow cameraFollow)
        {
            cameraFollow.SnapToTarget();
            cameraFollow.ResetPhysicsInterpolation();
        }
        _cameraZones.TargetCamera?.ForceUpdateScroll();
    }

    internal void UpdatePlayerRoom()
    {
        bool cutscenePlaying = _cutscenes?.IsPlaying == true;
        if (_ownsMinimap) _minimap.Visible = !cutscenePlaying;
        if (cutscenePlaying) return;

        Vector2 gridPosition = _player.Position / _cellSize;
        var cell = new Vector2I(Mathf.FloorToInt(gridPosition.X), Mathf.FloorToInt(gridPosition.Y));
        if (!_rooms.Contains(cell))
        {
            // End 东侧电梯在格网外。回到格内时恢复本房间的相机区。
            if (_endRoomGeometry.HasValue && _currentRoom == EndCell && cell.X >= _config.Columns)
                _currentRoom = new Vector2I(-1, -1);
            return;
        }
        Vector2I room = GetRoomBounds(cell).Position;
        if (room == _currentRoom) return;
        _currentRoom = room;
        SwitchRoomCamera(room);
        _minimap.EnterRoom(GetRoomBounds(room));
    }

    internal void Clear()
    {
        if (GodotObject.IsInstanceValid(_cameraZones))
        {
            foreach (var zone in _roomCameraAreas.Values)
                _cameraZones.UnregisterZone(zone.ZoneName);
            _cameraZones.UnregisterZone("Stage_Global");
        }

        foreach (var content in _contents.Values)
            if (GodotObject.IsInstanceValid(content) && content.GetParent() == null)
                content.Free();

        foreach (Node node in _mountedRoots)
            if (GodotObject.IsInstanceValid(node))
                node.Free();
        _mountedRoots.Clear();
        _roomCameraAreas.Clear();
        _contents.Clear();
        _mapWalls.Clear();
        _solidWallBounds.Clear();
        _fixedWallFaces.Clear();
        _drawnExteriorSideWalls.Clear();
        _interiorWallArtRanges.Clear();
    }

    private void BindContainers(StageTestMinimap? minimap)
    {
        if (minimap != null)
        {
            _roomContainer = _world.GetNode<Node2D>("Rooms");
            _walls = _world.GetNode<StaticBody2D>("Walls");
            _wallVisuals = _world.GetNode<Node2D>("WallVisuals");
            _interiorWallVisuals = _world.GetNode<Node2D>("InteriorWallVisuals");
            _debugWallFills = _wallVisuals.GetNode<Node2D>("DebugWallFills");
            _minimap = minimap;
            return;
        }

        _roomContainer = AddRoot(_world, new Node2D { Name = "Rooms", YSortEnabled = true });
        _walls = AddRoot(_world, new StaticBody2D { Name = "Walls", CollisionMask = 0 });
        _wallVisuals = AddRoot(_world, new Node2D { Name = "WallVisuals", YSortEnabled = true });
        _interiorWallVisuals = AddRoot(_world, new Node2D { Name = "InteriorWallVisuals", YSortEnabled = true });
        _debugWallFills = new Node2D { Name = "DebugWallFills", Visible = false };
        _wallVisuals.AddChild(_debugWallFills);

        var hud = AddRoot(_world.GetParent(), new CanvasLayer { Name = "GridFloorHUD", Layer = 20 });
        _minimap = new StageTestMinimap
        {
            Name = "Minimap",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Scale = new Vector2(0.65f, 0.65f),
            GrowHorizontal = Control.GrowDirection.Begin
        };
        hud.AddChild(_minimap);
        _minimap.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _minimap.OffsetRight = -18f;
        _minimap.OffsetTop = 18f;
    }

    private T AddRoot<T>(Node parent, T node) where T : Node
    {
        parent.AddChild(node);
        _mountedRoots.Add(node);
        return node;
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

    private string? ResolveFixedRoom(RoomDefinition? definition, PackedScene? fallback, bool begin, Rect2 floor,
        out PackedScene? scene, out FixedRoomGeometry? geometry)
    {
        scene = definition == null ? fallback : LoadDefinitionScene(definition);
        geometry = null;
        if (definition != null && scene == null)
            return $"固定房 {(begin ? "Begin" : "End")} 的 RoomDefinition 场景无效：{definition.ScenePath}";
        return ReadFixedRoom(scene, floor, begin, out geometry);
    }

    private string? ReadFixedRoom(PackedScene? scene, Rect2 floor, bool begin, out FixedRoomGeometry? geometry)
    {
        geometry = null;
        if (scene == null) return null;

        Node instance = scene.Instantiate();
        try
        {
            if (instance is not Node2D room ||
                room.GetNodeOrNull<Marker2D>("GridConnection") is not { } anchor ||
                anchor.GetNodeOrNull<Marker2D>("Top") is not { } top ||
                anchor.GetNodeOrNull<Marker2D>("Bottom") is not { } bottom ||
                room.GetNodeOrNull<CameraZoneArea>("CameraZone")?
                    .GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape is not RectangleShape2D ||
                (begin && room.GetNodeOrNull<Node2D>("PlayerSpawnPoint") == null))
                return $"固定房 {scene.ResourcePath} 缺少 GridConnection/Top、Bottom、CameraZone 或入口出生点。";

            // 锚点对齐格边中点。门洞为固定房开口与普通房可通行范围的交集。
            Vector2 span = new(Mathf.Max(floor.Position.Y, _cellSize.Y * 0.5f + top.Position.Y),
                Mathf.Min(floor.End.Y, _cellSize.Y * 0.5f + bottom.Position.Y));
            if (!FitsEndpointDoor(span, floor.Position.Y, floor.End.Y))
                return $"固定房 {scene.ResourcePath} 的门洞与相邻格没有足够的共同通行宽度。";

            float joinDepth = begin ? _cellSize.X - floor.End.X : floor.Position.X;
            geometry = new FixedRoomGeometry(anchor.Position, span, joinDepth);
            return null;
        }
        finally
        {
            instance.Free();
        }
    }

    private Node2D MountFixedRoom(PackedScene scene, Vector2I cell, FixedRoomGeometry geometry, string name, bool eastDoor)
    {
        var room = scene.Instantiate<Node2D>();
        // 过场资源从 CutsceneManager 定位 World/CBegin 与 World/CEnd。
        room.Name = name;
        room.Position = CellCenter(cell) + Vector2.Right * (_cellSize.X * (eastDoor ? 0.5f : -0.5f)) - geometry.Anchor;
        var zone = room.GetNode<CameraZoneArea>("CameraZone");
        zone.UseLocalVerticalLimits = true;
        zone.ZoneName = GetZoneName(cell);
        zone.Monitoring = false;
        AddRoot(_world, room);
        RegisterRoomCamera(cell, zone);
        return room;
    }

    private FixedRoomGeometry? GetFixedRoomGeometry(Vector2I cell) =>
        cell == BeginCell ? _beginRoomGeometry : cell == EndCell ? _endRoomGeometry : null;

    private Rect2I GetRoomBounds(Vector2I cell) =>
        _largeRoomsByCell.TryGetValue(cell, out var placement) ? placement.Bounds : new Rect2I(cell, Vector2I.One);

    private Dictionary<Vector2I, (Rect2I Bounds, RoomDefinition? Definition)> GetMinimapRooms()
    {
        var rooms = new Dictionary<Vector2I, (Rect2I Bounds, RoomDefinition? Definition)>();
        foreach (var placement in _placements)
        {
            Vector2I cell = placement.Bounds.Position;
            rooms[cell] = (placement.Bounds, _plannedContents.GetValueOrDefault(cell).Definition);
        }
        rooms[BeginCell] = (GetRoomBounds(BeginCell), _config.BeginRoomDefinition);
        rooms[EndCell] = (GetRoomBounds(EndCell), _config.EndRoomDefinition);
        return rooms;
    }

    private string GetZoneName(Vector2I cell) => $"GridFloor_{_roomContainer.GetInstanceId()}_R{cell.Y}_C{cell.X}";

    private void RegisterRoomCamera(Vector2I cell, CameraZoneArea zone)
    {
        _roomCameraAreas.Add(cell, zone);
        _cameraZones.RegisterZone(zone.ZoneName, zone.ComputeWorldBounds(), zone.ZoomLevel);
    }

    private void SwitchRoomCamera(Vector2I cell)
    {
        // 网格房停用了区域监测，过场和电梯退出时使用当前格的相机边界作为回退。
        var zone = _roomCameraAreas[cell];
        _cameraZones.RegisterZone("Stage_Global", zone.ComputeWorldBounds(), zone.ZoomLevel);
        _cameraZones.SwitchToZone(zone.ZoneName, zoomDuration: 0f);
    }

    private Dictionary<Vector2I, GridRoomTemplate> BuildRooms()
    {
        var instances = new Dictionary<Vector2I, GridRoomTemplate>(_rooms.Count);
        foreach (var (scene, bounds, center) in _placements)
        {
            Vector2I cell = bounds.Position;
            var room = scene.Instantiate<GridRoomTemplate>();
            room.Name = $"Room_R{cell.Y}_C{cell.X}";
            room.Position = ((Vector2)cell + (Vector2)bounds.Size * 0.5f) * _cellSize - center;
            var zone = room.GetNode<CameraZoneArea>("CameraZone");
            zone.ZoneName = GetZoneName(cell);
            // 以跨格为切换时机，包含 AreaSize 和 CameraZone 之间的接缝。
            zone.Monitoring = false;
            _roomContainer.AddChild(room);
            RegisterRoomCamera(cell, zone);
            foreach (var occupied in CellsIn(bounds))
                instances.Add(occupied, room);
        }

        return instances;
    }

    private bool IsInsideGrid(Vector2I cell) => cell.X >= 0 && cell.X < _config.Columns && cell.Y >= 0 && cell.Y < _config.Rows;

    private void SelectRoomContents()
    {
        var contentPools = new Dictionary<Vector2I, Queue<RoomContentSelection>>();
        foreach (var selection in GetContentCandidates())
        {
            var instance = selection.Scene.Instantiate();
            if (instance is GridRoomContent content && content.Footprint.X > 0 && content.Footprint.Y > 0)
            {
                if (!contentPools.TryGetValue(content.Footprint, out var pool))
                    contentPools.Add(content.Footprint, pool = new Queue<RoomContentSelection>());
                pool.Enqueue(selection);
            }
            else
                GD.PushWarning($"[GridFloorGenerator] 房间内容 {selection.Scene.ResourcePath} 须使用 GridRoomContent 并设置正数占格尺寸。");
            instance.Free();
        }

        foreach (var placement in _placements)
        {
            Rect2I bounds = placement.Bounds;
            Vector2I cell = bounds.Position;
            if (cell == BeginCell || cell == EndCell || !contentPools.TryGetValue(bounds.Size, out var pool))
                continue;

            var selection = pool.Dequeue();
            pool.Enqueue(selection);
            _plannedContents.Add(cell, selection);
        }
    }

    private IEnumerable<RoomContentSelection> GetContentCandidates()
    {
        if (_config.RoomCatalog is { } catalog)
        {
            foreach (var definition in catalog.Rooms)
            {
                if (definition == null) continue;
                if (LoadDefinitionScene(definition) is { } scene)
                    yield return new RoomContentSelection(scene, definition);
                else
                    GD.PushWarning($"[GridFloorGenerator] 房间定义的场景无效：{definition.ScenePath}");
            }
            yield break;
        }

        foreach (var scene in _config.RoomContentScenes)
            if (scene != null)
                yield return new RoomContentSelection(scene, null);
    }

    private static PackedScene? LoadDefinitionScene(RoomDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.ScenePath) && ResourceLoader.Exists(definition.ScenePath, "PackedScene")
            ? ResourceLoader.Load<PackedScene>(definition.ScenePath)
            : null;

    private void MountRoomContents(Dictionary<Vector2I, GridRoomTemplate> rooms)
    {
        foreach (var (cell, content) in _contents)
            rooms[cell].AddChild(content);
    }

    private void ConfigureFixedWalls(Dictionary<Vector2I, GridRoomTemplate> rooms, Node2D? beginRoom, Node2D? endRoom)
    {
        var walls = new List<(GridFixedWall Wall, Vector2I Direction, List<Vector2I> Cells)>();
        // 内容场景可以在相同格段提供比房间骨架更具体的固定美术。
        foreach (var (cell, content) in _contents) Collect(content, GetRoomBounds(cell));
        foreach (var (cell, room) in rooms)
        {
            var bounds = GetRoomBounds(cell);
            if (cell == bounds.Position) Collect(room, bounds);
        }
        if (beginRoom != null) Collect(beginRoom, GetRoomBounds(BeginCell));
        if (endRoom != null) Collect(endRoom, GetRoomBounds(EndCell));

        // 先确认所有房间自己的墙面，再处理对侧背面。邻居的独立墙面优先于背面素材。
        foreach (var (wall, direction, cells) in walls)
        {
            if (wall.BackFace is not { } backFace) continue;
            backFace.Visible = wall.ReplaceOppositeFace && cells.TrueForAll(cell =>
                _rooms.Contains(cell + direction) && !_fixedWallFaces.Contains((cell + direction, -direction)));
        }

        foreach (var (wall, direction, cells) in walls)
            if (wall.ReplaceOppositeFace)
                foreach (var cell in cells)
                    _fixedWallFaces.Add((cell + direction, -direction));

        void Collect(Node2D room, Rect2I bounds)
        {
            if (room.GetNodeOrNull<Node2D>("FixedWalls") is not { Visible: true } container) return;
            foreach (var child in container.GetChildren())
            {
                if (child is not GridFixedWall wall || !wall.Visible) continue;
                Vector2I direction = wall.Side switch
                {
                    WallSide.North => Vector2I.Up,
                    WallSide.East => Vector2I.Right,
                    WallSide.South => Vector2I.Down,
                    _ => Vector2I.Left
                };
                bool horizontal = direction.X == 0;
                int length = horizontal ? bounds.Size.X : bounds.Size.Y;
                int count = wall.SegmentCount == 0 ? length - wall.FirstSegment : wall.SegmentCount;
                if (wall.FirstSegment < 0 || wall.FirstSegment >= length || count <= 0 || count > length - wall.FirstSegment)
                {
                    wall.Hide();
                    GD.PushWarning($"[Stage_test] {room.Name}/{wall.Name} 的固定墙覆盖范围超出了房间边界。");
                    continue;
                }

                Vector2I firstCell = bounds.Position + new Vector2I(
                    direction == Vector2I.Right ? bounds.Size.X - 1 : 0,
                    direction == Vector2I.Down ? bounds.Size.Y - 1 : 0);
                Vector2I along = horizontal ? Vector2I.Right : Vector2I.Down;
                var cells = new List<Vector2I>(count);
                for (int i = 0; i < count; i++)
                    cells.Add(firstCell + along * (wall.FirstSegment + i));

                if (wall.Boundary != WallBoundary.Any &&
                    cells.Exists(cell => ClassifyWallBoundary(cell + direction) != wall.Boundary))
                {
                    wall.Hide();
                    continue;
                }

                // 固定美术整块使用。覆盖范围有门洞时把这块交还给常规墙面生成。
                if (cells.Exists(cell => _passages.Contains(SharedEdge(cell, cell + direction))))
                {
                    wall.Hide();
                    continue;
                }
                if (cells.Exists(cell => _fixedWallFaces.Contains((cell, direction))))
                {
                    wall.Hide();
                    GD.PushWarning($"[Stage_test] {room.Name}/{wall.Name} 与同一面上的固定墙重复覆盖。");
                    continue;
                }

                walls.Add((wall, direction, cells));
                foreach (var cell in cells)
                    _fixedWallFaces.Add((cell, direction));
            }
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
        out int requestedBlocked, out string error)
    {
        error = "";
        placed = 0;
        requestedBlocked = 0;
        if (!IsAvailableCell(begin) || !IsAvailableCell(end))
        {
            error = "入口和 Boss／出口不能设为禁放格。";
            return false;
        }

        var beginExit = begin + Vector2I.Right;
        if (!CanOpenEdge(begin, beginExit))
        {
            error = $"Begin 房通过东侧接入地图，请保留东邻格 {beginExit} 并解除两房之间的封边。";
            return false;
        }

        var bossEntrance = end + Vector2I.Left;
        if (!CanOpenEdge(end, bossEntrance))
        {
            error = $"Boss 房只能从西侧进入，请保留西邻格 {bossEntrance} 并解除两房之间的封边。";
            return false;
        }

        var candidates = new List<Vector2I>();
        int roomCount = 2;
        for (int y = 0; y < _config.Rows; y++)
        for (int x = 0; x < _config.Columns; x++)
        {
            var cell = new Vector2I(x, y);
            if (cell == begin || cell == end || !IsAvailableCell(cell)) continue;
            roomCount++;
            if (cell != beginExit && cell != bossEntrance) candidates.Add(cell);
        }

        var manualDistances = FindRoomDistances(beginExit, passagesOnly: false, excludeEndpoints: true);
        if (manualDistances.Count != roomCount - 2)
        {
            error = "绕开 Begin 和 Boss 房时，有普通房间无法从 Begin 东邻格到达，请检查手动禁放格和封闭边。";
            return false;
        }

        int directLength = end.X - begin.X;
        int requestedLength = Mathf.Max(_config.MinimumRouteLength, directLength);
        // 四向网格中，每次绕路增加偶数步。
        int longestPossible = roomCount - 1 - (roomCount - 1 - directLength) % 2;
        if (requestedLength > longestPossible)
        {
            error = $"当前保留格最多只能支持 {longestPossible} 个门洞的最短路线，请降低 Minimum Route Length 或增加可用格。";
            return false;
        }
        int minLength = requestedLength + (requestedLength - directLength) % 2;
        requestedBlocked = _config.RandomBlockedMin == _config.RandomBlockedMax
            ? _config.RandomBlockedMin
            : random.RandiRange(_config.RandomBlockedMin, _config.RandomBlockedMax);
        int targetBlocked = Mathf.Min(requestedBlocked, roomCount - (minLength + 1));
        bool needsLongerPath = minLength > directLength;
        bool canGoStraight = manualDistances[bossEntrance] + 2 == directLength;
        bool preferDetour = targetBlocked > 0 && canGoStraight &&
            _config.DetourChance > 0f && random.Randf() < _config.DetourChance;

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

        error = $"本次生成未达到至少 {_config.MinimumRouteLength} 个门洞的最短路线，尝试中最长为 {longestFound} 个门洞。";
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
            if (bounds.End.X > _config.Columns || bounds.End.Y > _config.Rows || bounds.HasPoint(begin) || bounds.HasPoint(end))
                continue;
            if (!CanUseLargeRoomBounds(bounds, normalFloor, template.Geometry.Floor)) continue;

            var candidate = new LargeRoomPlacement(template, bounds);
            RememberLargeRoom(candidate);
            if (FindRoomDistances(begin, passagesOnly: true)[end] >= _config.MinimumRouteLength)
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
        for (int y = 0; y < _config.Rows; y++)
        for (int x = 0; x <= _config.Columns; x++)
        {
            var west = new Vector2I(x - 1, y);
            var east = new Vector2I(x, y);
            var start = new Vector2(x, y);
            AddEdge(start, start + Vector2.Down, (west, east), rooms, random);
        }

        for (int y = 0; y <= _config.Rows; y++)
        for (int x = 0; x < _config.Columns; x++)
        {
            var north = new Vector2I(x, y - 1);
            var south = new Vector2I(x, y);
            var start = new Vector2(x, y);
            AddEdge(start, start + Vector2.Right, (north, south), rooms, random);
        }
    }

    private void BuildWallCornerAnchors(Dictionary<Vector2I, GridRoomTemplate> rooms)
    {
        var wallSpans = new Dictionary<(Vector2I First, Vector2I Second), List<Vector2>>();
        foreach (var (start, end, first, second) in _mapWalls)
        {
            var edge = (first, second);
            if (!wallSpans.TryGetValue(edge, out var spans))
            {
                spans = new List<Vector2>();
                wallSpans.Add(edge, spans);
            }
            spans.Add(first.X == second.X ? new Vector2(start.X, end.X) : new Vector2(start.Y, end.Y));
        }

        foreach (var (cell, room) in rooms)
        {
            Rect2I cells = GetRoomBounds(cell);
            if (cell != cells.Position) continue;

            var bounds = new Rect2((Vector2)cell * _cellSize, (Vector2)cells.Size * _cellSize);
            float west = bounds.Position.X + room.WestWallDepth * 0.5f;
            float east = bounds.End.X - room.EastWallDepth * 0.5f;
            float north = bounds.Position.Y + room.NorthWallDepth * 0.5f;
            float south = bounds.End.Y - room.SouthWallDepth * 0.5f;
            var container = new Node2D { Name = "WallCornerAnchors" };
            room.AddChild(container);
            AddCorner("NorthWest", new Vector2(west, north), Vector2I.Up, Vector2I.Left);
            AddCorner("NorthEast", new Vector2(east, north), Vector2I.Up, Vector2I.Right);
            AddCorner("SouthWest", new Vector2(west, south), Vector2I.Down, Vector2I.Left);
            AddCorner("SouthEast", new Vector2(east, south), Vector2I.Down, Vector2I.Right);

            // 横向大房间的每条分格线只与北墙、南墙各形成一对素材接缝。
            for (int x = cells.Position.X + 1; x < cells.End.X; x++)
            {
                float seam = x * _cellSize.X;
                float left = seam - room.EastWallDepth * 0.5f;
                float right = seam + room.WestWallDepth * 0.5f;
                int index = x - cells.Position.X;
                AddCorner($"NorthSeam{index}Left", new Vector2(left, north), Vector2I.Up, Vector2I.Right, RoomSeam.HorizontalWall);
                AddCorner($"NorthSeam{index}Right", new Vector2(right, north), Vector2I.Up, Vector2I.Left, RoomSeam.HorizontalWall);
                AddCorner($"SouthSeam{index}Left", new Vector2(left, south), Vector2I.Down, Vector2I.Right, RoomSeam.HorizontalWall);
                AddCorner($"SouthSeam{index}Right", new Vector2(right, south), Vector2I.Down, Vector2I.Left, RoomSeam.HorizontalWall);
            }

            // 纵向大房间沿东西墙分段，分格线仍由同一个房间贯通。
            for (int y = cells.Position.Y + 1; y < cells.End.Y; y++)
            {
                float seam = y * _cellSize.Y;
                float upper = seam - room.SouthWallDepth * 0.5f;
                float lower = seam + room.NorthWallDepth * 0.5f;
                int index = y - cells.Position.Y;
                AddCorner($"WestSeam{index}Upper", new Vector2(west, upper), Vector2I.Down, Vector2I.Left, RoomSeam.VerticalWall);
                AddCorner($"WestSeam{index}Lower", new Vector2(west, lower), Vector2I.Up, Vector2I.Left, RoomSeam.VerticalWall);
                AddCorner($"EastSeam{index}Upper", new Vector2(east, upper), Vector2I.Down, Vector2I.Right, RoomSeam.VerticalWall);
                AddCorner($"EastSeam{index}Lower", new Vector2(east, lower), Vector2I.Up, Vector2I.Right, RoomSeam.VerticalWall);
            }

            void AddCorner(string name, Vector2 position, Vector2I horizontalDirection, Vector2I verticalDirection,
                RoomSeam seam = RoomSeam.None)
            {
                Vector2 grid = position / _cellSize;
                var horizontalCell = new Vector2I(
                    Mathf.Clamp(Mathf.FloorToInt(grid.X), cells.Position.X, cells.End.X - 1),
                    horizontalDirection == Vector2I.Up ? cells.Position.Y : cells.End.Y - 1);
                var verticalCell = new Vector2I(
                    verticalDirection == Vector2I.Left ? cells.Position.X : cells.End.X - 1,
                    Mathf.Clamp(Mathf.FloorToInt(grid.Y), cells.Position.Y, cells.End.Y - 1));
                var horizontalNeighbor = horizontalCell + horizontalDirection;
                var verticalNeighbor = verticalCell + verticalDirection;
                WallBoundary horizontal = seam == RoomSeam.VerticalWall
                    ? WallBoundary.Interior : ClassifyWallBoundary(horizontalNeighbor);
                WallBoundary vertical = seam == RoomSeam.HorizontalWall
                    ? WallBoundary.Interior : ClassifyWallBoundary(verticalNeighbor);
                bool horizontalExterior = horizontal == WallBoundary.Exterior;
                bool verticalExterior = vertical == WallBoundary.Exterior;
                string kind = horizontalExterior && verticalExterior ? "Exterior"
                    : horizontalExterior || verticalExterior ? "Mixed" : "Interior";
                Vector2? horizontalSpan = seam == RoomSeam.VerticalWall
                    ? null : FindWallAt(SharedEdge(horizontalCell, horizontalNeighbor), grid.X);
                Vector2? verticalSpan = seam == RoomSeam.HorizontalWall
                    ? null : FindWallAt(SharedEdge(verticalCell, verticalNeighbor), grid.Y);
                bool hasIntersection = horizontalSpan.HasValue && verticalSpan.HasValue;
                // 同侧两段外墙的接缝独立于横向门洞，两个墙段各自保留遮掩锚点。
                bool hasExteriorSideJoint = verticalExterior &&
                    _drawnExteriorSideWalls.Contains((verticalCell, verticalDirection)) &&
                    _drawnExteriorSideWalls.Contains((verticalCell + horizontalDirection, verticalDirection));
                bool usesFixedWallArt =
                    (horizontalSpan.HasValue && _fixedWallFaces.Contains((horizontalCell, horizontalDirection))) ||
                    (verticalSpan.HasValue && _fixedWallFaces.Contains((verticalCell, verticalDirection)));

                Vector2I exteriorSide = verticalExterior ? verticalDirection : horizontalDirection;
                string coverKind = "None";
                string? interiorCoverKind = null;
                if (hasExteriorSideJoint)
                    coverKind = "ExteriorT";
                else if (usesFixedWallArt)
                    coverKind = "None";
                else if (!horizontalExterior && !verticalExterior)
                {
                    if (hasIntersection && horizontal == WallBoundary.Interior && vertical == WallBoundary.Interior)
                    {
                        coverKind = "Interior";
                        interiorCoverKind = "Corner";
                    }
                }
                else if (seam == RoomSeam.HorizontalWall && horizontalDirection == Vector2I.Up && horizontalSpan.HasValue)
                    coverKind = "ExteriorT";
                // 外侧转角使用双墙交点，南外墙沿线跳过遮掩。
                else if (hasIntersection && !(horizontalExterior && horizontalDirection == Vector2I.Down))
                {
                    if (horizontalExterior && verticalExterior)
                        coverKind = "ExteriorL";
                    else
                    {
                        // 两间房的北外墙连续时，各自的角点使用北墙接缝素材。
                        Vector2I adjacentCell = horizontalExterior ? verticalNeighbor : horizontalNeighbor;
                        bool hasAdjacentRoom = rooms.ContainsKey(adjacentCell);
                        if (horizontalExterior && hasAdjacentRoom && !IsInsideGrid(adjacentCell + exteriorSide))
                            coverKind = "ExteriorT";
                        else if (!hasAdjacentRoom && IsInsideGrid(adjacentCell) &&
                            (horizontalExterior ? vertical : horizontal) == WallBoundary.Interior)
                            coverKind = horizontalExterior ? "ExteriorNorthEnd" : "ExteriorSideEnd";
                    }
                }

                var marker = new Marker2D { Name = name, Position = room.ToLocal(_walls.ToGlobal(position)) };
                marker.SetMeta("corner_type", kind);
                marker.SetMeta("horizontal_boundary", seam == RoomSeam.VerticalWall ? "RoomSeam" : horizontal.ToString());
                marker.SetMeta("vertical_boundary", seam == RoomSeam.HorizontalWall ? "RoomSeam" : vertical.ToString());
                marker.SetMeta("has_intersection", hasIntersection);
                if (usesFixedWallArt) marker.SetMeta("has_fixed_wall_art", true);
                marker.SetMeta("cover_kind", coverKind);
                if (interiorCoverKind != null) marker.SetMeta("interior_cover_kind", interiorCoverKind);
                if (coverKind is "ExteriorL" or "ExteriorT" or "ExteriorNorthEnd" or "ExteriorSideEnd")
                    marker.SetMeta("exterior_side", exteriorSide == Vector2I.Up ? "North"
                        : exteriorSide == Vector2I.Left ? "West" : "East");
                container.AddChild(marker);
                if (usesFixedWallArt && !hasExteriorSideJoint) return;

                // 普通 WallCap 已处理美术范围内的余段。房角这里只检查实体单墙
                // 留在该范围之外的端头，避免在已有绿植或门洞上再放一块。
                if (!horizontalExterior && !verticalExterior && horizontalSpan.HasValue != verticalSpan.HasValue)
                {
                    bool isHorizontal = horizontalSpan.HasValue;
                    Vector2I wallCell = isHorizontal ? horizontalCell : verticalCell;
                    Vector2I side = isHorizontal ? horizontalDirection : verticalDirection;
                    if (_interiorWallArtRanges.TryGetValue((wallCell, side), out Vector2 artRange))
                    {
                        bool atEnd = isHorizontal ? verticalDirection == Vector2I.Right : horizontalDirection == Vector2I.Down;
                        float depth = horizontalDirection == Vector2I.Up ? room.NorthWallDepth : room.SouthWallDepth;
                        if (!isHorizontal)
                            depth = verticalDirection == Vector2I.Left ? room.WestWallDepth : room.EastWallDepth;
                        Vector2 wallSpan = horizontalSpan ?? verticalSpan.GetValueOrDefault();
                        Rect2 endBounds = GetUncoveredWallEnd(wallCell, position, wallSpan, artRange,
                            isHorizontal, atEnd, depth);
                        string endKind = isHorizontal ? "HorizontalEnd" : "VerticalEnd";
                        if (endBounds.HasArea() && TryMountInteriorWallCap(endBounds, endKind))
                        {
                            marker.SetMeta("cover_kind", "Interior");
                            marker.SetMeta("interior_cover_kind", endKind);
                            var endAnchor = new Marker2D
                            {
                                Name = "WallEndAnchor",
                                Position = room.ToLocal(_walls.ToGlobal(endBounds.GetCenter())) - marker.Position
                            };
                            endAnchor.SetMeta("max_footprint_size", endBounds.Size);
                            marker.AddChild(endAnchor);
                        }
                    }
                    return;
                }

                MountWallCover(room.Name, marker, coverKind, interiorCoverKind, exteriorSide,
                    mirrorNorth: verticalDirection == Vector2I.Right);
            }
        }

        Vector2? FindWallAt((Vector2I First, Vector2I Second) edge, float axis)
        {
            if (!wallSpans.TryGetValue(edge, out var spans)) return null;
            foreach (Vector2 span in spans)
                if (axis >= span.X && axis <= span.Y) return span;
            return null;
        }
    }

    private Rect2 GetUncoveredWallEnd(Vector2I cell, Vector2 corner, Vector2 wallSpan, Vector2 artRange,
        bool horizontal, bool atEnd, float depth)
    {
        Vector2 along = horizontal ? Vector2.Right : Vector2.Down;
        Vector2 across = horizontal ? Vector2.Down : Vector2.Right;
        float length = _cellSize.Dot(along);
        float edge = (((Vector2)cell).Dot(along) + (atEnd ? 1f : 0f)) * length;
        float innerEdge = 2f * corner.Dot(along) - edge;
        float from = Mathf.Max(Mathf.Min(edge, innerEdge), wallSpan.X * length);
        float to = Mathf.Min(Mathf.Max(edge, innerEdge), wallSpan.Y * length);
        if (atEnd)
            from = Mathf.Max(from, artRange.Y);
        else
            to = Mathf.Min(to, artRange.X);
        if (to <= from) return default;
        return new Rect2(along * from + across * (corner.Dot(across) - depth * 0.5f),
            along * (to - from) + across * depth);
    }

    private void MountWallCover(StringName roomName, Marker2D corner, string coverKind, string? interiorKind,
        Vector2I exteriorSide, bool mirrorNorth)
    {
        PackedScene? scene = coverKind switch
        {
            "ExteriorL" => _config.FloorWallSet?.ExteriorCornerCover,
            "ExteriorT" or "ExteriorNorthEnd" or "ExteriorSideEnd" => _config.FloorWallSet?.ExteriorJointCover,
            "Interior" => _config.FloorWallSet?.InteriorWallCover,
            _ => null
        };
        if (scene?.CanInstantiate() != true) return;

        Node instance = scene.Instantiate();
        if (instance is not Node2D cover)
        {
            instance.Free();
            return;
        }

        bool interior = coverKind == "Interior";
        bool mirror = !interior && (exteriorSide == Vector2I.Right ||
            (exteriorSide == Vector2I.Up && mirrorNorth));
        string? variant = coverKind switch
        {
            "ExteriorT" => exteriorSide == Vector2I.Up ? "North" : "Side",
            "ExteriorNorthEnd" => "North",
            "ExteriorSideEnd" => "Side",
            _ => interiorKind
        };
        if (variant != null && cover.GetNodeOrNull<Node2D>("Variants") is { } variants && variants.HasNode(variant))
        {
            foreach (Node node in variants.GetChildren())
                if (node is CanvasItem item) item.Visible = item.Name == variant;
        }

        cover.Name = $"{cover.Name}_{roomName}_{corner.Name}";
        cover.SetMeta("cover_kind", coverKind);
        if (interiorKind != null) cover.SetMeta("interior_cover_kind", interiorKind);
        if (corner.HasMeta("exterior_side")) cover.SetMeta("exterior_side", corner.GetMeta("exterior_side"));
        var parent = interior ? _interiorWallVisuals : _wallVisuals;
        var anchor = cover.GetNodeOrNull<Marker2D>("WallAnchor");
        Vector2 localAnchor = anchor != null ? cover.Transform * anchor.Position : Vector2.Zero;
        Vector2 position = parent.ToLocal(corner.GlobalPosition);
        if (interior && !HasSolidWallSupport(cover, anchor, parent, position, localAnchor))
        {
            corner.SetMeta("cover_kind", "None");
            corner.RemoveMeta("interior_cover_kind");
            cover.Free();
            return;
        }
        AddWallArt(cover, position, localAnchor, new Vector2(mirror ? -1f : 1f, 1f), interior);
    }

    private bool HasSolidWallSupport(Node2D cover, Marker2D? anchor, Node2D parent,
        Vector2 position, Vector2 localAnchor)
    {
        Vector2 wallPoint = _walls.ToLocal(parent.ToGlobal(position));
        if (anchor?.HasMeta("wall_footprint") != true)
        {
            // 旧素材只有对齐点，使用实际墙体内部的点判定。
            foreach (Rect2 wall in _solidWallBounds)
                if (wall.HasPoint(wallPoint)) return true;
            return false;
        }

        // 墙脚占地与贴图的透视、叶片及阴影分开，按生成后的碰撞矩形检查。
        Rect2 footprint = GetWallCoverFootprint(cover, anchor, parent, position, localAnchor);
        if (!footprint.HasArea()) return false;

        var uncovered = new List<Rect2> { footprint };
        foreach (Rect2 wall in _solidWallBounds)
        {
            if (!wall.Intersects(footprint)) continue;
            for (int i = uncovered.Count - 1; i >= 0; i--)
            {
                Rect2 piece = uncovered[i];
                Rect2 overlap = piece.Intersection(wall);
                if (!overlap.HasArea()) continue;

                uncovered.RemoveAt(i);
                // 扣掉实体墙覆盖的部分，允许两面相接的墙共同支撑一个角柱。
                Keep(piece.Position.X, piece.Position.Y, piece.End.X, overlap.Position.Y);
                Keep(piece.Position.X, overlap.End.Y, piece.End.X, piece.End.Y);
                Keep(piece.Position.X, overlap.Position.Y, overlap.Position.X, overlap.End.Y);
                Keep(overlap.End.X, overlap.Position.Y, piece.End.X, overlap.End.Y);
            }
            if (uncovered.Count == 0) return true;
        }
        return false;

        void Keep(float left, float top, float right, float bottom)
        {
            if (!Mathf.IsEqualApprox(left, right) && !Mathf.IsEqualApprox(top, bottom))
                uncovered.Add(new Rect2(left, top, right - left, bottom - top));
        }
    }

    private Rect2 GetWallCoverFootprint(Node2D cover, Marker2D anchor, Node2D parent,
        Vector2 position, Vector2 localAnchor)
    {
        Transform2D toWall = _walls.GlobalTransform.AffineInverse() * parent.GlobalTransform;
        Transform2D mounted = cover.Transform;
        mounted.Origin += position - localAnchor;
        return toWall * mounted * anchor.Transform * anchor.GetMeta("wall_footprint").AsRect2();
    }

    private void AddEdge(Vector2 start, Vector2 end, (Vector2I First, Vector2I Second) cells,
        Dictionary<Vector2I, GridRoomTemplate> rooms, RandomNumberGenerator random)
    {
        rooms.TryGetValue(cells.First, out var first);
        rooms.TryGetValue(cells.Second, out var second);
        if (first != null && first == second) return;
        bool hasFirst = _rooms.Contains(cells.First);
        bool hasSecond = _rooms.Contains(cells.Second);
        if (!hasFirst && !hasSecond) return;

        bool vertical = start.X == end.X;
        Vector2 along = vertical ? Vector2.Down : Vector2.Right;
        Vector2 across = vertical ? Vector2.Right : Vector2.Down;
        Vector2 worldStart = start * _cellSize;
        Vector2 worldEnd = end * _cellSize;
        if (first == null && second == null)
        {
            // 固定房的外围保留小地图边线，实体墙由固定场景提供。
            _mapWalls.Add((start, end, cells.First, cells.Second));
            return;
        }

        bool hasPassage = hasFirst && hasSecond && _passages.Contains(cells);
        // 门洞两端的接缝碰撞伸入固定房，与原有南北空气墙相接。
        float firstJoinDepth = hasPassage ? GetFixedRoomGeometry(cells.First)?.JoinDepth ?? 0f : 0f;
        float secondJoinDepth = hasPassage ? GetFixedRoomGeometry(cells.Second)?.JoinDepth ?? 0f : 0f;
        float firstDepth = (vertical ? first?.EastWallDepth : first?.SouthWallDepth) ?? firstJoinDepth;
        float secondDepth = (vertical ? second?.WestWallDepth : second?.NorthWallDepth) ?? secondJoinDepth;
        WallBoundary boundary = ClassifyWallBoundary(hasFirst ? cells.Second : cells.First);
        bool drawFirst = first != null && !_fixedWallFaces.Contains((cells.First, (Vector2I)across));
        bool drawSecond = second != null && !_fixedWallFaces.Contains((cells.Second, -(Vector2I)across));
        var firstFace = new WallFace(-across * firstDepth,
            FindWallVisual(cells.First, vertical ? WallSide.East : WallSide.South, boundary), Draw: drawFirst);
        var secondFace = new WallFace(across * secondDepth,
            FindWallVisual(cells.Second, vertical ? WallSide.West : WallSide.North, boundary), Draw: drawSecond);

        (float Start, float End) walkablePassageRange = default;
        (float Start, float End) passageRange = default;
        WallModules? moduleLayout = null;
        if (boundary == WallBoundary.Interior)
        {
            var firstFloor = first != null ? GetRoomFloor(first, cells.First) : GetRoomFloor(second!, cells.Second);
            var secondFloor = first != null && second != null ? GetRoomFloor(second, cells.Second) : firstFloor;
            walkablePassageRange = GetPassageRange(worldStart, worldEnd, along, firstFloor, secondFloor);
            passageRange = walkablePassageRange;
            var firstArtRange = new Vector2(firstFloor.Position.Dot(along), firstFloor.End.Dot(along));
            var secondArtRange = new Vector2(secondFloor.Position.Dot(along), secondFloor.End.Dot(along));
            bool reserveCellEnds = !vertical || GetRoomBounds(cells.First).Size.Y > 1 ||
                GetRoomBounds(cells.Second).Size.Y > 1;
            if (reserveCellEnds)
            {
                // 每个墙段的两端保留本格墙深，包括长边经过的房内分格处。
                Vector2 firstSlot = ReserveWallEnds(firstArtRange, first ?? second!);
                Vector2 secondSlot = ReserveWallEnds(secondArtRange, second ?? first!);
                float from = Mathf.Max(firstSlot.X, secondSlot.X);
                float to = Mathf.Min(firstSlot.Y, secondSlot.Y);
                if (to - from >= MinimumFloorSize.Dot(along))
                    (passageRange, firstArtRange, secondArtRange) = ((from, to), firstSlot, secondSlot);
            }
            // 两侧都有房间时采用共同净长，否则沿有房间的一侧排完整内墙。
            firstFace = firstFace with { ArtRange = firstArtRange };
            secondFace = secondFace with { ArtRange = secondArtRange };
            if (drawFirst) _interiorWallArtRanges[(cells.First, (Vector2I)across)] = firstArtRange;
            if (drawSecond) _interiorWallArtRanges[(cells.Second, -(Vector2I)across)] = secondArtRange;
            float moduleLength = vertical ? VerticalWallModuleLength : HorizontalWallModuleLength;
            moduleLayout = WallModules.CenteredIn(passageRange.Start, passageRange.End, moduleLength);
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
            if (door == Vector2.Zero)
                (gapFrom, gapTo) = walkablePassageRange;
            else
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
                // 先按两间房的共同净宽检查门洞是否足够玩家通过。
                float openingStart = Mathf.Max(gapFrom, modules.BoundaryAt(from));
                float openingEnd = Mathf.Min(gapTo, modules.BoundaryAt(to));
                if (openingEnd - openingStart < minimumWidth) continue;

                // 南北门洞开到最边上的模块时，由相交的东西墙封边，省去转角短墙。
                if (!vertical && from == 0) openingStart = worldStart.X;
                if (!vertical && to == modules.Count) openingEnd = worldEnd.X;
                openings.Add((openingStart, openingEnd));
            }

            // 特殊尺寸容不下足够的整块模块时使用共同净宽，保留已生成的连通路线。
            if (openings.Count > 0)
                (gapFrom, gapTo) = openings[random.RandiRange(0, openings.Count - 1)];
        }
        Vector2 gapStart = vertical ? new Vector2(worldStart.X, gapFrom) : new Vector2(gapFrom, worldStart.Y);
        Vector2 gapEnd = vertical ? new Vector2(worldStart.X, gapTo) : new Vector2(gapTo, worldStart.Y);
        AddWall(worldStart, gapStart, edge);
        AddWall(gapEnd, worldEnd, edge);

        Vector2 ReserveWallEnds(Vector2 artRange, GridRoomTemplate room)
        {
            float startDepth = vertical ? room.NorthWallDepth : room.WestWallDepth;
            float endDepth = vertical ? room.SouthWallDepth : room.EastWallDepth;
            return new Vector2(Mathf.Max(artRange.X, worldStart.Dot(along) + startDepth),
                Mathf.Min(artRange.Y, worldEnd.Dot(along) - endDepth));
        }
    }

    private GridWallVisual? FindWallVisual(Vector2I cell, WallSide side, WallBoundary boundary)
    {
        _contents.TryGetValue(GetRoomBounds(cell).Position, out var content);
        return GridWallVisual.Find(content?.WallVisuals, side, boundary)
            ?? GridWallVisual.Find(_config.FloorWallSet?.WallVisuals, side, boundary);
    }

    private WallBoundary ClassifyWallBoundary(Vector2I neighbor)
    {
        if (!IsInsideGrid(neighbor)) return WallBoundary.Exterior;
        if (_rooms.Contains(neighbor)) return WallBoundary.Interior;
        return _config.BlockedCells.Contains(neighbor) ? WallBoundary.FixedVoid : WallBoundary.Interior;
    }

    private void AddWall(Vector2 start, Vector2 end, WallEdge edge)
    {
        if (start.IsEqualApprox(end)) return;
        _mapWalls.Add((start / _cellSize, end / _cellSize, edge.Cells.First, edge.Cells.Second));
        var span = end - start;
        var bounds = new Rect2(start + edge.First.Inset, span + edge.Second.Inset - edge.First.Inset);
        _solidWallBounds.Add(bounds);
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
        Vector2? exteriorCenter = edge.Boundary == WallBoundary.Exterior ? wall.Position : null;
        bool isExteriorSide = exteriorCenter.HasValue && span.X == 0f;
        if (AddWallFace(start, span, edge.First, exteriorCenter: exteriorCenter) && isExteriorSide)
            _drawnExteriorSideWalls.Add((edge.Cells.First, Vector2I.Right));
        if (AddWallFace(start, span, edge.Second, exteriorCenter: exteriorCenter) && isExteriorSide)
            _drawnExteriorSideWalls.Add((edge.Cells.Second, Vector2I.Left));
    }

    private void AddInteriorWallFace(Vector2 start, Vector2 end, WallFace face, WallModules modules, Vector2 along)
    {
        if (!face.Draw) return;
        if (face.Inset == Vector2.Zero) return;
        float edgeStart = start.Dot(along);
        float visibleStart = Mathf.Max(edgeStart, face.ArtRange.X);
        float visibleEnd = Mathf.Min(end.Dot(along), face.ArtRange.Y);
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
        if (!TryMountInteriorWallCap(bounds))
            AddWallFill(_debugWallFills, bounds, WallColor, "WallCap");
    }

    private bool TryMountInteriorWallCap(Rect2 bounds, string interiorKind = "WallCap")
    {
        if (_config.FloorWallSet?.InteriorWallCover is not { } scene || !scene.CanInstantiate()) return false;
        Node instance = scene.Instantiate();
        if (instance is not Node2D cover ||
            cover.GetNodeOrNull<Marker2D>("WallAnchor") is not { } anchor ||
            !anchor.HasMeta("wall_footprint"))
        {
            instance.Free();
            return false;
        }

        var parent = _interiorWallVisuals;
        Vector2 position = parent.ToLocal(_walls.ToGlobal(bounds.GetCenter()));
        Vector2 localAnchor = cover.Transform * anchor.Position;
        Rect2 footprint = GetWallCoverFootprint(cover, anchor, parent, position, localAnchor);
        if (!footprint.HasArea() || !bounds.Encloses(footprint))
        {
            cover.Free();
            return false;
        }

        cover.Name = $"{cover.Name}_{interiorKind}";
        cover.SetMeta("cover_kind", "Interior");
        cover.SetMeta("interior_cover_kind", interiorKind);
        cover.SetMeta("wall_cap_bounds", bounds);
        AddWallArt(cover, position, localAnchor, Vector2.One, interior: true);
        return true;
    }

    private bool AddWallFace(Vector2 start, Vector2 span, WallFace face, bool centerOnModule = false, Vector2? exteriorCenter = null)
    {
        if (!face.Draw || face.Inset == Vector2.Zero) return false;
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
                    Vector2 placementAnchor = anchor + step * i;
                    if ((centerOnModule || exteriorCenter.HasValue) &&
                        wall.GetNodeOrNull<Marker2D>("WallAnchor") is { } marker)
                    {
                        localAnchor = wall.Transform * marker.Position;
                        if (exteriorCenter is { } center)
                            placementAnchor = center + step * (i - (count - 1) * 0.5f);
                    }
                    AddWallArt(wall, placementAnchor, localAnchor, mirror, centerOnModule);
                }
                return true;
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
                return true;
            }
        }
        if (art != null && art.FillColor.A > 0f)
        {
            AddWallFill(_wallVisuals, bounds, art.FillColor, "WallFill");
            return true;
        }

        AddWallFill(_debugWallFills, bounds, WallColor, "WallFallback");
        return false;
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

    private static void AddWallFill(Node2D parent, Rect2 bounds, Color color, string name)
    {
        parent.AddChild(new ColorRect
        {
            Name = name,
            Position = bounds.Position,
            Size = bounds.Size,
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore
        }, forceReadableName: true);
    }
}
