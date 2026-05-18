using Godot;
using Kuros.Systems.Cutscene;

namespace Kuros.Objects
{
    public partial class TaxiController : CharacterBody2D
    {
        private AnimationPlayer? _animPlayer;
        private CutsceneManager? _cutsceneManager;
        private bool _hasSkipped = false;

        public override void _Ready()
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            if (_animPlayer != null)
                _animPlayer.AnimationFinished += OnAnimationFinished;

            // 获取CutsceneManager（通过group）
            var managers = GetTree().GetNodesInGroup("cutscene_manager");
            if (managers.Count > 0)
                _cutsceneManager = managers[0] as CutsceneManager;
        }

        public override void _Process(double delta)
        {
            // 当skip发生时，立刻跳到taxi_idle
            if (_cutsceneManager != null && _cutsceneManager.IsSkipRequested && !_hasSkipped)
            {
                _hasSkipped = true;
                _animPlayer?.Play("taxi_idle");
            }
        }

        private void OnAnimationFinished(StringName animName)
        {
            if (animName == "taxi_intro")
            {
                _animPlayer?.Play("taxi_idle");
            }
        }
    }
}
