using Godot;
using Kuros.Core;
using Kuros.Core.Interactions.Dialogue;

namespace Kuros.Core.Interactions
{
    /// <summary>
    /// 地图互动实体基类，封装交互开关、重复次数与信号，并内置对话触发。
    /// </summary>
    public abstract partial class BaseInteractable : Node2D, IInteractable
    {
        [Signal] public delegate void InteractedEventHandler(GameActor actor);
        [Signal] public delegate void DialogueRequestedEventHandler(DialogueSequence sequence, GameActor actor);

        [Export] public bool IsEnabled { get; set; } = true;
        [Export] public int MaxInteractions { get; set; } = 0; // 0 表示无限

        [ExportGroup("Dialogue")]
        [Export] public DialogueSequence? Dialogue { get; set; }
        [Export] public NodePath DialoguePlayerPath { get; set; } = new();

        private int _interactionCount = 0;
        private IDialoguePlayer? _dialoguePlayer;

        public override void _Ready()
        {
            _dialoguePlayer = ResolveDialoguePlayer();
        }

        public bool CanInteract(GameActor actor)
        {
            if (!IsEnabled) return false;
            if (MaxInteractions > 0 && _interactionCount >= MaxInteractions) return false;
            return OnCanInteract(actor);
        }

        public void Interact(GameActor actor)
        {
            if (!CanInteract(actor)) return;

            _interactionCount++;
            OnInteract(actor);
            EmitSignal(SignalName.Interacted, actor);

            TryPlayDialogue(actor);

            if (MaxInteractions > 0 && _interactionCount >= MaxInteractions)
            {
                OnInteractionLimitReached(actor);
            }
        }

        protected virtual bool OnCanInteract(GameActor actor) => true;

        protected abstract void OnInteract(GameActor actor);

        protected virtual void OnInteractionLimitReached(GameActor actor)
        {
            IsEnabled = false;
        }

        protected virtual void TryPlayDialogue(GameActor actor)
        {
            if (Dialogue == null || Dialogue.IsEmpty) return;

            var player = _dialoguePlayer ??= ResolveDialoguePlayer();
            if (player != null && player.CanPlayDialogue(Dialogue, actor))
            {
                player.PlayDialogue(Dialogue, actor, this);
            }

            EmitSignal(SignalName.DialogueRequested, Dialogue, actor);
        }

        private IDialoguePlayer? ResolveDialoguePlayer()
        {
            Node? target = null;
            if (DialoguePlayerPath.GetNameCount() > 0)
            {
                target = GetNodeOrNull(DialoguePlayerPath);
            }

            return target as IDialoguePlayer;
        }
    }
}

