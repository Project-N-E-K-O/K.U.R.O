using System.Text;
using Godot;
using Kuros.Systems.AI;

namespace Kuros.Companions
{
    /// <summary>
    /// On-screen debug panel for observing P2 runtime state.
    /// </summary>
    [GlobalClass]
    public partial class P2DebugPanel : CanvasLayer
    {
        [ExportCategory("References")]
        [Export] public NodePath CompanionControllerPath { get; set; } = new("..");
        [Export] public NodePath SupportBrainPath { get; set; } = new("../SupportBrain");
        [Export] public NodePath SupportExecutorPath { get; set; } = new("../SupportExecutor");
        [Export] public NodePath HintBubblePath { get; set; } = new("../HintBubble");
        [Export] public NodePath GameStateProviderPath { get; set; } = new("../MainCharacter/GameStateProvider");

        [ExportCategory("UI")]
        [Export] public NodePath ToggleButtonPath { get; set; } = new("Panel/VBox/ToggleButton");
        [Export] public NodePath ContentNodePath { get; set; } = new("Panel/VBox/OutputText");
        [Export] public NodePath OutputTextPath { get; set; } = new("Panel/VBox/OutputText");
        [Export] public bool AutoRefresh { get; set; } = true;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float RefreshIntervalSeconds { get; set; } = 0.5f;

        private P2CompanionController? _controller;
        private P2SupportBrain? _brain;
        private P2SupportExecutor? _executor;
        private P2HintBubble? _hintBubble;
        private GameStateProvider? _gameStateProvider;
        private Button? _toggleButton;
        private Control? _contentNode;
        private RichTextLabel? _outputText;
        private bool _contentVisible = true;
        private float _timer;

        public override void _Ready()
        {
            ResolveDependencies();

            _toggleButton = GetNodeOrNull<Button>(ToggleButtonPath);
            _contentNode = GetNodeOrNull<Control>(ContentNodePath);
            _outputText = GetNodeOrNull<RichTextLabel>(OutputTextPath);

            if (_toggleButton != null)
            {
                _toggleButton.Pressed += OnTogglePressed;
                UpdateToggleButtonText();
            }

            _timer = 0f;
            SetProcess(AutoRefresh);
            RefreshView();
        }

        public override void _ExitTree()
        {
            if (_toggleButton != null)
            {
                _toggleButton.Pressed -= OnTogglePressed;
            }

            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (!AutoRefresh)
            {
                return;
            }

            _timer -= (float)delta;
            if (_timer > 0f)
            {
                return;
            }

            _timer = Mathf.Max(0.1f, RefreshIntervalSeconds);
            RefreshView();
        }

        private void OnTogglePressed()
        {
            _contentVisible = !_contentVisible;
            if (_contentNode != null)
            {
                _contentNode.Visible = _contentVisible;
            }

            UpdateToggleButtonText();
        }

        private void UpdateToggleButtonText()
        {
            if (_toggleButton == null)
            {
                return;
            }

            _toggleButton.Text = _contentVisible ? "Hide" : "Show";
        }

        private void RefreshView()
        {
            ResolveDependencies();
            if (_outputText == null)
            {
                return;
            }

            var sb = new StringBuilder(512);
            sb.AppendLine("[P2 Debug]");

            if (_gameStateProvider != null)
            {
                GameState state = _gameStateProvider.CaptureGameState();
                sb.AppendLine($"player: {state.PlayerHp}/{state.PlayerMaxHp} | under_attack={state.PlayerUnderAttack}");
                sb.AppendLine($"state: {Safe(state.PlayerStateName)}");
                sb.AppendLine($"enemies: {state.AliveEnemyCount} | nearest={FormatDistance(state.NearestEnemyDistance)}");
            }
            else
            {
                sb.AppendLine("game: (missing)");
            }

            if (_hintBubble != null)
            {
                sb.AppendLine($"hint: {Safe(_hintBubble.CurrentMessage)}");
            }
            else
            {
                sb.AppendLine("hint: (missing)");
            }

            if (_brain != null)
            {
                sb.AppendLine($"rule: {Safe(_brain.LastTriggeredRuleKey)}");
                sb.AppendLine($"ai_bridge: {(_brain.HasAiDecisionBridge ? "connected" : "missing")} | inflight={_brain.IsAiRequestInFlight}");
                if (!string.IsNullOrWhiteSpace(_brain.LastAiDecisionIntent))
                {
                    sb.AppendLine($"ai_decision: {_brain.LastAiDecisionIntent} ({Safe(_brain.LastAiDecisionUrgency)})");
                }
                if (!string.IsNullOrWhiteSpace(_brain.LastAiRejectReason))
                {
                    sb.AppendLine($"ai_reject: {Safe(_brain.LastAiRejectReason)}");
                }
                if (!string.IsNullOrWhiteSpace(_brain.LastAiDecisionParseError))
                {
                    sb.AppendLine($"ai_parse: {Safe(_brain.LastAiDecisionParseError)}");
                }
                sb.AppendLine($"brain_stats: emit={_brain.TotalDecisionsEmitted} apply={_brain.TotalDecisionsApplied} reject={_brain.TotalDecisionsRejected}");
                sb.AppendLine($"brain_stats2: fallback={_brain.TotalFallbackHints} ai_map={_brain.TotalAiMappedApplied} chatter={_brain.TotalPersonalityChatters}");
            }
            else
            {
                sb.AppendLine("rule: (missing)");
            }

            if (_executor != null)
            {
                sb.AppendLine($"result: {Safe(_executor.LastResult)}");
                sb.AppendLine($"action: {Safe(_executor.LastIntent)}");
                sb.AppendLine($"loadout: skill={Safe(_executor.GetEquippedSupportSkillId())} | equip={Safe(_executor.GetEquippedEquipmentId())}");
                sb.AppendLine($"cooldown: skill={_executor.GetSupportSkillCooldownRemainingSeconds():0.0}s | item={_executor.GetSupportItemCooldownRemainingSeconds():0.0}s");
                sb.AppendLine($"shield: {_executor.GetActiveShieldPoints()} | remain={_executor.GetShieldRemainingSeconds():0.0}s");
                sb.AppendLine($"exec_stats: req={_executor.TotalDecisionRequests} apply={_executor.TotalDecisionApplied} reject={_executor.TotalDecisionRejected}");
                sb.AppendLine($"exec_stats2: absorbed={_executor.TotalShieldAbsorbedDamage} heal_skill={_executor.TotalHealFromSkills} heal_bonus={_executor.TotalHealFromEquipBonus}");
                if (!string.IsNullOrWhiteSpace(_executor.LastActionDetail))
                {
                    sb.AppendLine($"detail: {Safe(_executor.LastActionDetail)}");
                }
                if (!string.IsNullOrWhiteSpace(_executor.LastRejectedReason))
                {
                    sb.AppendLine($"reject: {Safe(_executor.LastRejectedReason)}");
                }
            }
            else
            {
                sb.AppendLine("action: (missing)");
            }

            _outputText.Text = sb.ToString();
        }

        private void ResolveDependencies()
        {
            _controller ??= GetNodeOrNull<P2CompanionController>(CompanionControllerPath)
                ?? GetNodeOrNull<P2CompanionController>(NormalizeRelativePath(CompanionControllerPath))
                ?? GetParent() as P2CompanionController;

            _brain ??= GetNodeOrNull<P2SupportBrain>(SupportBrainPath)
                ?? GetNodeOrNull<P2SupportBrain>(NormalizeRelativePath(SupportBrainPath))
                ?? GetParent()?.GetNodeOrNull<P2SupportBrain>("SupportBrain");

            _executor ??= GetNodeOrNull<P2SupportExecutor>(SupportExecutorPath)
                ?? GetNodeOrNull<P2SupportExecutor>(NormalizeRelativePath(SupportExecutorPath))
                ?? GetParent()?.GetNodeOrNull<P2SupportExecutor>("SupportExecutor");

            _hintBubble ??= GetNodeOrNull<P2HintBubble>(HintBubblePath)
                ?? GetNodeOrNull<P2HintBubble>(NormalizeRelativePath(HintBubblePath))
                ?? GetParent()?.GetNodeOrNull<P2HintBubble>("HintBubble");

            _gameStateProvider ??= GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath))
                ?? GetTree().GetFirstNodeInGroup("player")?.GetNodeOrNull<GameStateProvider>("GameStateProvider");
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(none)";
            }

            return value.Length > 72 ? value[..72] + "..." : value;
        }

        private static string FormatDistance(float distance)
        {
            return distance < 0f ? "n/a" : distance.ToString("0");
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }
    }
}
