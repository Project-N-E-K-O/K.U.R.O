using Godot;

namespace Kuros.Systems.Stage
{
    [Tool, GlobalClass]
    public partial class RoomDefinition : Resource
    {
        private RoomType _roomType;

        [Export] public string FloorTheme { get; set; } = "";
        [Export] public RoomType RoomType
        {
            get => _roomType;
            set => _roomType = value.Normalize();
        }
        [Export] public string ElevatorPreviewName { get; set; } = "";
        [Export] public Texture2D? ElevatorPreviewIcon { get; set; }
        [Export] public string[] RouteTags { get; set; } = System.Array.Empty<string>();
        [Export(PropertyHint.File, "*.tscn,*.scn")] public string ScenePath { get; set; } = "";
    }
}
