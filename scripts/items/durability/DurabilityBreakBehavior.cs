namespace Kuros.Items.Durability
{
    /// <summary>
    /// 指定耐久度降至 0 时物品的行为。
    /// </summary>
    public enum DurabilityBreakBehavior
    {
        /// <summary>耐久度耗尽后立即从背包中移除。</summary>
        Disappear = 0,

        /// <summary>进入损毁状态，可选择后续修复。</summary>
        BecomeBroken = 1
    }
}

