using Godot;
using Kuros.Core;

namespace Kuros.Core.Interactions.Dialogue
{
    /// <summary>
    /// 由 UI 或对话系统实现，负责展示对话。
    /// </summary>
    public interface IDialoguePlayer
    {
        bool CanPlayDialogue(DialogueSequence sequence, GameActor actor);

        void PlayDialogue(DialogueSequence sequence, GameActor actor, Node source);
    }
}

