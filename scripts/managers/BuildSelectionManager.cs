using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Systems;
using Kuros.UI;

namespace Kuros.Managers
{
    /// <summary>
    /// 构筑选择管理器：监听玩家分数，达到阈值时弹出三选一构筑效果窗口。
    /// </summary>
    public partial class BuildSelectionManager : Node
    {
        public static BuildSelectionManager Instance { get; private set; } = null!;

        [ExportGroup("Thresholds")]
        [Export] public ScoreThresholdCurve? ThresholdCurve { get; set; }

        [ExportGroup("Effect Pool")]
        [Export] public Godot.Collections.Array<BuildEffectDefinition> EffectPool { get; set; } = new();

        [ExportGroup("Debug")]
        [Export] public bool DebugTrigger { get; set; }

        private SamplePlayer? _boundPlayer;
        private PlayerBuildController? _buildController;
        private int _lastKnownScore;
        private int _triggerCount;
        private bool _isSelectionActive;
        private readonly System.Random _rng = new();

        public bool IsSelectionActive => _isSelectionActive;

        public override void _Ready()
        {
            Instance = this;
            TryBindPlayer();
        }

        public override void _ExitTree()
        {
            UnbindPlayer();
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer))
                TryBindPlayer();

            if (DebugTrigger && _boundPlayer != null && !_isSelectionActive)
            {
                DebugTrigger = false;
                TriggerSelection();
            }
        }

        private void TryBindPlayer()
        {
            if (_isSelectionActive) return;

            var tree = GetTree();
            if (tree == null) return;

            var player = tree.GetFirstNodeInGroup("player") as SamplePlayer;
            if (player == null || !IsInstanceValid(player)) return;
            if (player == _boundPlayer) return;

            if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                _boundPlayer.StatsUpdated -= OnPlayerStatsUpdated;

            _boundPlayer = player;
            _buildController = player.FindChild("BuildController", recursive: true, owned: false) as PlayerBuildController;
            _boundPlayer.StatsUpdated += OnPlayerStatsUpdated;
            _lastKnownScore = player.Score;
            _triggerCount = ThresholdCurve?.GetTriggerCount(player.Score) ?? 0;
        }

        private void UnbindPlayer()
        {
            if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                _boundPlayer.StatsUpdated -= OnPlayerStatsUpdated;
            _boundPlayer = null;
            _buildController = null;
        }

        private void OnPlayerStatsUpdated(int health, int maxHealth, int score)
        {
            if (score > _lastKnownScore)
                CheckAndTriggerSelection(score);
            _lastKnownScore = score;
        }

        private void CheckAndTriggerSelection(int newScore)
        {
            if (_isSelectionActive) return;
            if (EffectPool.Count == 0) return;
            if (ThresholdCurve == null) return;

            int nextThreshold = ThresholdCurve.GetCumulativeScore(_triggerCount + 1);
            if (newScore >= nextThreshold)
            {
                _triggerCount++;
                TriggerSelection();
            }
        }

        private PackedScene? _windowScene;

        private void TriggerSelection()
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer)) return;

            var options = PickRandomEffects(3);
            if (options.Count == 0) return;

            _isSelectionActive = true;

            _windowScene ??= GD.Load<PackedScene>("res://scenes/ui/windows/BuildSelectionWindow.tscn");
            var window = _windowScene.Instantiate<BuildSelectionWindow>();

            var canvasLayer = new CanvasLayer { Layer = 3 };
            GetTree().Root.AddChild(canvasLayer);
            canvasLayer.AddChild(window);

            window.ShowWindow(options, chosenEffect =>
            {
                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                {
                    ApplyEffectBonuses(chosenEffect);
                    if (_buildController != null)
                        _buildController.AddBuildEffectPoints(chosenEffect.BuildClass, chosenEffect.LevelCount);
                }
                _isSelectionActive = false;

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            });
        }

        private void ApplyEffectBonuses(BuildEffectDefinition effect)
        {
            if (_boundPlayer == null) return;

            foreach (var kvp in effect.StatBonuses)
            {
                switch (kvp.Key)
                {
                    case "attack_damage":
                        _boundPlayer.AttackDamage += kvp.Value;
                        break;
                    case "speed":
                        _boundPlayer.Speed += kvp.Value;
                        break;
                    case "max_health":
                        _boundPlayer.MaxHealth += Mathf.RoundToInt(kvp.Value);
                        _boundPlayer.RestoreHealth(Mathf.RoundToInt(kvp.Value));
                        break;
                }
            }
        }

        private List<BuildEffectDefinition> PickRandomEffects(int count)
        {
            var result = new List<BuildEffectDefinition>();
            int poolSize = EffectPool.Count;
            if (poolSize == 0) return result;
            count = Mathf.Min(count, poolSize);

            var indices = new List<int>(poolSize);
            for (int i = 0; i < poolSize; i++)
                indices.Add(i);

            for (int i = 0; i < count; i++)
            {
                int j = _rng.Next(i, poolSize);
                (indices[i], indices[j]) = (indices[j], indices[i]);

                var effect = EffectPool[indices[i]];
                if (effect != null)
                    result.Add(effect);
            }

            return result;
        }
    }
}
