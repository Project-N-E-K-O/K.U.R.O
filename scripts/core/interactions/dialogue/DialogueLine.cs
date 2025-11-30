using Godot;

namespace Kuros.Core.Interactions.Dialogue
{
    /// <summary>
    /// 对话中的单句文本。
    /// </summary>
    [GlobalClass]
    public partial class DialogueLine : Resource
    {
        [Export] public string Speaker { get; set; } = string.Empty;

        [Export(PropertyHint.MultilineText)]
        public string Text { get; set; } = string.Empty;

        [Export] public Texture2D? Portrait { get; set; }
    }
}

