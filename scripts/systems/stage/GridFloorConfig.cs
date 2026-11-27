using Godot;
using Kuros.Scenes;

namespace Kuros.Systems.Stage;

[GlobalClass]
public partial class GridFloorConfig : Resource
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
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int RandomBlockedMin { get; set; }
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int RandomBlockedMax { get; set; }
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int MinimumRouteLength { get; set; }
    [Export(PropertyHint.Range, "0,1,0.01")] public float DetourChance { get; set; }

    // 网格原点在左上角，共享边由西侧或北侧格登记。
    [Export] public Godot.Collections.Array<Vector2I> BlockedCells { get; set; } = new();
    [Export] public Godot.Collections.Array<Vector2I> ClosedEastEdges { get; set; } = new();
    [Export] public Godot.Collections.Array<Vector2I> ClosedSouthEdges { get; set; } = new();
}
