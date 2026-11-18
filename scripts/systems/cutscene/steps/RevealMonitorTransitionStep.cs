using Godot;
using Kuros.UI;
using System.Threading.Tasks;

namespace Kuros.Systems.Cutscene;

/// <summary>镜头就位后揭开电脑熄屏遮罩。没有该转场时直接继续。</summary>
[GlobalClass]
public partial class RevealMonitorTransitionStep : CutsceneStep
{
    /// <summary>关闭时，后续步骤可以在揭幕动画播放期间开始。</summary>
    [Export] public bool WaitForCompletion { get; set; } = true;

    public override Task Execute(CutsceneContext ctx)
    {
        var reveal = RevealAsync(ctx);
        return WaitForCompletion ? reveal : Task.CompletedTask;
    }

    private static async Task RevealAsync(CutsceneContext ctx)
    {
        var transition = MonitorShutdownTransition.Find(ctx.Tree);
        if (transition == null) return;

        if (ctx.IsSkipping)
        {
            transition.QueueFree();
            return;
        }

        var reveal = transition.RevealAsync();
        while (!reveal.IsCompleted && !ctx.IsSkipping && !ctx.IsCancelled)
            await ctx.NextFrame();

        if (reveal.IsCompleted) await reveal;
        else if (GodotObject.IsInstanceValid(transition)) transition.QueueFree();
    }
}
