using Godot;
using Kuros.Actors.Enemies.States;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 在范围内检测玩家并触发攻击特效
    /// 仅可配置持续时间，不做伤害判定
    /// </summary>
    [GlobalClass]
    public partial class EnemyWheelAttack : EnemyAttackTemplate
    {
        // 起手检测区由基类提供：TriggerAreaPath（未配置 = 不做额外限制）。旧的 DetectionAreaPath 已并入基类
        // （原字段声明了但从未被读取，等于摆设）。

        /// <summary>起手额外要求玩家在 TriggerAreaPath 指定的区内（未配置则不限制）。</summary>
        public override bool CanStart() => base.CanStart() && IsPlayerInTriggerArea();
    }
}