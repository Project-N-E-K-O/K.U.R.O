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

            animPlayer.Play(AnimationName);
            GD.Print($"[Cutscene] PlayAnimationStep: 已播放动画 {AnimationName}");

            if (!WaitForCompletion)
            {
                // 后台监听skip
                _ = MonitorSkipAndFinishAsync(ctx, animPlayer);
                GD.Print("[Cutscene] PlayAnimationStep 异步执行（WaitForCompletion=false），动画在后台进行");
                return;
            }

            // 阻塞等待动画完成
            while (!ctx.IsSkipping && animPlayer.IsPlaying())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
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
            while (!ctx.IsSkipping && animPlayer.IsPlaying())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                animPlayer.Seek(animPlayer.CurrentAnimationLength, true);
                GD.Print("[Cutscene] PlayAnimationStep 被skip，快进到结尾");
            }
        }
    }
}
