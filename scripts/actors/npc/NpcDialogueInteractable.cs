using Godot;
using Kuros.Core;
using Kuros.Core.Interactions;

namespace Kuros.Actors.Npc
{
    /// <summary>
    /// 可互动 NPC 示例：仅触发对话，可自定义动画或任务逻辑。
    /// </summary>
    public partial class NpcDialogueInteractable : BaseInteractable
    {
        [Export] public string IdleAnimation { get; set; } = "Idle";
        [Export] public string TalkAnimation { get; set; } = "Talk";

        private AnimationPlayer? _animationPlayer;

        public override void _Ready()
        {
            base._Ready();
            _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            PlayAnimation(IdleAnimation);
        }

        protected override void OnInteract(GameActor actor)
        {
            PlayAnimation(TalkAnimation);
        }

        protected override void OnInteractionLimitReached(GameActor actor)
        {
            base.OnInteractionLimitReached(actor);
            PlayAnimation(IdleAnimation);
        }

        private void PlayAnimation(string animationName)
        {
            if (_animationPlayer == null || string.IsNullOrEmpty(animationName)) return;
            if (_animationPlayer.HasAnimation(animationName))
            {
                _animationPlayer.Play(animationName);
            }
        }
    }
}

