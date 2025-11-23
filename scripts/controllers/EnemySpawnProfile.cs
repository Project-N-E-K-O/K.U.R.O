using Godot;
using Godot.Collections;

namespace Kuros.Controllers
{
    [GlobalClass]
    public partial class EnemySpawnProfile : Resource
    {
        [ExportCategory("Identity")]
        [Export] public string ProfileId = "";
        [Export] public PackedScene EnemyScene { get; set; } = null!;

        [ExportCategory("Spawn Settings")]
        [Export(PropertyHint.Range, "1,100,1")] public int SpawnCount = 1;
        [Export] public bool UseExplicitPositions = false;
        [Export] public Array<Vector2> SpawnPositions { get; set; } = new();
        [Export] public Vector2 AreaCenter = Vector2.Zero;
        [Export] public Vector2 AreaExtents = new Vector2(200, 100);

        [ExportCategory("Respawn")]
        [Export] public bool AllowRespawn = false;
        [Export(PropertyHint.Range, "0.5,60,0.5")] public float RespawnDelay = 5f;

        [ExportCategory("Orientation")]
        [Export] public Vector2 DefaultFacing = Vector2.Left;
    }
}

