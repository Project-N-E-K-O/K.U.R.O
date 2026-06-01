using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 淡入/淡出黑幕（电影式）。
    /// 支持两种模式：
    /// 1. 透明度淡化：改变 TargetAlpha（原始模式，使用 CutsceneManager.FadeOverlay）
    /// 2. 电影黑幕：上下黑条向外移动（推荐）
    /// 
    /// 电影模式需要在 CutsceneManager 中配置：
    /// - TopBlackBarPath：指向上方黑色 ColorRect
    /// - BottomBlackBarPath：指向下方黑色 ColorRect
    /// 
    /// 动画行为：
    /// - TargetAlpha >= 0.5（淡出/黑条进入）：黑条返回初始位置
    /// - TargetAlpha < 0.5（淡入/黑条消失）：黑条向外移动
    /// </summary>
    [GlobalClass]
    public partial class FadeStep : CutsceneStep
    {
        [Export] public float TargetAlpha { get; set; } = 0f;

        /// <summary>
        /// 延迟时长（秒）：等待多久后才开始转变
        /// </summary>
        [Export] public float Duration { get; set; } = 0f;

        /// <summary>
        /// 转变时长（秒）：从当前 Alpha 转变到 TargetAlpha 所需的时间
        /// </summary>
        [Export] public float TransitionDuration { get; set; } = 0f;

        /// <summary>
        /// 是否使用电影式黑幕（上下条形移动）。
        /// 若为 true，使用 CutsceneManager 中配置的 TopBlackBarPath 和 BottomBlackBarPath。
        /// 若为 false，使用传统的透明度淡化（CutsceneManager.FadeOverlay）。
        /// </summary>
        [Export] public bool UseMovieBlackBars { get; set; } = true;

        /// <summary>
        /// 是否等待整个过程完成（延迟 + 转变）。
        /// true（默认）：阻塞执行，等待 Duration + TransitionDuration 秒后才执行下一步。
        /// false：启动动画后立即返回，不阻塞后续步骤（人物/UI 可同时出现）。
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = false;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] FadeStep 开始，TargetAlpha={TargetAlpha}, Duration={Duration}, TransitionDuration={TransitionDuration}, WaitForCompletion={WaitForCompletion}, UseMovieBlackBars={UseMovieBlackBars}");
            
            if (UseMovieBlackBars)
            {
                await ExecuteMovieBlackBars(ctx);
            }
            else
            {
                await ExecuteAlphaFade(ctx);
            }
        }

        private async Task ExecuteAlphaFade(CutsceneContext ctx)
        {
            var overlay = ctx.Manager.FadeOverlay;
            if (overlay == null)
            {
                GD.PrintErr("[Cutscene] FadeStep: FadeOverlay 为 null，请在 CutsceneManager 中设置 FadeOverlayPath");
                return;
            }

            // 如果没有延迟也没有过渡时长，立即设置并返回
            if ((Duration <= 0f && TransitionDuration <= 0f) || ctx.IsSkipping)
            {
                overlay.Modulate = new Color(overlay.Modulate, TargetAlpha);
                GD.Print("[Cutscene] AlphaFade 立即完成（无延迟和过渡）");
                return;
            }

            // 创建 Tween 序列：先延迟，后过渡
            var tween = overlay.CreateTween();
            
            // 步骤 1：如果有延迟时长，先等待
            if (Duration > 0f)
            {
                tween.TweenInterval(Duration);
            }

            // 步骤 2：过渡到目标 Alpha（最少 0.01 秒）
            float transitionTime = Mathf.Max(TransitionDuration, 0.01f);
            tween.TweenProperty(overlay, "modulate:a", TargetAlpha, transitionTime);

            // 若不等待完成，立即返回，但后台仍监听skip事件
            if (!WaitForCompletion)
            {
                // 后台监听skip并立刻消失
                _ = MonitorSkipAndFinishAlphaAsync(ctx, tween, overlay);
                GD.Print($"[Cutscene] AlphaFade 异步执行（WaitForCompletion=false），总耗时约 {Duration + transitionTime} 秒");
                return;
            }

            // 否则阻塞等待整个过程完成或skip
            while (!ctx.IsSkipping && tween.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tween.Kill();
                overlay.Modulate = new Color(overlay.Modulate, TargetAlpha);
            }
            GD.Print("[Cutscene] AlphaFade 完成");
        }

        /// <summary>
        /// 后台监听skip事件，如果发生skip则立刻跳到最终Alpha
        /// </summary>
        private async Task MonitorSkipAndFinishAlphaAsync(CutsceneContext ctx, Tween tween, CanvasItem overlay)
        {
            while (!ctx.IsSkipping && tween.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tween.Kill();
                overlay.Modulate = new Color(overlay.Modulate, TargetAlpha);
                GD.Print("[Cutscene] AlphaFade 被skip，立刻消失");
            }
        }

        private async Task ExecuteMovieBlackBars(CutsceneContext ctx)
        {
            // 从 CutsceneManager 获取黑条节点
            var topRect = ctx.Manager.TopBlackBar;
            var bottomRect = ctx.Manager.BottomBlackBar;

            if (topRect == null || bottomRect == null)
            {
                GD.PrintErr($"[Cutscene] MovieBlackBars: TopBlackBar 或 BottomBlackBar 为 null，请在 CutsceneManager 中设置 TopBlackBarPath 和 BottomBlackBarPath");
                return;
            }

            // 根据视口高度计算屏幕内（可见）与屏幕外（隐藏）坐标
            float viewportH     = ctx.Manager.GetViewport().GetVisibleRect().Size.Y;
            float topInsideY    = 0f;
            float topOutsideY   = -topRect.Size.Y;
            float bottomInsideY  = viewportH - bottomRect.Size.Y;
            float bottomOutsideY = viewportH;

            float transTime = Mathf.Max(TransitionDuration, 0.01f);
            float holdTime  = Mathf.Max(Duration, 0f);

            // 初始化：确保黑条在屏幕外
            topRect.Position    = new Vector2(topRect.Position.X, topOutsideY);
            bottomRect.Position = new Vector2(bottomRect.Position.X, bottomOutsideY);

            if (ctx.IsSkipping)
            {
                GD.Print("[Cutscene] MovieBlackBars skip立即结束（黑条保持隐藏）");
                return;
            }

            if (!WaitForCompletion)
            {
                _ = RunMovieBlackBarsAsync(ctx, topRect, bottomRect,
                    topInsideY, topOutsideY, bottomInsideY, bottomOutsideY, transTime, holdTime);
                GD.Print("[Cutscene] MovieBlackBars 异步执行（WaitForCompletion=false）");
                return;
            }

            await RunMovieBlackBarsAsync(ctx, topRect, bottomRect,
                topInsideY, topOutsideY, bottomInsideY, bottomOutsideY, transTime, holdTime);
        }

        /// <summary>
        /// 执行完整的移入→保持→移出三段动画。
        /// </summary>
        private async System.Threading.Tasks.Task RunMovieBlackBarsAsync(
            CutsceneContext ctx,
            Control topRect, Control bottomRect,
            float topInsideY, float topOutsideY,
            float bottomInsideY, float bottomOutsideY,
            float transTime, float holdTime)
        {
            // ── Phase 1: 从屏幕外向内移入 ────────────────────────
            var tweenIn = ctx.Tree.CreateTween();
            tweenIn.TweenProperty(topRect, "position:y", topInsideY, transTime);
            tweenIn.Parallel().TweenProperty(bottomRect, "position:y", bottomInsideY, transTime);

            while (!ctx.IsSkipping && tweenIn.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tweenIn.Kill();
                topRect.Position    = new Vector2(topRect.Position.X, topOutsideY);
                bottomRect.Position = new Vector2(bottomRect.Position.X, bottomOutsideY);
                GD.Print("[Cutscene] MovieBlackBars skip at IN phase，黑条已隐藏");
                return;
            }

            // ── Phase 2: 保持 Duration 秒 ────────────────────────
            if (holdTime > 0f)
            {
                ulong startMs = Godot.Time.GetTicksMsec();
                ulong holdMs  = (ulong)(holdTime * 1000.0);
                while (!ctx.IsSkipping && Godot.Time.GetTicksMsec() - startMs < holdMs)
                    await ctx.NextFrame();
            }

            if (ctx.IsSkipping)
            {
                topRect.Position    = new Vector2(topRect.Position.X, topOutsideY);
                bottomRect.Position = new Vector2(bottomRect.Position.X, bottomOutsideY);
                GD.Print("[Cutscene] MovieBlackBars skip at HOLD phase，黑条已隐藏");
                return;
            }

            // ── Phase 3: 从内向外移出 ────────────────────────────
            var tweenOut = ctx.Tree.CreateTween();
            tweenOut.TweenProperty(topRect, "position:y", topOutsideY, transTime);
            tweenOut.Parallel().TweenProperty(bottomRect, "position:y", bottomOutsideY, transTime);

            while (!ctx.IsSkipping && tweenOut.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
                tweenOut.Kill();

            topRect.Position    = new Vector2(topRect.Position.X, topOutsideY);
            bottomRect.Position = new Vector2(bottomRect.Position.X, bottomOutsideY);
            GD.Print("[Cutscene] MovieBlackBars 完成（移入→保持→移出）");
        }
    }
}
