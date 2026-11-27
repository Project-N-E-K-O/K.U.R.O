using Godot;
using Kuros.Managers;
using Kuros.Systems.Stage;
using Kuros.UI;

namespace Kuros.Scenes;

public partial class StageTest : Node2D
{
    [Export(PropertyHint.Range, "3,31,2,or_greater")] public int Columns { get; set; } = 5;
    [Export(PropertyHint.Range, "1,31,1,or_greater")] public int Rows { get; set; } = 5;
    [Export] public PackedScene? RoomScene { get; set; }
    [Export] public Godot.Collections.Array<PackedScene> LargeRoomScenes { get; set; } = new();
    [Export] public RoomCatalog? RoomCatalog { get; set; }
    [Export] public RoomDefinition? BeginRoomDefinition { get; set; }
    [Export] public RoomDefinition? EndRoomDefinition { get; set; }
    [Export] public Godot.Collections.Array<PackedScene> RoomContentScenes { get; set; } = new();
    [Export] public PackedScene? BeginRoomScene { get; set; }
    [Export] public PackedScene? EndRoomScene { get; set; }
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

    private bool _debugWallFill;

    [ExportGroup("Debug")]
    /// <summary>显示没有美术覆盖的默认墙面色块。</summary>
    [Export] public bool DebugWallFill
    {
        get => _debugWallFill;
        set
        {
            _debugWallFill = value;
            if (GetNodeOrNull<Node2D>("World/WallVisuals/DebugWallFills") is { } fills) fills.Visible = value;
        }
    }

    private GridFloorGenerator? _grid;

    public override void _Ready()
    {
        var world = GetNode<Node2D>("World");
        var player = world.GetNode<CharacterBody2D>("MainCharacter");
        var settings = new GridFloorConfig
        {
            Columns = Columns,
            Rows = Rows,
            RoomScene = RoomScene,
            LargeRoomScenes = LargeRoomScenes,
            RoomCatalog = RoomCatalog,
            BeginRoomDefinition = BeginRoomDefinition,
            EndRoomDefinition = EndRoomDefinition,
            RoomContentScenes = RoomContentScenes,
            BeginRoomScene = BeginRoomScene,
            EndRoomScene = EndRoomScene,
            FloorWallSet = FloorWallSet,
            RandomBlockedMin = RandomBlockedMin,
            RandomBlockedMax = RandomBlockedMax,
            MinimumRouteLength = MinimumRouteLength,
            DetourChance = DetourChance,
            BlockedCells = BlockedCells,
            ClosedEastEdges = ClosedEastEdges,
            ClosedSouthEdges = ClosedSouthEdges
        };

        var grid = new GridFloorGenerator(settings, LayoutSeed);
        if (!grid.TryPlan(player, out string error))
        {
            GD.PushError($"[Stage_test] {error}");
            player.ProcessMode = ProcessModeEnum.Disabled;
            SetPhysicsProcess(false);
            GetNode<Label>("HUD/LayoutError").Text = error;
            return;
        }

        DebugWallFill = _debugWallFill;
        grid.Mount(world, player, GetNode<CameraZoneManager>("CameraZoneManager"),
            minimap: GetNode<StageTestMinimap>("HUD/Minimap"));
        _grid = grid;
    }

    public override void _PhysicsProcess(double delta) => _grid?.UpdatePlayerRoom();
}
