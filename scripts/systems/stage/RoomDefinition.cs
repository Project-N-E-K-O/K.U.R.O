using Godot;

namespace Kuros.Systems.Stage
{
    [GlobalClass]
    public partial class RoomDefinition : Resource
    {
        [Export] public string FloorTheme { get; set; } = "";
        [Export] public RoomType RoomType { get; set; }
        [Export] public Texture2D? ElevatorPreviewIcon { get; set; }
        [Export] public string[] RouteTags { get; set; } = System.Array.Empty<string>();
        [Export(PropertyHint.File, "*.tscn,*.scn")] public string ScenePath { get; set; } = "";
    }
}
