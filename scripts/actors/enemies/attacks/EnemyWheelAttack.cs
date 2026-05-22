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
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
    }
}