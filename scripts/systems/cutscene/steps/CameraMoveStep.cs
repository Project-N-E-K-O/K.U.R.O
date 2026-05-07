using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 将摄像机移动到指定世界坐标（或节点位置）。
    /// 需要 CutsceneSequence.TakeOverCamera = true，且 CutsceneManager.CameraPath 已设置。
    /// </summary>
    [GlobalClass]
    public partial class CameraMoveStep : CutsceneStep
    {
        /// <summary>目标节点路径（相对于 CutsceneManager）。若为空则使用 TargetPosition。</summary>
        [Export] public NodePath TargetPath { get; set; } = new NodePath();

        /// <summary>目标世界坐标（TargetPath 为空时使用）。</summary>
        [Export] public Vector2 TargetPosition { get; set; }

        [Export] public float Duration { get; set; } = 1.0f;

        [Export] public Tween.EaseType Ease { get; set; } = Tween.EaseType.InOut;

        [Export] public Tween.TransitionType Transition { get; set; } = Tween.TransitionType.Cubic;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] CameraMoveStep 开始，TargetPath={TargetPath}, TargetPosition={TargetPosition}, Duration={Duration}");
            var camera = ctx.Manager.Camera;
            if (camera == null)
            {
                GD.PrintErr("[Cutscene] CameraMoveStep: Camera 为 null，请检查 CutsceneManager.CameraPath，且 TakeOverCamera=true");
                return;
            }

            var target = TargetPosition;
            if (!TargetPath.IsEmpty)
            {
                var node = ctx.Manager.GetNodeOrNull<Node2D>(TargetPath);
                if (node != null)
                {
                    target = node.GlobalPosition;
                    GD.Print($"[Cutscene] CameraMoveStep: 目标节点 {node.Name}，GlobalPosition={target}");
                }
                else
                {
                    GD.PrintErr($"[Cutscene] CameraMoveStep: TargetPath='{TargetPath}' 节点未找到，将使用 TargetPosition={TargetPosition}");
                }
            }

            GD.Print($"[Cutscene] CameraMoveStep: Camera.TopLevel={camera.TopLevel}，从 {camera.GlobalPosition} 移动到 {target}");

            if (ctx.IsSkipping || Duration <= 0f)
            {
                camera.GlobalPosition = target;
                GD.Print("[Cutscene] CameraMoveStep 立即完成");
                return;
            }

            var tween = camera.CreateTween();
            tween.TweenProperty(camera, "global_position", target, Duration)
                 .SetEase(Ease).SetTrans(Transition);

            while (!ctx.IsSkipping && tween.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tween.Kill();
                camera.GlobalPosition = target;
            }
            GD.Print($"[Cutscene] CameraMoveStep 完成，Camera.GlobalPosition={camera.GlobalPosition}");
        }
    }
}
