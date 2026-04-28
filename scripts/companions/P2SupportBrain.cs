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
        [Export] public NodePath SupportDecisionBridgePath { get; set; } = new("../SupportDecisionBridge");
        [Export] public NodePath AiDecisionBridgePath { get; set; } = new("../MainCharacter/AiDecisionBridge");

        [ExportCategory("AI Bridge")]
        [Export] public bool EnableAiDecisionBridge { get; set; } = false;
        [Export] public bool AiDecisionHasPriority { get; set; } = true;
        [Export] public bool UseLiveAiDecisionSource { get; set; } = true;
        [Export] public bool RequestAiDecisionFromBridge { get; set; } = true;
        [Export(PropertyHint.Range, "0.2,10,0.1")] public float AiRequestIntervalSeconds { get; set; } = 1.0f;
        [Export] public bool ConsumeOnlyFreshAiDecision { get; set; } = true;
        [Export(PropertyHint.MultilineText)] public string DebugAiSuggestionJson { get; set; } = string.Empty;

        [ExportCategory("AI Personality Chatter")]
        [Export] public bool EnableAiPersonalityChatter { get; set; } = true;
        [Export] public bool PersonalityChatterOnlyWhenSafe { get; set; } = false;
        [Export(PropertyHint.Range, "3,60,0.5")] public float PersonalityChatterMinIntervalSeconds { get; set; } = 14f;
        [Export(PropertyHint.Range, "0,1,0.01")] public float PersonalityChatterChance { get; set; } = 0.28f;
        [Export(PropertyHint.Range, "8,80,1")] public int PersonalityChatterMaxChars { get; set; } = 26;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float EvaluateIntervalSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0.1,20,0.1")] public float GlobalHintCooldownSeconds { get; set; } = 2.2f;

        [ExportCategory("Rules")]
        [Export(PropertyHint.Range, "0.05,1,0.01")] public float LowHpThresholdRatio { get; set; } = 0.35f;
        [Export(PropertyHint.Range, "10,2000,1")] public float EnemyDangerDistance { get; set; } = 320f;
        [Export(PropertyHint.Range, "1,30,0.5")] public float QuietSceneReminderSeconds { get; set; } = 9f;

        private GameStateProvider? _gameStateProvider;
        private P2SupportExecutor? _supportExecutor;
        private P2SupportDecisionBridge? _decisionBridge;
        private AiDecisionBridge? _aiDecisionBridge;
        private float _tickAccum;
        private ulong _globalNextHintAtMs;
        private ulong _nextAiRequestAtMs;
        private ulong _nextPersonalityChatterAtMs;
        private string _lastConsumedAiDecisionSignature = string.Empty;
        private string _lastPersonalitySourceSignature = string.Empty;
        private readonly Dictionary<string, ulong> _ruleCooldownUntilMs = new();

        public ulong LastEvaluateAtMs { get; private set; }
        public string LastTriggeredRuleKey { get; private set; } = string.Empty;
        public string LastDecisionJson { get; private set; } = string.Empty;
        public string LastAiRejectReason { get; private set; } = string.Empty;
        public bool HasAiDecisionBridge => _aiDecisionBridge != null && IsInstanceValid(_aiDecisionBridge) && _aiDecisionBridge.IsInsideTree();
        public bool IsAiRequestInFlight => _aiDecisionBridge?.RequestInFlight == true;
        public string LastAiDecisionIntent => _aiDecisionBridge?.LastStructuredDecision?.Intent ?? string.Empty;
        public string LastAiDecisionUrgency => _aiDecisionBridge?.LastStructuredDecision?.Urgency ?? string.Empty;
        public string LastAiDecisionParseError => _aiDecisionBridge?.LastDecisionParseError ?? string.Empty;
        public string LastConsumedAiDecisionSignature => _lastConsumedAiDecisionSignature;
        public ulong TotalDecisionsEmitted { get; private set; }
        public ulong TotalDecisionsApplied { get; private set; }
        public ulong TotalDecisionsRejected { get; private set; }
        public ulong TotalFallbackHints { get; private set; }
        public ulong TotalAiMappedApplied { get; private set; }
        public ulong TotalPersonalityChatters { get; private set; }

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

            // Personality chatter runs on its own low-frequency gate and should not depend on rule branch returns.
            TryEmitPersonalityChatter(state);

            bool aiDecisionApplied = EnableAiDecisionBridge && TryEmitAiDecision(state);
            if (aiDecisionApplied && AiDecisionHasPriority)
            {
                return;
            }

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
                        target: "player",
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

        private bool TryEmitAiDecision(GameState state)
        {
            if (_decisionBridge == null)
            {
                LastAiRejectReason = "decision bridge not available";
                return false;
            }

            if (UseLiveAiDecisionSource)
            {
                RequestLiveAiDecisionIfNeeded();

                if (_aiDecisionBridge?.LastStructuredDecision?.IsValid == true)
                {
                    var live = _aiDecisionBridge.LastStructuredDecision;
                    string signature = BuildAiDecisionSignature(live);
                    if (ConsumeOnlyFreshAiDecision && signature == _lastConsumedAiDecisionSignature)
                    {
                        LastAiRejectReason = "ai decision unchanged";
                        return false;
                    }

                    if (!_decisionBridge.TryBuildDecisionFromAiDecision(live, out var mappedDecision, out string mapReject))
                    {
                        LastAiRejectReason = mapReject;
                        return false;
                    }

                    if (!_decisionBridge.TryValidateDecision(mappedDecision, state, out string liveValidateReject))
                    {
                        LastAiRejectReason = liveValidateReject;
                        return false;
                    }

                    LastAiRejectReason = string.Empty;
                    _lastConsumedAiDecisionSignature = signature;
                    TryEmitDecision("ai_bridge_live", mappedDecision, perRuleCooldownSeconds: 1.5f);
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(DebugAiSuggestionJson))
            {
                LastAiRejectReason = UseLiveAiDecisionSource ? "no valid live ai decision yet" : "empty ai suggestion json";
                return false;
            }

            if (!_decisionBridge.TryBuildDecisionFromJson(DebugAiSuggestionJson, out var aiDecision, out string parseReject))
            {
                LastAiRejectReason = parseReject;
                return false;
            }

            if (!_decisionBridge.TryValidateDecision(aiDecision, state, out string validateReject))
            {
                LastAiRejectReason = validateReject;
                return false;
            }

            LastAiRejectReason = string.Empty;
            TryEmitDecision("ai_bridge_debug", aiDecision, perRuleCooldownSeconds: 1.5f);
            return true;
        }

        private void RequestLiveAiDecisionIfNeeded()
        {
            if (!RequestAiDecisionFromBridge || _aiDecisionBridge == null)
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextAiRequestAtMs || _aiDecisionBridge.RequestInFlight)
            {
                return;
            }

            _ = _aiDecisionBridge.RequestDecisionAsync();
            _nextAiRequestAtMs = now + SecondsToMs(AiRequestIntervalSeconds);
        }

        private static string BuildAiDecisionSignature(AiDecision decision)
        {
            return string.Join("|", new[]
            {
                decision.Intent ?? string.Empty,
                decision.Target ?? string.Empty,
                decision.Urgency ?? string.Empty,
                decision.DurationSeconds.ToString("0.###"),
                decision.Reason ?? string.Empty
            });
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
            TotalDecisionsEmitted++;

            bool applied = _supportExecutor?.TryExecute(decision) == true;
            if (!applied && _supportExecutor != null)
            {
                TotalDecisionsRejected++;
                string fallbackMessage = BuildFallbackHint(ruleKey, _supportExecutor.LastRejectedReason);
                var fallback = SupportDecision.Hint(
                    message: fallbackMessage,
                    sourceRule: $"{ruleKey}_fallback_hint",
                    reason: $"fallback because primary decision rejected: {_supportExecutor.LastRejectedReason}",
                    urgency: "medium",
                    durationSeconds: 1.8f);

                LastTriggeredRuleKey = $"{ruleKey}_fallback_hint";
                LastDecisionJson = fallback.ToJson(pretty: false);
                TotalFallbackHints++;
                bool fallbackApplied = _supportExecutor.TryExecute(fallback);
                if (fallbackApplied)
                {
                    TotalDecisionsApplied++;
                }
            }
            else if (applied)
            {
                TotalDecisionsApplied++;
                if (ruleKey.StartsWith("ai_bridge", System.StringComparison.Ordinal))
                {
                    TotalAiMappedApplied++;
                }
            }

            _globalNextHintAtMs = now + SecondsToMs(GlobalHintCooldownSeconds);
            _ruleCooldownUntilMs[ruleKey] = now + SecondsToMs(perRuleCooldownSeconds);
        }

        private void TryEmitPersonalityChatter(GameState state)
        {
            if (!EnableAiPersonalityChatter || _supportExecutor == null || _aiDecisionBridge?.LastStructuredDecision?.IsValid != true)
            {
                return;
            }

            if (PersonalityChatterOnlyWhenSafe && state.AliveEnemyCount > 0)
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextPersonalityChatterAtMs)
            {
                return;
            }

            if (GD.Randf() > Mathf.Clamp(PersonalityChatterChance, 0f, 1f))
            {
                _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds * 0.5f);
                return;
            }

            var decision = _aiDecisionBridge.LastStructuredDecision;
            string sourceSignature = BuildAiDecisionSignature(decision);
            if (sourceSignature == _lastPersonalitySourceSignature)
            {
                _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds * 0.75f);
                return;
            }

            string text = BuildPersonalityText(decision);
            if (string.IsNullOrWhiteSpace(text))
            {
                _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds * 0.5f);
                return;
            }

            var hint = SupportDecision.Hint(
                message: text,
                sourceRule: "ai_personality_chatter",
                reason: "ambient chatter from live ai decision",
                urgency: "low",
                durationSeconds: 2.4f,
                target: "player");

            if (_supportExecutor.TryExecute(hint))
            {
                _lastPersonalitySourceSignature = sourceSignature;
                TotalPersonalityChatters++;
            }

            _nextPersonalityChatterAtMs = now + SecondsToMs(PersonalityChatterMinIntervalSeconds);
        }

        private string BuildPersonalityText(AiDecision decision)
        {
            string reason = (decision.Reason ?? string.Empty).Trim();
            string intent = (decision.Intent ?? string.Empty).Trim().ToLowerInvariant();

            string prefix = intent switch
            {
                "attack" => "我觉得可以主动压一下，",
                "use_skill" => "这波节奏不错，",
                "retreat" => "先别贪，我建议稳一手，",
                "reposition" => "换个站位更舒服，",
                "loot" => "安全的话顺手摸掉落，",
                _ => "我这边判断是，"
            };

            string core = string.IsNullOrWhiteSpace(reason) ? "当前局势可以再快一点。" : reason;
            string text = $"{prefix}{core}";
            if (text.Length > Mathf.Max(8, PersonalityChatterMaxChars))
            {
                text = text[..Mathf.Max(8, PersonalityChatterMaxChars)] + "...";
            }

            return text;
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

            if (_decisionBridge == null || !IsInstanceValid(_decisionBridge) || !_decisionBridge.IsInsideTree())
            {
                _decisionBridge = GetNodeOrNull<P2SupportDecisionBridge>(SupportDecisionBridgePath)
                    ?? GetNodeOrNull<P2SupportDecisionBridge>(NormalizeRelativePath(SupportDecisionBridgePath));
            }

            if (_aiDecisionBridge == null || !IsInstanceValid(_aiDecisionBridge) || !_aiDecisionBridge.IsInsideTree())
            {
                _aiDecisionBridge = GetNodeOrNull<AiDecisionBridge>(AiDecisionBridgePath)
                    ?? GetNodeOrNull<AiDecisionBridge>(NormalizeRelativePath(AiDecisionBridgePath));
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