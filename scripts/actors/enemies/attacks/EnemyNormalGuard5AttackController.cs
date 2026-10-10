using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// Enemy_Normal_guard5 攻击控制器：疲劳权重法（继承基类）。
    /// Melee = 盾击（节点 "SimpleMeleeAttack"，复用 <see cref="EnemySimpleMeleeAttack"/>：
    /// 无位移的普通近战——动画事件结算伤害+击退）；
    /// Skill = 跪地持盾→冲撞（节点 "ShieldStance"，<see cref="EnemyGuard5ShieldStanceAttack"/>，skill1+skill2 一体）。
    /// </summary>
    public partial class EnemyNormalGuard5AttackController : EnemyFatigueAttackControllerBase
    {
        public EnemyNormalGuard5AttackController()
        {
            MeleeAttackName = "SimpleMeleeAttack";
            SkillAttackName = "ShieldStance";
        }
    }
}
