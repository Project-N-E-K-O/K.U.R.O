using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 等待指定秒数。跳过时立即结束。
    /// </summary>
    [GlobalClass]
    public partial class WaitStep : CutsceneStep
    {
        [Export] public float Duration { get; set; } = 1.0f;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] WaitStep 开始，Duration={Duration}");
            if (ctx.IsSkipping || Duration <= 0f)
            {
                GD.Print("[Cutscene] WaitStep 跳过（IsSkipping 或 Duration<=0）");
                return;
            }

            var timer = ctx.Tree.CreateTimer(Duration);
            while (!ctx.IsSkipping && timer.TimeLeft > 0f)
                await ctx.NextFrame();

            GD.Print("[Cutscene] WaitStep 完成");
        }
    }
}
