namespace Kuros.Systems.Stage
{
    public enum RoomType
    {
        Begin = 0,
        Combat = 1,
        // 2 和 3 是旧战斗分级的资源编号。挑战沿用原谜题编号。
        Challenge = 4,
        Boss = 5,
        End = 6,
        BossWingLeft = 7,
        BossCore = 8,
        BossWingRight = 9,
        Hidden = 10,
        Shop = 11,
        Healing = 12,
    }

    internal static class RoomTypeExtensions
    {
        internal static RoomType Normalize(this RoomType type) =>
            (int)type is 2 or 3 ? RoomType.Combat : type;

        internal static bool IsRandomMiddle(this RoomType type) =>
            type is RoomType.Combat or RoomType.Challenge or RoomType.Shop or RoomType.Healing;
    }
}
