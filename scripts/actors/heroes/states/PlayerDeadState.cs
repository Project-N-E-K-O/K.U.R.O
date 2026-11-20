using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 玩家死亡终止状态：弹出死亡界面（GameOverScreen，可重试/返回初始界面），
    /// 并终结玩家节点（掉落、清效果、销毁）。
    /// </summary>
    public partial class PlayerDeadState : PlayerState
    {
        public override void Enter()
        {
            // 每次死亡：游戏循环数 +1，并自动保存元数据（进度/时间）
            var saves = SaveManager.Instance;
            if (saves?.HasActiveRun == true)
                saves.FinishRun(RunOutcome.NotCleared, playerDied: true);
            else if (saves?.CurrentGameData is { } profile)
            {
                profile.CycleCount++;
                saves.AutosaveCurrentSlot();
            }

            // 弹出死亡界面（挂在 UIManager 菜单层，玩家节点销毁不影响其存活）
            UIManager.Instance?.LoadGameOverScreen();
            Player.FinalizeDeath();
        }
    }
}
