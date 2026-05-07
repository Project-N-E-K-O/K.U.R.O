using Godot;

namespace Kuros.Objects
{
    public partial class TaxiController : CharacterBody2D
    {
        public override void _Ready()
        {
            var animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            if (animPlayer != null)
                animPlayer.AnimationFinished += OnAnimationFinished;
        }

        private void OnAnimationFinished(StringName animName)
        {
            if (animName == "taxi_intro")
            {
                var animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
                animPlayer?.Play("taxi_idle");
            }
        }
    }
}
