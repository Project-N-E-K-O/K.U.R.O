using System.Collections.Generic;
using Godot;
using Kuros.Items.Tags;
using Kuros.Systems.AI;

namespace Kuros.Companions
{
    /// <summary>
    /// Rule-based support brain for P2. Reads GameState and emits lightweight non-blocking hints.
    /// </summary>
    public partial class P2SupportBrain : Node
    {
        [ExportCategory("References")]
        [Export] public NodePath GameStateProviderPath { get; set; } = new("../MainCharacter/GameStateProvider");
        [Export] public NodePath SupportExecutorPath { get; set; } = new("../SupportExecutor");

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float EvaluateIntervalSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0.1,20,0.1")] public float GlobalHintCooldownSeconds { get; set; } = 2.2f;

        [ExportCategory("Rules")]
        [Export(PropertyHint.Range, "0.05,1,0.01")] public float LowHpThresholdRatio { get; set; } = 0.35f;
        [Export(PropertyHint.Range, "10,2000,1")] public float EnemyDangerDistance { get; set; } = 320f;
        [Export(PropertyHint.Range, "1,30,0.5")] public float QuietSceneReminderSeconds { get; set; } = 9f;

        private GameStateProvider? _gameStateProvider;
        private P2SupportExecutor? _supportExecutor;
        private float _tickAccum;
        private ulong _globalNextHintAtMs;
        private readonly Dictionary<string, ulong> _ruleCooldownUntilMs = new();

        public ulong LastEvaluateAtMs { get; private set; }
        public string LastTriggeredRuleKey { get; private set; } = string.Empty;
        public string LastDecisionJson { get; private set; } = string.Empty;

        public override void _Process(double delta)
        {
            ResolveDependencies();
            if (_gameStateProvider == null || _supportExecutor == null)
            {
                return;
            }

            _tickAccum += (float)delta;
            if (_tickAccum < Mathf.Max(0.1f, EvaluateIntervalSeconds))
            {
                return;
            }

            _tickAccum = 0f;
            Evaluate(_gameStateProvider.CaptureGameState());
        }

        private void Evaluate(GameState state)
        {
            LastEvaluateAtMs = Time.GetTicksMsec();

            if (state.PlayerMaxHp <= 0)
            {
                return;
            }

            float hpRatio = state.PlayerHp / (float)Mathf.Max(1, state.PlayerMaxHp);

            if (hpRatio <= LowHpThresholdRatio && state.PlayerUnderAttack)
            {
                TryEmitDecision(
                    ruleKey: "low_hp_under_attack",
                    decision: SupportDecision.UseSupportItem(
                        sourceRule: "low_hp_under_attack",
                        reason: "player hp below threshold while under attack",
                        itemTag: ItemTagIds.Food,
                        urgency: "high"),
                    perRuleCooldownSeconds: 5.5f);
                return;
            }

            if (state.AliveEnemyCount > 0 && state.NearestEnemyDistance > 0f && state.NearestEnemyDistance <= EnemyDangerDistance)
            {
                TryEmitDecision(
                    ruleKey: "enemy_too_close",
                    decision: SupportDecision.TriggerSupportSkill(
                        sourceRule: "enemy_too_close",
                        reason: "nearest enemy is within danger distance",
                        target: "shield",
                        urgency: "medium"),
                    perRuleCooldownSeconds: 4.0f);
                return;
            }

            if (state.AliveEnemyCount == 0)
            {
                TryEmitDecision(
                    ruleKey: "quiet_scene_pickup",
                    decision: SupportDecision.Hint(
                        message: "暂时安全，看看附近掉落",
                        sourceRule: "quiet_scene_pickup",
                        reason: "no alive enemies",
                        urgency: "low",
                        durationSeconds: 1.8f),
                    perRuleCooldownSeconds: QuietSceneReminderSeconds);
            }
        }

        private void TryEmitDecision(string ruleKey, SupportDecision decision, float perRuleCooldownSeconds)
        {
            ulong now = Time.GetTicksMsec();
            if (now < _globalNextHintAtMs)
            {
                return;
            }

            if (_ruleCooldownUntilMs.TryGetValue(ruleKey, out ulong untilMs) && now < untilMs)
            {
                return;
            }

            LastTriggeredRuleKey = ruleKey;
            LastDecisionJson = decision.ToJson(pretty: false);

            bool applied = _supportExecutor?.TryExecute(decision) == true;
            if (!applied && _supportExecutor != null)
            {
                string fallbackMessage = BuildFallbackHint(ruleKey, _supportExecutor.LastRejectedReason);
                var fallback = SupportDecision.Hint(
                    message: fallbackMessage,
                    sourceRule: $"{ruleKey}_fallback_hint",
                    reason: $"fallback because primary decision rejected: {_supportExecutor.LastRejectedReason}",
                    urgency: "medium",
                    durationSeconds: 1.8f);

                LastTriggeredRuleKey = $"{ruleKey}_fallback_hint";
                LastDecisionJson = fallback.ToJson(pretty: false);
                _supportExecutor.TryExecute(fallback);
            }

            _globalNextHintAtMs = now + SecondsToMs(GlobalHintCooldownSeconds);
            _ruleCooldownUntilMs[ruleKey] = now + SecondsToMs(perRuleCooldownSeconds);
        }

        private static string BuildFallbackHint(string ruleKey, string rejectReason)
        {
            if (ruleKey == "low_hp_under_attack")
            {
                return "补给暂不可用，先撤一步保命";
            }

            if (ruleKey == "enemy_too_close")
            {
                return "护盾暂不可用，注意拉开距离";
            }

            return string.IsNullOrWhiteSpace(rejectReason)
                ? "当前辅助动作不可用"
                : $"当前辅助不可用：{rejectReason}";
        }

        private void ResolveDependencies()
        {
            if (_gameStateProvider == null || !IsInstanceValid(_gameStateProvider) || !_gameStateProvider.IsInsideTree())
            {
                _gameStateProvider = GetNodeOrNull<GameStateProvider>(GameStateProviderPath)
                    ?? GetNodeOrNull<GameStateProvider>(NormalizeRelativePath(GameStateProviderPath))
                    ?? GetTree().GetFirstNodeInGroup("player")?.GetNodeOrNull<GameStateProvider>("GameStateProvider");
            }

            if (_supportExecutor == null || !IsInstanceValid(_supportExecutor) || !_supportExecutor.IsInsideTree())
            {
                _supportExecutor = GetNodeOrNull<P2SupportExecutor>(SupportExecutorPath)
                    ?? GetNodeOrNull<P2SupportExecutor>(NormalizeRelativePath(SupportExecutorPath));
            }
        }

        private static ulong SecondsToMs(float seconds)
        {
            return (ulong)Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f);
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