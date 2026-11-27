using Godot;

namespace Kuros.Scenes;

[GlobalClass]
public partial class GridFloorWallSet : Resource
{
    [Export] public Godot.Collections.Array<GridWallVisual> WallVisuals { get; set; } = new();
    [ExportGroup("Wall Covers")]
    [Export] public PackedScene? ExteriorCornerCover { get; set; }
    [Export] public PackedScene? ExteriorJointCover { get; set; }
    [Export] public PackedScene? InteriorWallCover { get; set; }
}
