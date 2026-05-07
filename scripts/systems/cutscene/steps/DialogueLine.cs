using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 对话框的单条台词（说话人 + 文字 + 显示速度）。
    /// </summary>
    [GlobalClass]
    public partial class DialogueLine : Resource
    {
        [Export] public string Speaker { get; set; } = "";

        [Export(PropertyHint.MultilineText)]
        public string Text { get; set; } = "";

        /// <summary>每秒显示的字符数，0 = 立即显示全部。</summary>
        [Export] public float RevealSpeed { get; set; } = 40f;
    }
}
