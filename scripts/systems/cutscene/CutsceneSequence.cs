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

        /// <summary>过场期间接管摄像机（需要 CutsceneManager 设置 CameraPath）。</summary>
        [Export] public bool TakeOverCamera { get; set; } = false;
    }
}
