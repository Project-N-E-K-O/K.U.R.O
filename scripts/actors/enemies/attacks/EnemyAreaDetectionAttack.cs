using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
        /// <summary>
    /// 区域检测攻击：将攻击触发条件从默认的距离判定改为指定 Area2D 的 Body 重叠检测。
    /// 在场景中放置独立的 DetectionArea，玩家进入该区域才允许此攻击触发。
    /// </summary>
    public partial class EnemyAreaDetectionAttack : EnemyAttackTemplate
    {
        // 检测区域由基类统一提供：TriggerAreaPath（未配置 = 不限制触发范围）。旧的 DetectionAreaPath 已并入基类。

        /// <summary>
        /// 覆写检测逻辑：用 TriggerArea 是否与玩家重叠替代默认的距离判定。
        /// 未配置 TriggerAreaPath 时返回 true（不限制触发范围）。
        /// </summary>
        public override bool IsPlayerInDetectionRange() => IsPlayerInTriggerArea();
    }
}