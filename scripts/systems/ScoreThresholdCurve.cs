using Godot;

namespace Kuros.Systems
{
    /// <summary>
    /// 分数阈值曲线计算器。
    /// 公式：NextThreshold = Bxp × (Level + Offset) ^ Scale ^ Power + Add
    ///
    /// 参数说明：
    ///   Bxp    基础分数需求，每级触发的基础分数量
    ///   Offset 偏移等级，>0 提高初期需求，<0 降低初期需求
    ///   Scale  陡峭程度，>1 越高级需求增长越快，<1 增长放缓
    ///   Power  幂指数，=1 线性，>1 加速，0-1 减速
    ///   Add    修正常数，平移所有等级的需求值
    /// </summary>
    [GlobalClass]
    public partial class ScoreThresholdCurve : Resource
    {
        [ExportGroup("曲线参数")]
        [Export(PropertyHint.Range, "1,10000,1")]
        public float Bxp { get; set; } = 100f;

        [Export(PropertyHint.Range, "-10,10,0.1")]
        public float Offset { get; set; } = 0f;

        [Export(PropertyHint.Range, "0.1,5,0.01")]
        public float Scale { get; set; } = 1f;

        [Export(PropertyHint.Range, "0.1,3,0.01")]
        public float Power { get; set; } = 1.5f;

        [Export(PropertyHint.Range, "-1000,1000,1")]
        public float Add { get; set; } = 0f;

        /// <summary>
        /// 计算第 level 级触发所需的累计总分。level 从 1 开始。
        /// </summary>
        public int GetCumulativeScore(int level)
        {
            if (level <= 0) return 0;

            int total = 0;
            for (int lv = 1; lv <= level; lv++)
                total += GetScoreForLevel(lv);
            return total;
        }

        /// <summary>
        /// 计算第 level 级单独需要多少分（不累计）。
        /// </summary>
        public int GetScoreForLevel(int level)
        {
            float inner = level + Offset;
            if (inner <= 0) inner = 0.001f;

            float result = Bxp * Mathf.Pow(Mathf.Pow(inner, Scale), Power) + Add;
            return Mathf.Max(1, Mathf.RoundToInt(result));
        }

        /// <summary>
        /// 根据当前总分，计算下一次触发所需的累计总分。
        /// 返回 -1 表示超过最大触发次数。
        /// </summary>
        public int GetNextThreshold(int currentScore, int maxTriggers = 20)
        {
            for (int lv = 1; lv <= maxTriggers; lv++)
            {
                int threshold = GetCumulativeScore(lv);
                if (currentScore < threshold)
                    return threshold;
            }
            return -1;
        }

        /// <summary>
        /// 根据当前总分，计算已触发的次数。
        /// </summary>
        public int GetTriggerCount(int currentScore, int maxTriggers = 20)
        {
            int count = 0;
            for (int lv = 1; lv <= maxTriggers; lv++)
            {
                if (currentScore >= GetCumulativeScore(lv))
                    count++;
                else
                    break;
            }
            return count;
        }
    }
}
