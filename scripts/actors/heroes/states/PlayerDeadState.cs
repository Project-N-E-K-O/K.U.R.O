namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 玩家死亡终止状态：处理场景重载或复活逻辑。
    /// </summary>
    public partial class PlayerDeadState : PlayerState
    {
        public override void Enter()
        {
            Player.FinalizeDeath();
        }
    }
}


