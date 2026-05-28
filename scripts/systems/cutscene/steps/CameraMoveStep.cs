using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 将摄像机移动到指定世界坐标（或节点位置）并支持缩放。
    /// 需要 CutsceneSequence.TakeOverCamera = true，且 CutsceneManager.CameraPath 已设置。
    /// 
    /// 功能：
    /// - 位置移动：从当前位置移动到 TargetPosition（或目标节点位置）
    /// - 缩放变换：从当前 Zoom 变换到 TargetZoom（若 TargetZoom != 1）
    /// - 跟随模式：FollowDuration > 0 时，先做初始移动（Duration 秒），再持续每帧同步到目标 GlobalPosition
    /// - 异步模式：WaitForCompletion=false 时不阻塞后续步骤
    /// </summary>
    [GlobalClass]
    public partial class CameraMoveStep : CutsceneStep
    {
        /// <summary>目标节点路径（相对于 CutsceneManager）。若为空则使用 TargetPosition。</summary>
        [Export] public NodePath TargetPath { get; set; } = new NodePath();

        /// <summary>目标世界坐标（TargetPath 为空时使用）。</summary>
        [Export] public Vector2 TargetPosition { get; set; }

        /// <summary>目标缩放级别。1.0 = 100%（无缩放），0.5 = 50%（放大），2.0 = 200%（缩小）。若为 <= 0，则不改变缩放。</summary>
        [Export] public float TargetZoom { get; set; } = 0f;

        [Export] public float Duration { get; set; } = 1.0f;

        [Export] public Tween.EaseType Ease { get; set; } = Tween.EaseType.InOut;

        [Export] public Tween.TransitionType Transition { get; set; } = Tween.TransitionType.Cubic;

        /// <summary>
        /// 是否等待整个过程完成。
        /// true（默认）：阻塞执行，等待移动/缩放完成后才执行下一步。
        /// false：启动动画后立即返回，动画在后台进行。
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = false;

        /// <summary>
        /// 跟随目标节点的持续时间（秒）。大于 0 时进入跟随模式：
        /// 先用 Duration 秒做初始移动，然后每帧持续同步摄像机到目标的 GlobalPosition，直到经过 FollowDuration 秒。
        /// 需配合有效的 TargetPath 使用。WaitForCompletion=false 时在后台运行，可与其他 Step 同时执行。
        /// </summary>
        [Export] public float FollowDuration { get; set; } = 0f;

        /// <summary>
        /// 跟随结束后摄像机的行为。
        /// true（默认）：保留在跟随结束瞬间目标所在的位置。
        /// false：回到跟随开始前摄像机的原始位置。
        /// </summary>
        [Export] public bool StayAtTargetAfterFollow { get; set; } = true;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] CameraMoveStep 开始，TargetPath={TargetPath}, TargetPosition={TargetPosition}, TargetZoom={TargetZoom}, Duration={Duration}, FollowDuration={FollowDuration}, WaitForCompletion={WaitForCompletion}");
            var camera = ctx.Manager.Camera;
            if (camera == null)
            {
                GD.PrintErr("[Cutscene] CameraMoveStep: Camera 为 null，请检查 CutsceneManager.CameraPath，且 TakeOverCamera=true");
                return;
            }

            Node2D? targetNode = null;
            var target = TargetPosition;
            if (!TargetPath.IsEmpty)
            {
                targetNode = ctx.Manager.GetNodeOrNull<Node2D>(TargetPath);
                if (targetNode != null)
                {
                    target = targetNode.GlobalPosition;
                    GD.Print($"[Cutscene] CameraMoveStep: 目标节点 {targetNode.Name}，GlobalPosition={target}");
                }
                else
                {
                    GD.PrintErr($"[Cutscene] CameraMoveStep: TargetPath='{TargetPath}' 节点未找到，将使用 TargetPosition={TargetPosition}");
                }
            }

            GD.Print($"[Cutscene] CameraMoveStep: Camera.TopLevel={camera.TopLevel}，从 {camera.GlobalPosition} 移动到 {target}，Zoom: {camera.Zoom} → {(TargetZoom > 0 ? TargetZoom : "不变")}");

            // ── 跟随模式：先初始移动，再持续同步目标 GlobalPosition ──────────────
            if (FollowDuration > 0f && targetNode != null)
            {
                if (!WaitForCompletion)
                {
                    _ = RunFollowAsync(ctx, camera, targetNode);
                    GD.Print($"[Cutscene] CameraMoveStep 跟随模式：异步执行 FollowDuration={FollowDuration}s");
                    return;
                }
                await RunFollowAsync(ctx, camera, targetNode);
                return;
            }

            // ── 普通单次移动模式 ─────────────────────────────────────────────────
            if (ctx.IsSkipping || Duration <= 0f)
            {
                camera.GlobalPosition = target;
                if (TargetZoom > 0f)
                    camera.Zoom = new Vector2(TargetZoom, TargetZoom);
                GD.Print("[Cutscene] CameraMoveStep 立即完成");
                return;
            }

            // 创建 Tween 进行位置和缩放动画
            var tween = camera.CreateTween();
            tween.TweenProperty(camera, "global_position", target, Duration)
                 .SetEase(Ease).SetTrans(Transition);

            // 如果需要缩放，并行执行缩放动画
            if (TargetZoom > 0f)
            {
                tween.Parallel().TweenProperty(camera, "zoom", new Vector2(TargetZoom, TargetZoom), Duration)
                     .SetEase(Ease).SetTrans(Transition);
            }

            // 若不等待完成，立即返回，但后台仍监听skip事件
            if (!WaitForCompletion)
            {
                _ = MonitorSkipAndFinishAsync(ctx, tween, camera, target);
                GD.Print($"[Cutscene] CameraMoveStep 异步执行（WaitForCompletion=false），总耗时约 {Duration} 秒");
                return;
            }

            // 否则阻塞等待整个过程完成或skip
            while (!ctx.IsSkipping && tween.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tween.Kill();
                camera.GlobalPosition = target;
                if (TargetZoom > 0f)
                    camera.Zoom = new Vector2(TargetZoom, TargetZoom);
            }
            GD.Print($"[Cutscene] CameraMoveStep 完成，Camera.GlobalPosition={camera.GlobalPosition}, Zoom={camera.Zoom}");
        }

        /// <summary>
        /// 跟随模式：先做初始 Tween 移动到目标当前位置，再每帧持续同步，直到 FollowDuration 结束。
        /// </summary>
        private async Task RunFollowAsync(CutsceneContext ctx, Camera2D camera, Node2D targetNode)
        {
            // 记录跟随开始前的摄像机位置，用于 StayAtTargetAfterFollow=false 时还原
            var posBeforeFollow = camera.GlobalPosition;

            // Phase 1: 初始 Tween 移动到目标当前位置
            if (Duration > 0f && !ctx.IsSkipping)
            {
                var tweenInit = camera.CreateTween();
                tweenInit.TweenProperty(camera, "global_position", targetNode.GlobalPosition, Duration)
                         .SetEase(Ease).SetTrans(Transition);
                if (TargetZoom > 0f)
                    tweenInit.Parallel().TweenProperty(camera, "zoom", new Vector2(TargetZoom, TargetZoom), Duration)
                             .SetEase(Ease).SetTrans(Transition);

                while (!ctx.IsSkipping && tweenInit.IsRunning())
                    await ctx.NextFrame();

                if (ctx.IsSkipping)
                {
                    tweenInit.Kill();
                    GD.Print("[Cutscene] CameraMoveStep 跟随模式 skip at 初始移动阶段");
                    return;
                }
            }
            else if (TargetZoom > 0f)
            {
                camera.Zoom = new Vector2(TargetZoom, TargetZoom);
            }

            // Phase 2: 每帧同步 GlobalPosition，持续 FollowDuration 秒
            ulong startMs = Godot.Time.GetTicksMsec();
            ulong durationMs = (ulong)(FollowDuration * 1000.0);
            while (!ctx.IsSkipping && Godot.Time.GetTicksMsec() - startMs < durationMs)
            {
                if (GodotObject.IsInstanceValid(targetNode))
                    camera.GlobalPosition = targetNode.GlobalPosition;
                await ctx.NextFrame();
            }

            if (GodotObject.IsInstanceValid(targetNode))
                camera.GlobalPosition = targetNode.GlobalPosition;

            if (!StayAtTargetAfterFollow)
            {
                camera.GlobalPosition = posBeforeFollow;
                GD.Print($"[Cutscene] CameraMoveStep 跟随结束，已还原至原始位置 {posBeforeFollow}");
            }
            else
            {
                GD.Print($"[Cutscene] CameraMoveStep 跟随完成，Camera.GlobalPosition={camera.GlobalPosition}");
            }
        }

        /// <summary>
        /// 后台监听skip事件，如果发生skip则立刻跳到最终位置和缩放
        /// </summary>
        private async Task MonitorSkipAndFinishAsync(CutsceneContext ctx, Tween tween, Camera2D camera, Vector2 targetPosition)
        {
            while (!ctx.IsSkipping && tween.IsRunning())
                await ctx.NextFrame();

            if (ctx.IsSkipping)
            {
                tween.Kill();
                camera.GlobalPosition = targetPosition;
                if (TargetZoom > 0f)
                    camera.Zoom = new Vector2(TargetZoom, TargetZoom);
                GD.Print("[Cutscene] CameraMoveStep 被skip，立刻完成移动和缩放");
            }
        }
    }
}
