using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 播放指定节点上的 AnimationPlayer 动画。
    /// AnimationPlayerPath 相对于 CutsceneManager 所在节点。
    /// </summary>
    [GlobalClass]
    public partial class PlayAnimationStep : CutsceneStep
    {
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath();

        [Export] public string AnimationName { get; set; } = "";

        /// <summary>是否等待动画播放完毕再进行下一步。</summary>
        [Export] public bool WaitForCompletion { get; set; } = true;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] PlayAnimationStep 开始，AnimationPlayerPath={AnimationPlayerPath}, AnimationName={AnimationName}");

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

            GD.Print($"[Cutscene] PlayAnimationStep: 播放动画 {AnimationName}，WaitForCompletion={WaitForCompletion}");
            animPlayer.Play(AnimationName);

            if (!WaitForCompletion)
            {
                GD.Print("[Cutscene] PlayAnimationStep: 不等待完成，直接继续");
                return;
            }

            while (!ctx.IsSkipping && animPlayer.IsPlaying())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
                animPlayer.Seek(animPlayer.CurrentAnimationLength, true);

            GD.Print("[Cutscene] PlayAnimationStep 完成");
        }
    }
}
