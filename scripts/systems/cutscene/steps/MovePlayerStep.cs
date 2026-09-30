using System.Threading.Tasks;
using Godot;
using Kuros.Managers;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 把**玩家**移动到指定节点的位置（同一场景内换房间 / 换站位用）。
    /// 目标按 NodePath 解析（相对 CutsceneManager，与 CameraMoveStep 一致）；TargetPath 为空时用 TargetPosition。
    ///
    /// - Duration &lt;= 0 或 Instant → **瞬移**；
    /// - 平滑模式：执行期间临时把玩家 ProcessMode 置 Disabled（冻结其状态机与物理），每帧由 Tween 推
    ///   GlobalPosition，结束后**还原玩家原来的 ProcessMode**——避免玩家的移动组件/状态机与本次位移抢速度。
    ///   序列本身若开了 DisablePlayerInput（同样是 Disabled），本步原样还原、不会把它重新打开。
    /// - 跳过（ctx.IsSkipping）= 瞬时落终态：直接瞬移到位（与其它步骤的约定一致）。
    /// - 长距离瞬移请配 FadeStep 遮一下：相机是平滑跟随，会一路扫过地图。
    /// </summary>
    [GlobalClass]
    public partial class MovePlayerStep : CutsceneStep
    {
        /// <summary>目标节点路径（相对于 CutsceneManager）。</summary>
        [Export] public NodePath TargetPath { get; set; } = new NodePath();

        /// <summary>目标世界坐标（TargetPath 为空或解析失败时使用）。</summary>
        [Export] public Vector2 TargetPosition { get; set; }

        /// <summary>落点微调（世界坐标增量）。</summary>
        [Export] public Vector2 Offset { get; set; }

        /// <summary>只改 X、保留玩家当前 Y（走廊 / 平台换房间常用）。</summary>
        [Export] public bool KeepPlayerY { get; set; } = false;

        /// <summary>立即完成（无平滑）。</summary>
        [Export] public bool Instant { get; set; } = false;

        /// <summary>平滑时长（秒）；&lt;= 0 或 Instant → 瞬移。</summary>
        [Export(PropertyHint.Range, "0,10,0.05")] public float Duration { get; set; } = 0f;

        [Export] public Tween.EaseType Ease { get; set; } = Tween.EaseType.InOut;
        [Export] public Tween.TransitionType Transition { get; set; } = Tween.TransitionType.Cubic;

        /// <summary>平滑期间冻结玩家（ProcessMode=Disabled），结束后还原——防止移动组件抢速度。</summary>
        [Export] public bool FreezeDuringMove { get; set; } = true;

        /// <summary>移动完成后让相机**立即对位**（CameraFollow.SnapToTarget：目标+Offset 并做地图边界钳制），
        /// 而不是平滑追过去——长距离瞬移时避免镜头扫过整张图。
        /// 只在"瞬移"与"被跳过"两种落终态时生效；平滑移动过程中相机会自然跟随，不需要也不该 snap。
        /// 序列若接管了相机（TakeOverCamera=true）则自动跳过，免得与过场镜头控制打架。</summary>
        [Export] public bool SnapCamera { get; set; } = true;

        public override async Task Execute(CutsceneContext ctx)
        {
            var player = ctx.Manager.Player;
            if (player == null)
            {
                GD.PrintErr("[Cutscene] MovePlayerStep: Player 为 null，请检查 CutsceneManager.PlayerPath");
                return;
            }

            Vector2 destination = TargetPosition + Offset;
            if (!TargetPath.IsEmpty)
            {
                var targetNode = ctx.Manager.GetNodeOrNull<Node2D>(TargetPath);
                if (targetNode != null)
                {
                    destination = targetNode.GlobalPosition + Offset;
                }
                else
                {
                    GD.PrintErr($"[Cutscene] MovePlayerStep: TargetPath='{TargetPath}' 节点未找到，改用 TargetPosition={TargetPosition}");
                }
            }

            if (KeepPlayerY)
                destination.Y = player.GlobalPosition.Y;

            // 瞬移（含"跳过的过场"：瞬时落终态）
            if (ctx.IsSkipping || Instant || Duration <= 0f)
            {
                player.GlobalPosition = destination;
                ZeroVelocity(player);
                SnapCameraIfNeeded(ctx);
                GD.Print($"[Cutscene] MovePlayerStep 瞬移完成 → {destination}");
                return;
            }

            // 平滑：冻结玩家（保存原值后还原，不硬编码 Inherit——序列可能本来就把玩家禁用了）
            var savedProcessMode = player.ProcessMode;
            if (FreezeDuringMove)
                player.ProcessMode = Node.ProcessModeEnum.Disabled;

            GD.Print($"[Cutscene] MovePlayerStep 开始：{player.GlobalPosition} → {destination}（{Duration}s，冻结={FreezeDuringMove}）");
            try
            {
                var tween = player.CreateTween();
                tween.TweenProperty(player, "global_position", destination, Duration)
                     .SetEase(Ease).SetTrans(Transition);

                while (!ctx.IsSkipping && tween.IsRunning())
                    await ctx.NextFrame();

                tween.Kill();
                if (GodotObject.IsInstanceValid(player))
                {
                    player.GlobalPosition = destination;   // 正常结束或跳过：都落在终点
                    ZeroVelocity(player);
                    if (ctx.IsSkipping) SnapCameraIfNeeded(ctx);   // 平滑途中被跳过 = 瞬移落点，镜头也要立刻对位
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(player))
                    player.ProcessMode = savedProcessMode;
            }

            GD.Print($"[Cutscene] MovePlayerStep 完成 → {destination}");
        }

        /// <summary>相机立即对位（无平滑、含地图边界钳制）：走 CameraFollow.SnapToTarget。
        /// 优先取 CutsceneManager 上配的相机，其次取视口当前相机；两者都不是 CameraFollow 时静默跳过。</summary>
        private void SnapCameraIfNeeded(CutsceneContext ctx)
        {
            if (!SnapCamera) return;
            if (ctx.Sequence.TakeOverCamera) return;   // 序列接管相机时不抢镜头

            var camera = ctx.Manager.Camera as CameraFollow
                ?? ctx.Tree.Root.GetViewport()?.GetCamera2D() as CameraFollow;
            if (camera == null) return;

            camera.SnapToTarget();
        }

        /// <summary>清掉残余速度：冻结期间物理没跑，解冻后若还带着旧速度会立刻冲出去。</summary>
        private static void ZeroVelocity(Node2D player)
        {
            if (player is CharacterBody2D body)
                body.Velocity = Vector2.Zero;
        }
    }
}
