using Godot;

namespace Kuros.Systems.Stage
{
    public enum StageRouteMode
    {
        Ordered = 0,
        RandomMiddleRooms = 1,
    }

    /// <summary>
    /// 关卡配置：房间池 + 元信息。新增一个关卡 = 新建一份此资源并填入房间池，
    /// 无需复制关卡壳场景（壳结构与 Stage_1~4 完全同构，差异只有池与文本）。
    /// </summary>
    [GlobalClass]
    public partial class StageConfig : Resource
    {
        [Export] public string StageId { get; set; } = "";
        [Export] public string DisplayName { get; set; } = "";
        [Export] public string ElevatorLabel { get; set; } = "";
        [Export] public Texture2D? ElevatorPreviewTexture { get; set; }
        [Export(PropertyHint.MultilineText)] public string LevelDescription { get; set; } = "";

        /// <summary>所在楼层/层序号：电梯上行默认取 Floor+1 的分支（支持负值 = 地下层）。</summary>
        [Export(PropertyHint.Range, "-20,20,1")] public int Floor { get; set; } = 0;

        /// <summary>上行目标层：0 = 默认 Floor+1 推导。非连续链/回程显式指定（如彩蛋层回主链、地下层返回大堂）。</summary>
        [Export(PropertyHint.Range, "-20,20,1")] public int UpFloorTarget { get; set; } = 0;

        /// <summary>下行目标层：0 = 无向下出口（大堂→地下1层等反向链）。</summary>
        [Export(PropertyHint.Range, "-20,20,1")] public int DownFloorTarget { get; set; } = 0;

        /// <summary>本关的敌人强度**覆盖**：留空 = 用会话级默认（按 <see cref="Floor"/> 推导）。
        /// 只给特例关填——彩蛋关（放宽）、Boss 层（收紧）——同一份配置可被多关共享。
        /// 不填就不会错：新关只要 Floor 对，强度自动一致。</summary>
        [Export] public StageDifficultyConfig? DifficultyOverride { get; set; }

        [Export] public bool IsHiddenFloor { get; set; }
        [Export] public string RequiredStoryFlag { get; set; } = "";
        [Export] public string RequiredRoomKeyItemId { get; set; } = "";

        [ExportCategory("Room Route")]
        [Export] public string FloorTheme { get; set; } = "";
        [Export] public StageRouteMode RouteMode { get; set; }
        [Export] public Godot.Collections.Array<RoomType> RoomSequence { get; set; } = new();

        [ExportGroup("Random Middle Rooms")]
        /// <summary>留空时使用当前主题的所有可随机中间房。</summary>
        [Export] public string MiddleRoomRouteTag { get; set; } = "";
        [Export(PropertyHint.Range, "0,64,1")] public int MiddleRoomCount { get; set; }
        [Export(PropertyHint.File, "*.tscn,*.scn")] public string BeginScenePath { get; set; } = "";
        [Export(PropertyHint.File, "*.tscn,*.scn")] public string EndScenePath { get; set; } = "";

        public string ArrivalName => string.IsNullOrWhiteSpace(DisplayName) ? StageId : DisplayName;
        public string DepartureLabel => string.IsNullOrWhiteSpace(ElevatorLabel) ? "？？？" : ElevatorLabel;

    }
}
