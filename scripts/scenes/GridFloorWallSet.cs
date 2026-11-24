using Godot;

namespace Kuros.Scenes;

[GlobalClass]
public partial class GridFloorWallSet : Resource
{
    [Export] public Godot.Collections.Array<GridWallVisual> WallVisuals { get; set; } = new();
}
