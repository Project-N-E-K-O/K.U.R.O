using Godot;

namespace Kuros.Systems.Stage;

[GlobalClass]
public partial class RoomTypeRule : Resource
{
    [Export] public RoomType RoomType { get; set; } = RoomType.Combat;
    [Export(PropertyHint.Range, "0,100,0.1")] public float Weight { get; set; } = 1;
    [Export(PropertyHint.Range, "0,64,1")] public int MinCount { get; set; }
    /// <summary>-1 表示不额外限制，0 表示禁止。</summary>
    [Export(PropertyHint.Range, "-1,64,1")] public int MaxCount { get; set; } = -1;
    /// <summary>连续缺席多少个有该类型候选的普通楼层后保底；0 禁用。</summary>
    [Export(PropertyHint.Range, "0,100,1")] public int GuaranteeAfter { get; set; }
}
