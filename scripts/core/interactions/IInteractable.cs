using Kuros.Core;

namespace Kuros.Core.Interactions
{
    /// <summary>
    /// 统一的互动接口，实现方可被玩家等交互体触发。
    /// </summary>
    public interface IInteractable
    {
        bool CanInteract(GameActor actor);

        void Interact(GameActor actor);
    }
}

