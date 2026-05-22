using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 等待指定秒数。跳过时立即结束。
    /// 支持异步模式：WaitForCompletion=false 时不阻塞后续步骤。
    /// </summary>
    [GlobalClass]
    public partial class WaitStep : CutsceneStep
    {
        [Export] public float Duration { get; set; } = 1.0f;

        /// <summary>
        /// 是否等待完成。
        /// true（默认）：阻塞执行，等待 Duration 秒后才执行下一步。
        /// false：启动计时后立即返回，后台计时进行。
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = true;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] WaitStep 开始，Duration={Duration}, WaitForCompletion={WaitForCompletion}");
            if (ctx.IsSkipping || Duration <= 0f)
            {
                GD.Print("[Cutscene] WaitStep 跳过（IsSkipping 或 Duration<=0）");
                return;
            }

            var timer = ctx.Tree.CreateTimer(Duration);

            // 若不等待完成，立即返回，但后台仍监听skip事件
            if (!WaitForCompletion)
            {
                _ = MonitorSkipAndFinishAsync(ctx, timer);
                GD.Print($"[Cutscene] WaitStep 异步执行（WaitForCompletion=false），总耗时约 {Duration} 秒");
                return;
            }

            // 否则阻塞等待
            while (!ctx.IsSkipping && timer.TimeLeft > 0f)
                await ctx.NextFrame();

            GD.Print("[Cutscene] WaitStep 完成");
        }

        /// <summary>
        /// 后台监听skip事件
        /// </summary>
        private async Task MonitorSkipAndFinishAsync(CutsceneContext ctx, SceneTreeTimer timer)
        {
            while (!ctx.IsSkipping && timer.TimeLeft > 0f)
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                GD.Print("[Cutscene] WaitStep 被skip，立刻完成");
            }
        }
    }
}
