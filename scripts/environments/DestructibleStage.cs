using Godot;

namespace Kuros.Environments
{
    /// <summary>
    /// 多阶段受损物的**一段**配置（供 <see cref="DestructibleStageController.Stages"/> 用）：
    /// 一段 = "常态循环 + 受击反馈 + 破壳动画 + 累计伤害阈值"。
    /// </summary>
    [GlobalClass]
    public partial class DestructibleStage : Resource
    {
        /// <summary>本段常态循环动画。</summary>
        [Export] public string IdleAnimation { get; set; } = "";

        /// <summary>本段受击一次性动画，**每次命中都从头重播**（高频伤害持续刷新、不构成免伤窗口）。
        /// 空 = 受击不播动画（如"过脆的末段"：不需要受击反馈，伤害照常累计）。</summary>
        [Export] public string HitAnimation { get; set; } = "";

        /// <summary>离开本段时播的一次性动画（累计伤害 ≥ <see cref="Threshold"/> 时）。
        /// **最后一段的 BreakAnimation = 终态动画**（`OnDeathFinalized` 播一次、禁命中、留尸）；
        /// 其余段播完锁免伤并回下一段 idle。空 = 不播动画（直接进下一段 / 终态保持锁死）。</summary>
        [Export] public string BreakAnimation { get; set; } = "";

        /// <summary>累计伤害阈值：**从满血起累计** ≥ 本值时离开本段（进入下一段 / 最后一段则进终态）。
        /// 跨段那一击的溢出伤害作废（累计钳到本值）——单次受击最多推进一段，任何单次伤害都不可能跳过中间动画。
        /// **&lt;= 0 = 免命中自动推进**：进入本段立即播 <see cref="BreakAnimation"/> 并前进到下一段（无需受击）；
        /// 最后一段 &lt;= 0 视为进入即达死亡线（直接终态）。</summary>
        [Export(PropertyHint.Range, "0,10000,1")] public float Threshold { get; set; } = 100f;
    }
}
