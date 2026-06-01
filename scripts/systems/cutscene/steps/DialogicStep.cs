using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 在过场序列中播放一段 Dialogic Timeline。
    ///
    /// 使用方式：
    ///   TimelinePath → "res://dialogic/timeline/xxx.dtl"（或 Timeline 名称）
    ///   CharacterPath → （可选）角色资源路径，用于气泡对话框定位
    ///   BubbleAnchorPath → 气泡锚点节点路径（配合 CharacterPath 使用）
    ///
    /// 行为：
    ///   - Execute() 启动 Timeline，并等待 timeline_ended 信号
    ///   - IsSkipping 时立即调用 end_timeline() 结束对话
    /// </summary>
    [GlobalClass]
    public partial class DialogicStep : CutsceneStep
    {
        [Export(PropertyHint.File, "*.dtl")] public string TimelinePath { get; set; } = "";

        /// <summary>
        /// Dialogic 样式名称（在 Dialogic 编辑器 → Styles 中设置的名字）。
        /// 留空则使用项目默认样式（通常是全屏 VN 对话框，会遮住游戏画面）。
        /// 过场对话推荐填 "textbubble_A" 以使用世界空间气泡，不遮挡角色。
        /// </summary>
        [Export] public string StyleName { get; set; } = "textbubble_A";

        /// <summary>
        /// （可选）角色资源路径（.dch），填写后气泡对话框会跟随 BubbleAnchorPath 节点显示。
        /// </summary>
        [Export(PropertyHint.File, "*.dch")] public string CharacterPath { get; set; } = "";

        /// <summary>
        /// 气泡对话框锚点的绝对节点路径（需从场景根可访问）。
        /// 例如："/root/Stage_1/NPC/Marker2D"
        /// </summary>
        [Export] public NodePath BubbleAnchorPath { get; set; } = new NodePath();

        /// <summary>
        /// true（默认）：阻塞执行，等待 timeline_ended 后才执行下一步。
        /// false：启动 Timeline 后立即返回，对话在后台继续，过场序列继续推进。
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = true;

        public override async Task Execute(CutsceneContext ctx)
        {
            if (string.IsNullOrEmpty(TimelinePath))
            {
                GD.PushError("[DialogicStep] TimelinePath 未设置！");
                return;
            }

            if (ctx.IsSkipping)
            {
                GD.Print("[DialogicStep] 跳过（IsSkipping）");
                return;
            }

            var dialogic = ctx.Manager.GetNodeOrNull("/root/Dialogic");
            if (dialogic == null)
            {
                GD.PushError("[DialogicStep] 找不到 Dialogic autoload！请确认 project.godot 中已启用 Dialogic。");
                return;
            }

            bool timelineEnded = false;

            // 监听 timeline_ended 信号
            Callable onEnded = Callable.From(() => { timelineEnded = true; });
            dialogic.Connect("timeline_ended", onEnded, (uint)GodotObject.ConnectFlags.OneShot);

            // 切换到指定样式（在 start() 之前），防止使用全屏默认样式遮挡游戏画面
            if (!string.IsNullOrWhiteSpace(StyleName))
            {
                var styles = dialogic.Get("Styles").AsGodotObject();
                if (styles != null)
                    styles.Call("load_style", StyleName);
            }

            // 启动 Timeline
            GD.Print($"[DialogicStep] 启动 Timeline: {TimelinePath}");
            var layoutVariant = dialogic.Call("start", TimelinePath);

            // 注册气泡锚点（如有配置）
            if (!string.IsNullOrEmpty(CharacterPath)
                && !BubbleAnchorPath.IsEmpty
                && layoutVariant.AsGodotObject() is Node layoutNode)
            {
                var anchor = ctx.Manager.GetNodeOrNull<Node2D>(BubbleAnchorPath);
                if (anchor != null)
                    layoutNode.CallDeferred("register_character", CharacterPath, anchor);
                else
                    GD.PushWarning($"[DialogicStep] 找不到气泡锚点节点：{BubbleAnchorPath}");
            }

            // 非阻塞模式：启动后立即返回，对话在后台继续
            if (!WaitForCompletion)
            {
                GD.Print($"[DialogicStep] 非阻塞模式，Timeline 已启动: {TimelinePath}");
                return;
            }

            // 阻塞模式：等待 timeline_ended 或 skip
            while (!timelineEnded && !ctx.IsSkipping)
                await ctx.NextFrame();

            // 若被 skip，强制结束 Timeline
            if (ctx.IsSkipping && !timelineEnded)
            {
                // 断开信号防止重复触发
                if (dialogic.IsConnected("timeline_ended", onEnded))
                    dialogic.Disconnect("timeline_ended", onEnded);

                if (dialogic.HasMethod("end_timeline"))
                {
                    dialogic.Call("end_timeline");
                    GD.Print("[DialogicStep] 被 skip，已调用 end_timeline");
                }
            }

            GD.Print($"[DialogicStep] Timeline 结束: {TimelinePath}");
        }
    }
}
