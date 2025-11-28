using Godot;

namespace Kuros.Core.Interactions.Dialogue
{
    /// <summary>
    /// 表示一段完整的对话，由多行文本组成。
    /// </summary>
    [GlobalClass]
    public partial class DialogueSequence : Resource
    {
        [Export] public string Title { get; set; } = string.Empty;

        [Export] public Godot.Collections.Array<DialogueLine> Lines { get; set; } = new();

        public bool IsEmpty => Lines.Count == 0;
    }
}

