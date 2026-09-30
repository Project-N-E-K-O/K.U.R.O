using Godot;

namespace Kuros.Systems.Stage
{
    /// <summary>
    /// 楼层难度系数：楼层越高，敌人的血量与伤害越高。
    ///
    /// **正层与地下层各一套**——正层按酒店楼层线性增长；地下层（Floor 为负）明显更强，且越深
    /// （|Floor| 越大）越强，两边各自封顶。
    ///
    /// 乘数一律由层数**推导**（见 <see cref="HealthMultiplier"/> / <see cref="DamageMultiplier"/>），
    /// 这里只导出"每层步进"和"封顶"这些真正要拍板的数，不导出乘数本身。
    /// 第 0 层严格 ×1（大堂基准）。换曲线形状只需改这两个函数，其余代码不动。
    /// </summary>
    [GlobalClass]
    public partial class StageDifficultyConfig : Resource
    {
        [ExportGroup("正层（Floor ≥ 0）")]
        [Export(PropertyHint.Range, "0,2,0.01")] public float HealthPerFloor { get; set; } = 0.12f;
        [Export(PropertyHint.Range, "0,2,0.01")] public float DamagePerFloor { get; set; } = 0.06f;
        [Export(PropertyHint.Range, "1,20,0.1")] public float MaxMultiplier { get; set; } = 3f;

        [ExportGroup("地下层（Floor < 0）")]
        [Export(PropertyHint.Range, "0,2,0.01")] public float BasementHealthPerFloor { get; set; } = 0.35f;
        [Export(PropertyHint.Range, "0,2,0.01")] public float BasementDamagePerFloor { get; set; } = 0.20f;
        [Export(PropertyHint.Range, "1,20,0.1")] public float BasementMaxMultiplier { get; set; } = 6f;

        /// <summary>该楼层的敌人血量系数（Floor 0 = 1）。</summary>
        public float HealthMultiplier(int floor)
            => Multiplier(floor, HealthPerFloor, BasementHealthPerFloor, MaxMultiplier, BasementMaxMultiplier);

        /// <summary>该楼层的敌人伤害系数（Floor 0 = 1）。</summary>
        public float DamageMultiplier(int floor)
            => Multiplier(floor, DamagePerFloor, BasementDamagePerFloor, MaxMultiplier, BasementMaxMultiplier);

        private static float Multiplier(int floor, float perFloor, float basementPerFloor, float cap, float basementCap)
        {
            if (floor >= 0)
                return Mathf.Min(1f + floor * perFloor, Mathf.Max(1f, cap));

            // 地下层：用 |floor| 做深度，步进更陡、封顶更高
            return Mathf.Min(1f + -floor * basementPerFloor, Mathf.Max(1f, basementCap));
        }
    }
}
