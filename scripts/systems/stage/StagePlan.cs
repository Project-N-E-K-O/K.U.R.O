using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Kuros.Systems.Stage
{
    public sealed record StagePlan(StageConfig Config, IReadOnlyList<RoomType> RoomTypes, IReadOnlyList<string> RoomPaths,
        IReadOnlyList<Texture2D?> RoomIcons, IReadOnlyList<string> RoomNames)
    {
        public IEnumerable<(string Text, Texture2D? Icon)> GetElevatorPreview()
        {
            if (Config.IsHiddenFloor)
            {
                yield return ("？？？", null);
                yield break;
            }
            int core = -1;
            for (int i = 0; i < RoomTypes.Count; i++)
                if (RoomTypes[i] == RoomType.BossCore)
                {
                    core = i;
                    break;
                }

            if (core >= 0)
            {
                yield return (PreviewName(core), RoomIcons[core]);
                yield break;
            }

            for (int i = 0; i < RoomTypes.Count; i++)
                yield return (PreviewName(i), RoomIcons[i]);
        }

        private string PreviewName(int index) => string.IsNullOrWhiteSpace(RoomNames[index])
            ? DescribeRoomType(RoomTypes[index]) : RoomNames[index];

        public string DescribeRoute() => string.Join(" → ", GetElevatorPreview().Select(room => room.Text));

        private static string DescribeRoomType(RoomType type) => type switch
        {
            RoomType.Begin => "入口",
            RoomType.Combat => "战斗",
            RoomType.AdvancedCombat => "战斗+",
            RoomType.EliteCombat => "战斗++",
            RoomType.Puzzle => "谜题",
            RoomType.Boss => "Boss",
            RoomType.End => "终点",
            RoomType.BossWingA => "Boss 左翼",
            RoomType.BossCore => "Boss 中枢",
            RoomType.BossWingC => "Boss 右翼",
            RoomType.Hidden => "？？？",
            RoomType.Shop => "商店",
            RoomType.Healing => "治疗",
            _ => type.ToString()
        };
    }
}
