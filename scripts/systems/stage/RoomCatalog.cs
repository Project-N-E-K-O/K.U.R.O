using Godot;
using Godot.Collections;

namespace Kuros.Systems.Stage
{
    [Tool, GlobalClass]
    public partial class RoomCatalog : Resource
    {
        [Export] public Array<RoomDefinition> Rooms { get; set; } = new();
    }
}
