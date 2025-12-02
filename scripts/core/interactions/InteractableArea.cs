using Godot;
using Kuros.Core;

namespace Kuros.Core.Interactions
{
    /// <summary>
    /// 可挂载在地图场景中的互动触发器，检测玩家进入并调用 IInteractable。
    /// </summary>
    [GlobalClass]
    public partial class InteractableArea : Area2D
    {
        [Export] public NodePath InteractableTargetPath { get; set; } = new();
        [Export] public bool RequireActionPress { get; set; } = true;
        [Export] public string ActionName { get; set; } = "interact";
        [Export] public bool HighlightFocusedActor { get; set; } = false;

        private IInteractable? _interactable;
        private GameActor? _focusedActor;
        private Color _originalActorModulate = Colors.White;
        private bool _cachedOriginalModulate = false;

        public override void _Ready()
        {
            BodyEntered += OnBodyEntered;
            BodyExited += OnBodyExited;
            _interactable = ResolveInteractable();
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            if (!RequireActionPress || _focusedActor == null || _interactable == null)
            {
                return;
            }

            if (Input.IsActionJustPressed(ActionName))
            {
                TryInteract(_focusedActor);
            }
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is not GameActor actor) return;
            
            // 只有在没有聚焦 actor 时才设置新的，避免多人模式下焦点被抢夺
            if (_focusedActor != null) return;
            
            _focusedActor = actor;
            UpdateHighlight(true);

            if (!_cachedOriginalModulate)
            {
                _originalActorModulate = actor.Modulate;
                _cachedOriginalModulate = true;
            }

            if (!RequireActionPress)
            {
                TryInteract(actor);
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (body == _focusedActor)
            {
                UpdateHighlight(false);
                _focusedActor = null;
                _cachedOriginalModulate = false;
            }
        }

        private void UpdateHighlight(bool focused)
        {
            if (!HighlightFocusedActor || _focusedActor == null) return;
            _focusedActor.Modulate = focused ? new Color(1f, 1f, 0.7f) : _originalActorModulate;
        }

        private void TryInteract(GameActor actor)
        {
            if (_interactable == null) return;
            if (!_interactable.CanInteract(actor)) return;
            _interactable.Interact(actor);
        }

        private IInteractable? ResolveInteractable()
        {
            Node? target = null;
            if (InteractableTargetPath.GetNameCount() > 0)
            {
                target = GetNodeOrNull(InteractableTargetPath);
            }

            target ??= GetParent();
            return target as IInteractable;
        }
    }
}

