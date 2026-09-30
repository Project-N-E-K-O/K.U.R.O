using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 播放指定节点上的 AnimationPlayer 动画。
    /// AnimationPlayerPath 相对于 CutsceneManager 所在节点。
    /// 支持异步模式：WaitForCompletion=false 时不阻塞后续步骤。
    /// </summary>
    [GlobalClass]
    public partial class PlayAnimationStep : CutsceneStep
    {
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath();

        [Export] public string AnimationName { get; set; } = "";

        /// <summary>
        /// 是否等待动画播放完毕。
        /// true（默认）：阻塞执行，等动画播完才执行下一步。
        /// false：启动动画后立即返回，动画在后台进行。
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = false;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] PlayAnimationStep 开始，AnimationPlayerPath={AnimationPlayerPath}, AnimationName={AnimationName}, WaitForCompletion={WaitForCompletion}");

            if (AnimationPlayerPath.IsEmpty || string.IsNullOrEmpty(AnimationName))
            {
                GD.PrintErr("[Cutscene] PlayAnimationStep: AnimationPlayerPath 或 AnimationName 未设置");
                return;
            }

            var animPlayer = ctx.Manager.GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
            if (animPlayer == null)
            {
                GD.PrintErr($"[Cutscene] PlayAnimationStep: 路径 '{AnimationPlayerPath}' 未找到 AnimationPlayer 节点");
                return;
            }
            if (!animPlayer.HasAnimation(AnimationName))
            {
                GD.PrintErr($"[Cutscene] PlayAnimationStep: AnimationPlayer '{animPlayer.Name}' 中不存在动画 '{AnimationName}'");
                return;
            }

            // 解冻：别的系统会把本播放器的 SpeedScale 压到 0 —— 典型是 KillProgressAscendController
            // （到站过场开始时把上升循环 up_loop 的速度置 0，让电梯停住）。SpeedScale=0 时 Play() 会
            // "在播但时间不走"：画面永远停在第一帧，且 IsPlaying() 恒为真 → 下面 WaitForCompletion 的
            // 等待循环永久挂起（跳过能用是因为 skip 走 Seek(末尾)，不受 SpeedScale 影响）。
            // 本步骤是被显式要求播这个动画的，所以先把速度解开；播完怎么收尾由调用方决定。
            if (animPlayer.SpeedScale <= 0f)
            {
                GD.Print($"[Cutscene] PlayAnimationStep: 播放器 SpeedScale={animPlayer.SpeedScale}（被外部置 0/负）→ 解冻为 1");
                animPlayer.SpeedScale = 1f;
            }

            animPlayer.Play(AnimationName);
            GD.Print($"[Cutscene] PlayAnimationStep: 已播放动画 {AnimationName}");

            if (!WaitForCompletion)
            {
                // 后台监听skip
                _ = MonitorSkipAndFinishAsync(ctx, animPlayer);
                GD.Print("[Cutscene] PlayAnimationStep 异步执行（WaitForCompletion=false），动画在后台进行");
                return;
            }

            // 阻塞等待动画完成。必须判存活：节点可能在动画里自毁（method 轨道调 DestroySelf 等），
            // 对已释放的 GodotObject 调 IsPlaying() 会抛 ObjectDisposedException，把这一步打断。
            while (!ctx.IsSkipping && GodotObject.IsInstanceValid(animPlayer) && animPlayer.IsPlaying())
                await ctx.NextFrame();

            if (!GodotObject.IsInstanceValid(animPlayer))
            {
                GD.Print("[Cutscene] PlayAnimationStep: 目标节点已销毁（动画中自毁），视为动画结束");
            }
            else if (ctx.IsSkipping)
            {
                animPlayer.Seek(animPlayer.CurrentAnimationLength, true);
                GD.Print("[Cutscene] PlayAnimationStep 被skip，动画快进到结尾");
            }

            GD.Print("[Cutscene] PlayAnimationStep 完成");
        }

        /// <summary>
        /// 后台监听skip事件，如果发生skip则快进动画到结尾
        /// </summary>
        private async Task MonitorSkipAndFinishAsync(CutsceneContext ctx, AnimationPlayer animPlayer)
        {
            // 同阻塞分支：节点自毁后立即收工（对已释放对象调 IsPlaying() 会抛异常）
            while (!ctx.IsSkipping && GodotObject.IsInstanceValid(animPlayer) && animPlayer.IsPlaying())
                await ctx.NextFrame();

            if (ctx.IsSkipping && GodotObject.IsInstanceValid(animPlayer))
            {
                animPlayer.Seek(animPlayer.CurrentAnimationLength, true);
                GD.Print("[Cutscene] PlayAnimationStep 被skip，快进到结尾");
            }
        }
    }
}
