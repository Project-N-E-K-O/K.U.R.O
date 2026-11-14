using Godot;
using Godot.Collections;

namespace Kuros.Systems.Stage
{
    [GlobalClass]
    public partial class RoomCatalog : Resource
    {
        [Export] public Array<RoomDefinition> Rooms { get; set; } = new();
    }
}
