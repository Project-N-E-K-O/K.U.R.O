using Godot;
using Godot.Collections;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 一段完整的过场动画序列（Resource），可在编辑器中配置步骤列表。
    /// </summary>
    [GlobalClass]
    public partial class CutsceneSequence : Resource
    {
        /// <summary>唯一 ID，用于信号区分。</summary>
        [Export] public string SequenceId { get; set; } = "";

        /// <summary>步骤列表，按顺序执行。</summary>
        [Export] public Array<CutsceneStep> Steps { get; set; } = new();

        /// <summary>过场期间禁用玩家输入。</summary>
        [Export] public bool DisablePlayerInput { get; set; } = true;

        /// <summary>过场期间隐藏玩家（与 <see cref="DisablePlayerInput"/> 独立：可以只禁输入不隐藏，或只隐藏不禁用）。</summary>
        [Export] public bool HidePlayer { get; set; } = true;

        /// <summary>过场期间隐藏（并禁用 ProcessMode）的节点路径列表，路径相对 CutsceneManager。
        /// **逐段过场独立配置**：不配 = 这段过场不隐藏任何节点——同一舞台里"要藏 P2 的开场"与
        /// "不藏 P2 的开场"因此可以并存（旧实现只有舞台级一份，做不到）。</summary>
        [Export] public Array<NodePath> HideNodePaths { get; set; } = new();

        /// <summary>过场期间接管摄像机（需要 CutsceneManager 设置 CameraPath）。</summary>
        [Export] public bool TakeOverCamera { get; set; } = false;

        /// <summary>接管期间关闭拖拽边距和引擎位置平滑，由过场步骤控制构图与缓动。</summary>
        [Export] public bool PreciseCamera { get; set; }
    }
}
