using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_Normal_guard4 攻击控制器：疲劳权重法（继承基类）——连续同攻击降权、切攻击恢复。
    /// Melee = 下劈（节点 "Attack"，复用 <see cref="EnemyKickAttack"/>）；
    /// Skill = 防御反击（节点 "CounterAttack"，<see cref="EnemyGuard4CounterAttack"/>，skill1+skill2 一体）。
    /// </summary>
    public partial class EnemyNormalGuard4AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyNormalGuard4AttackController()
        {
            MeleeAttackName = "SimpleMeleeAttack";
            SkillAttackName = "CounterAttack";
        }

        /// <summary>子攻击被打断（受击/眩晕导致的状态切换）：控制器默认会清掉子攻击 CD（允许尽快重试），
        /// 但防御反击不同——被打断就是"招架失败"：按普通 CD 结算 + **兜底眩晕**。
        /// 兜底的必要性：模板内置的"受伤眩晕打断"依赖伤害事件发布时攻击仍在运行，
        /// 部分打断路径（攻击先被清掉/事件回调轮不到）会漏掉；控制器级打断回调是全部路径的必经点。
        /// （Recovery 阶段打断时控制器本就保留 CD、也不触发本回调，不受影响。）</summary>
        protected override void OnAttackInterrupted(EnemyAttackTemplate attack)
        {
            base.OnAttackInterrupted(attack);
            if (attack is EnemyGuard4CounterAttack counter)
            {
                counter.ApplyInterruptedCooldown();
                counter.ApplyInterruptStun();
            }
        }
    }
}
