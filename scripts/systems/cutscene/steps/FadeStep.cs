using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 淡入/淡出黑幕。
    /// 需要 CutsceneManager.FadeOverlayPath 指向一个 CanvasItem（如全屏 ColorRect）。
    /// TargetAlpha = 1 → 全黑，TargetAlpha = 0 → 透明。
    /// </summary>
    [GlobalClass]
    public partial class FadeStep : CutsceneStep
    {
        [Export] public float TargetAlpha { get; set; } = 1.0f;

        [Export] public float Duration { get; set; } = 0.5f;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] FadeStep 开始，TargetAlpha={TargetAlpha}, Duration={Duration}");
            var overlay = ctx.Manager.FadeOverlay;
            if (overlay == null)
            {
                GD.PrintErr("[Cutscene] FadeStep: FadeOverlay 为 null，请在 CutsceneManager 中设置 FadeOverlayPath");
                return;
            }

            if (Duration <= 0f || ctx.IsSkipping)
            {
                overlay.Modulate = new Color(overlay.Modulate, TargetAlpha);
                GD.Print("[Cutscene] FadeStep 立即完成（无过渡）");
                return;
            }

            var tween = overlay.CreateTween();
            tween.TweenProperty(overlay, "modulate:a", TargetAlpha, Duration);

            while (!ctx.IsSkipping && tween.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tween.Kill();
                overlay.Modulate = new Color(overlay.Modulate, TargetAlpha);
            }
            GD.Print("[Cutscene] FadeStep 完成");
        }
    }
}
