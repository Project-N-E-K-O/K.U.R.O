namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 敌人死亡终止状态：负责触发实体销毁及资源清理。
    /// </summary>
    public partial class EnemyDeadState : EnemyState
    {
        public override void Enter()
        {
            Enemy.FinalizeDeath();
        }
    }
}


