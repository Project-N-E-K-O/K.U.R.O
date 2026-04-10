using System;
using Godot;
using Kuros.Core;
using Kuros.Utils;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// Local safety layer between structured AI decision and in-game execution.
    /// It validates, throttles, and only applies whitelisted actions.
    /// </summary>
    [GlobalClass]
    public partial class AiDecisionExecutor : Node
    {
        [Signal] public delegate void ExecutionCompletedEventHandler(string executionJson);
        [Signal] public delegate void ExecutionRejectedEventHandler(string reason);
        [Signal] public delegate void AutopilotChangedEventHandler(bool enabled);

        [Export] public NodePath AiDecisionBridgePath { get; set; } = new("../AiDecisionBridge");
        [Export] public NodePath PlayerPath { get; set; } = new("..");
        [Export] public bool EnableAutoExecution { get; set; } = true;
        [Export] public bool RequireLivingPlayer { get; set; } = true;
        [Export(PropertyHint.Range, "0,10,0.05")] public float MinExecutionIntervalSeconds { get; set; } = 0.15f;
        [Export] public bool StartWithAutopilotEnabled { get; set; }
        [Export] public string EnemyGroupName { get; set; } = "enemies";
        [Export(PropertyHint.Range, "0.1,10,0.05")] public float DecisionRequestIntervalSeconds { get; set; } = 1.0f;
        [Export(PropertyHint.Range, "1,2000,1")] public float AttackApproachDistance { get; set; } = 260f;
        [Export(PropertyHint.Range, "1,3000,1")] public float RunDistanceThreshold { get; set; } = 420f;
        [Export(PropertyHint.Range, "0.1,10,0.05")] public float DecisionHoldGraceSeconds { get; set; } = 0.1f;
        [Export(PropertyHint.Range, "0,10,0.05")] public float DecisionCarryForwardSeconds { get; set; } = 1.2f;
        [ExportGroup("Escape")]
        [Export(PropertyHint.Range, "0.01,0.5,0.01")] public float EscapeMashIntervalSeconds { get; set; } = 0.06f;
        [ExportGroup("Tactical Layer")]
        [Export] public bool EnableLocalTacticalLayer { get; set; } = true;
        [Export(PropertyHint.Range, "0,1,0.01")] public float RetreatHpRatio { get; set; } = 0.35f;
        [Export(PropertyHint.Range, "1,2000,1")] public float KiteDistanceMin { get; set; } = 120f;
        [Export(PropertyHint.Range, "1,3000,1")] public float KiteDistanceMax { get; set; } = 220f;
        [Export(PropertyHint.Range, "0,10,0.05")] public float RecentlyHitWindowSeconds { get; set; } = 0.65f;

        public string LastExecutionJson { get; private set; } = string.Empty;
        public string LastExecutionError { get; private set; } = string.Empty;
        public bool AutoPilotEnabled { get; private set; }

        private AiDecisionBridge? _bridge;
        private global::SamplePlayer? _player;
        private ulong _lastExecutionAtMs;
        private ulong _lastDecisionRequestedAtMs;
        private AiDecision? _activeDecision;
        private ulong _activeDecisionExpiresAtMs;
        private ulong _activeDecisionCarryUntilMs;
        private string _lastStructuredIntent = string.Empty;
        private int _sameIntentStreak;
        private ulong _nextEscapeMashAtMs;
        private bool _escapeMashLeftNext = true;

        public override void _Ready()
        {
            ResolveDependencies();
            SubscribeBridgeSignals();
            SetAutopilotEnabled(StartWithAutopilotEnabled, emitSignal: false);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (!AutoPilotEnabled)
            {
                if (_player != null)
                {
                    _player.SetAiInputOverrideEnabled(false);
                }

                return;
            }

            ResolveDependencies();
            SubscribeBridgeSignals();
            if (_player == null || _bridge == null)
            {
                if (_player != null)
                {
                    // Missing control dependencies: immediately return control to player input.
                    _player.SetAiInputOverrideEnabled(false);
                    _player.ClearAiControlCommands();
                }

                return;
            }

            _player.SetAiInputOverrideEnabled(true);
            UpdateAutopilotDecisionRequests();
            ApplyAutopilotControl();
        }

        public override void _ExitTree()
        {
            UnsubscribeBridgeSignals();
            base._ExitTree();
        }

        private void ResolveDependencies()
        {
            _bridge ??= GetNodeOrNull<AiDecisionBridge>(AiDecisionBridgePath)
                ?? GetNodeOrNull<AiDecisionBridge>(NormalizeRelativePath(AiDecisionBridgePath));

            _player ??= GetNodeOrNull<global::SamplePlayer>(PlayerPath)
                ?? GetNodeOrNull<global::SamplePlayer>(NormalizeRelativePath(PlayerPath))
                ?? GetTree().GetFirstNodeInGroup("player") as global::SamplePlayer;
        }

        private void SubscribeBridgeSignals()
        {
            if (_bridge == null)
            {
                return;
            }

            var structuredCallable = new Callable(this, MethodName.OnDecisionStructured);
            if (!_bridge.IsConnected(AiDecisionBridge.SignalName.DecisionStructured, structuredCallable))
            {
                _bridge.DecisionStructured += OnDecisionStructured;
            }

            var parseFailCallable = new Callable(this, MethodName.OnDecisionStructureFailed);
            if (!_bridge.IsConnected(AiDecisionBridge.SignalName.DecisionStructureFailed, parseFailCallable))
            {
                _bridge.DecisionStructureFailed += OnDecisionStructureFailed;
            }
        }

        private void UnsubscribeBridgeSignals()
        {
            if (_bridge == null)
            {
                return;
            }

            var structuredCallable = new Callable(this, MethodName.OnDecisionStructured);
            if (_bridge.IsConnected(AiDecisionBridge.SignalName.DecisionStructured, structuredCallable))
            {
                _bridge.DecisionStructured -= OnDecisionStructured;
            }

            var parseFailCallable = new Callable(this, MethodName.OnDecisionStructureFailed);
            if (_bridge.IsConnected(AiDecisionBridge.SignalName.DecisionStructureFailed, parseFailCallable))
            {
                _bridge.DecisionStructureFailed -= OnDecisionStructureFailed;
            }
        }

        private void OnDecisionStructured(string decisionJson)
        {
            ResolveDependencies();
            if (_bridge == null)
            {
                PublishReject("Execution skipped: AiDecisionBridge missing.");
                return;
            }

            var decision = _bridge.LastStructuredDecision;
            if (decision == null || !decision.IsValid)
            {
                PublishReject("Execution skipped: structured decision is invalid.");
                return;
            }

            UpdateIntentStreak(decision.Intent);

            _activeDecision = decision;
            ulong now = Time.GetTicksMsec();
            _activeDecisionExpiresAtMs = now + (ulong)Mathf.RoundToInt(Mathf.Max(0.1f, decision.DurationSeconds + DecisionHoldGraceSeconds) * 1000f);
            _activeDecisionCarryUntilMs = _activeDecisionExpiresAtMs + (ulong)Mathf.RoundToInt(Mathf.Max(0f, DecisionCarryForwardSeconds) * 1000f);

            if (!EnableAutoExecution)
            {
                return;
            }

            if (AutoPilotEnabled && !ShouldExecuteImmediately(decision))
            {
                var autopilotReport = BuildNoopResult(decision, "accepted_control", "decision handled by autopilot continuous control layer");
                LastExecutionError = string.Empty;
                LastExecutionJson = Json.Stringify(autopilotReport, "  ");
                EmitSignal(SignalName.ExecutionCompleted, LastExecutionJson);
                return;
            }

            if (!CanExecuteNow(out string throttleReason))
            {
                PublishReject($"Execution throttled: {throttleReason}");
                return;
            }

            if (!ValidateActorState(out string stateReason))
            {
                PublishReject($"Execution rejected: {stateReason}");
                return;
            }

            var report = ExecuteWithSafety(decision);
            LastExecutionError = report["status"].AsString() == "rejected" ? report["message"].AsString() : string.Empty;
            LastExecutionJson = Json.Stringify(report, "  ");
            EmitSignal(SignalName.ExecutionCompleted, LastExecutionJson);
        }

        private void OnDecisionStructureFailed(string error)
        {
            PublishReject($"Execution skipped due to decision parse failure: {error}");
        }

        private bool CanExecuteNow(out string reason)
        {
            ulong now = Time.GetTicksMsec();
            ulong minMs = (ulong)Mathf.RoundToInt(Mathf.Max(0f, MinExecutionIntervalSeconds) * 1000f);
            if (_lastExecutionAtMs != 0 && now - _lastExecutionAtMs < minMs)
            {
                reason = $"min interval {MinExecutionIntervalSeconds:0.##}s not reached";
                return false;
            }

            _lastExecutionAtMs = now;
            reason = string.Empty;
            return true;
        }

        private bool ValidateActorState(out string reason)
        {
            if (_player == null)
            {
                reason = "player node not found";
                return false;
            }

            if (!RequireLivingPlayer)
            {
                reason = string.Empty;
                return true;
            }

            if (_player.IsDead || _player.IsDeathSequenceActive)
            {
                reason = "player is dead or in death sequence";
                return false;
            }

            string stateName = _player.StateMachine?.CurrentState?.Name ?? string.Empty;
            if (string.Equals(stateName, "Dying", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stateName, "Dead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stateName, "Frozen", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"player state '{stateName}' blocks AI execution";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private Godot.Collections.Dictionary<string, Variant> ExecuteWithSafety(AiDecision decision)
        {
            string intent = decision.Intent.Trim().ToLowerInvariant();

            return intent switch
            {
                "attack" => ExecuteAttack(decision),
                "use_skill" => ExecuteUseSkill(decision),
                "switch_weapon" => ExecuteSwitchWeapon(decision),
                "retreat" => BuildNoopResult(decision, "accepted_control", "retreat intent accepted and handled by movement control layer."),
                "reposition" => BuildNoopResult(decision, "accepted_control", "reposition intent accepted and handled by movement control layer."),
                "loot" => BuildNoopResult(decision, "accepted_control", "loot intent accepted but current autopilot only supports combat-facing movement."),
                "hold" => BuildNoopResult(decision, "accepted_noop", "hold intent acknowledged."),
                _ => BuildRejectResult(decision, $"intent '{intent}' is not in local execution whitelist")
            };
        }

        private Godot.Collections.Dictionary<string, Variant> ExecuteAttack(AiDecision decision)
        {
            if (_player == null)
            {
                return BuildRejectResult(decision, "player not available");
            }

            var nearestEnemy = ResolveNearestEnemy();
            if (nearestEnemy == null)
            {
                return BuildRejectResult(decision, "no valid enemy target found for attack");
            }

            if (nearestEnemy != null)
            {
                float distance = _player.GlobalPosition.DistanceTo(nearestEnemy.GlobalPosition);
                if (distance > AttackApproachDistance)
                {
                    return BuildNoopResult(decision, "accepted_control", "enemy is outside attack range, approach handled by movement control layer");
                }
            }

            string currentState = _player.StateMachine?.CurrentState?.Name ?? string.Empty;
            if (string.Equals(currentState, "Hit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentState, "Frozen", StringComparison.OrdinalIgnoreCase))
            {
                return BuildNoopResult(decision, "downgraded", $"attack blocked by state '{currentState}', downgraded to hold");
            }

            _player.RequestAttackFromState(_player.LastMovementStateName);
            _player.StateMachine?.ChangeState("Attack");
            GameLogger.Info(nameof(AiDecisionExecutor), "AI decision executed: attack");
            return BuildAppliedResult(decision, "state_machine.change_state", "Attack");
        }

        private Godot.Collections.Dictionary<string, Variant> ExecuteUseSkill(AiDecision decision)
        {
            if (_player?.WeaponSkillController?.TryTriggerActionSkill("weapon_skill_block") == true)
            {
                GameLogger.Info(nameof(AiDecisionExecutor), "AI decision executed: use_skill -> weapon_skill_block");
                return BuildAppliedResult(decision, "weapon_skill", "weapon_skill_block");
            }

            return BuildNoopResult(decision, "downgraded", "requested skill is unavailable now, downgraded to hold");
        }

        private Godot.Collections.Dictionary<string, Variant> ExecuteSwitchWeapon(AiDecision decision)
        {
            if (_player?.InventoryComponent == null)
            {
                return BuildRejectResult(decision, "inventory component not available");
            }

            if (!TryParseQuickBarIndex(decision.Target, out int slotIndex))
            {
                return BuildRejectResult(decision, "target must be quickbar_1..quickbar_5 or slot_1..slot_5");
            }

            _player.InventoryComponent.SelectedQuickBarSlot = slotIndex;
            GameLogger.Info(nameof(AiDecisionExecutor), $"AI decision executed: switch_weapon -> slot {slotIndex + 1}");
            return BuildAppliedResult(decision, "quickbar.select", (slotIndex + 1).ToString());
        }

        public void SetAutopilotEnabled(bool enabled, bool emitSignal = true)
        {
            if (AutoPilotEnabled == enabled)
            {
                return;
            }

            AutoPilotEnabled = enabled;
            ResolveDependencies();
            SubscribeBridgeSignals();

            bool canTakeOverInput = enabled && _player != null && _bridge != null;

            if (_player != null)
            {
                _player.SetAiInputOverrideEnabled(canTakeOverInput);
                _player.ClearAiControlCommands();
            }

            if (enabled && !canTakeOverInput)
            {
                GameLogger.Warn(nameof(AiDecisionExecutor), "Autopilot enabled but dependencies are missing; fallback to manual input until ready.");
            }

            _activeDecision = null;
            _activeDecisionExpiresAtMs = 0;
            _activeDecisionCarryUntilMs = 0;
            _lastDecisionRequestedAtMs = 0;
            _nextEscapeMashAtMs = 0;
            _escapeMashLeftNext = true;

            if (emitSignal)
            {
                EmitSignal(SignalName.AutopilotChanged, enabled);
            }
        }

        private void UpdateAutopilotDecisionRequests()
        {
            if (_bridge == null || _player == null || _bridge.RequestInFlight)
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            ulong intervalMs = (ulong)Mathf.RoundToInt(Mathf.Max(0.1f, DecisionRequestIntervalSeconds) * 1000f);
            if (_lastDecisionRequestedAtMs != 0 && now - _lastDecisionRequestedAtMs < intervalMs)
            {
                return;
            }

            bool hasActiveDecision = _activeDecision != null && now <= _activeDecisionExpiresAtMs;
            if (hasActiveDecision)
            {
                ulong refreshLeadMs = (ulong)Mathf.RoundToInt(Mathf.Max(0.2f, DecisionCarryForwardSeconds * 0.5f) * 1000f);
                if (_activeDecisionExpiresAtMs > now + refreshLeadMs)
                {
                    return;
                }
            }

            _lastDecisionRequestedAtMs = now;
            _ = _bridge.RequestDecisionAsync("You are fully controlling the player in a fast-paced action game. Choose the next movement or combat action and return strict JSON with intent, target, urgency, duration_seconds, reason.");
        }

        private void ApplyAutopilotControl()
        {
            if (_player == null)
            {
                return;
            }

            string currentState = _player.StateMachine?.CurrentState?.Name ?? string.Empty;
            if (string.Equals(currentState, "Frozen", StringComparison.OrdinalIgnoreCase))
            {
                ApplyFreezeEscapeControl();
                return;
            }

            ulong now = Time.GetTicksMsec();
            if (_activeDecision == null)
            {
                _player.ClearAiControlCommands();
                return;
            }

            if (now > _activeDecisionCarryUntilMs)
            {
                _player.ClearAiControlCommands();
                return;
            }

            switch (_activeDecision.Intent)
            {
                case "attack":
                    ApplyAttackControl();
                    break;
                case "use_skill":
                    ApplyAttackControl(preferRun: true);
                    break;
                case "retreat":
                    ApplyRetreatControl();
                    break;
                case "reposition":
                    ApplyRepositionControl();
                    break;
                case "hold":
                    _player.ClearAiControlCommands();
                    break;
                default:
                    _player.ClearAiControlCommands();
                    break;
            }
        }

        private void ApplyFreezeEscapeControl()
        {
            if (_player == null)
            {
                return;
            }

            _player.SetAiDesiredMovement(Vector2.Zero, false);
            ulong now = Time.GetTicksMsec();
            if (_nextEscapeMashAtMs != 0 && now < _nextEscapeMashAtMs)
            {
                return;
            }

            if (_escapeMashLeftNext)
            {
                _player.QueueAiMoveLeft();
            }
            else
            {
                _player.QueueAiMoveRight();
            }

            _escapeMashLeftNext = !_escapeMashLeftNext;
            ulong intervalMs = (ulong)Mathf.RoundToInt(Mathf.Max(0.01f, EscapeMashIntervalSeconds) * 1000f);
            _nextEscapeMashAtMs = now + intervalMs;
        }

        private void ApplyAttackControl(bool preferRun = false)
        {
            if (_player == null)
            {
                return;
            }

            var nearestEnemy = ResolveNearestEnemy();
            if (nearestEnemy == null)
            {
                _player.ClearAiControlCommands();
                return;
            }

            Vector2 toEnemy = nearestEnemy.GlobalPosition - _player.GlobalPosition;
            float distance = toEnemy.Length();

            if (EnableLocalTacticalLayer)
            {
                float hpRatio = _player.MaxHealth > 0
                    ? (float)_player.CurrentHealth / _player.MaxHealth
                    : 1f;
                bool recentlyHit = _player.GetSecondsSinceLastDamageTaken() <= Mathf.Max(0f, RecentlyHitWindowSeconds);
                bool lowHpPressure = hpRatio <= RetreatHpRatio;

                if (lowHpPressure && distance <= KiteDistanceMax)
                {
                    // Low HP while pressured: break contact instead of face-tanking.
                    Vector2 retreatDir = (_player.GlobalPosition - nearestEnemy.GlobalPosition).Normalized();
                    _player.SetAiDesiredMovement(retreatDir, true);
                    return;
                }

                if (distance < KiteDistanceMin)
                {
                    if (recentlyHit || _sameIntentStreak >= 4)
                    {
                        // Under close pressure: strafe to create space (kite behavior).
                        Vector2 lateral = ComputeLateralDirection(toEnemy, Time.GetTicksMsec());
                        _player.SetAiDesiredMovement(lateral, false);
                        return;
                    }

                    Vector2 shortRetreat = (_player.GlobalPosition - nearestEnemy.GlobalPosition).Normalized();
                    _player.SetAiDesiredMovement(shortRetreat, false);
                    return;
                }
            }

            if (distance > AttackApproachDistance)
            {
                _player.SetAiDesiredMovement(toEnemy.Normalized(), preferRun || distance > RunDistanceThreshold);
                return;
            }

            _player.SetAiDesiredMovement(Vector2.Zero, false);
            if (_player.AttackTimer <= 0f)
            {
                _player.QueueAiAttack();
            }
        }

        private void ApplyRetreatControl()
        {
            if (_player == null)
            {
                return;
            }

            var nearestEnemy = ResolveNearestEnemy();
            if (nearestEnemy == null)
            {
                _player.ClearAiControlCommands();
                return;
            }

            Vector2 away = (_player.GlobalPosition - nearestEnemy.GlobalPosition).Normalized();
            _player.SetAiDesiredMovement(away, true);
        }

        private void ApplyRepositionControl()
        {
            if (_player == null)
            {
                return;
            }

            var nearestEnemy = ResolveNearestEnemy();
            if (nearestEnemy == null)
            {
                _player.ClearAiControlCommands();
                return;
            }

            Vector2 toEnemy = nearestEnemy.GlobalPosition - _player.GlobalPosition;
            Vector2 lateral = new Vector2(-toEnemy.Y, toEnemy.X).Normalized();
            _player.SetAiDesiredMovement(lateral, false);
        }

        private GameActor? ResolveNearestEnemy()
        {
            if (_player == null || string.IsNullOrWhiteSpace(EnemyGroupName))
            {
                return null;
            }

            GameActor? nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (Node node in GetTree().GetNodesInGroup(EnemyGroupName))
            {
                if (node is not GameActor actor || actor.IsDead || actor.IsDeathSequenceActive)
                {
                    continue;
                }

                float distance = _player.GlobalPosition.DistanceTo(actor.GlobalPosition);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = actor;
                }
            }

            return nearest;
        }

        private static bool TryParseQuickBarIndex(string rawTarget, out int slotIndex)
        {
            slotIndex = -1;
            if (string.IsNullOrWhiteSpace(rawTarget))
            {
                return false;
            }

            string text = rawTarget.Trim().ToLowerInvariant();
            text = text.Replace("quickbar_", string.Empty, StringComparison.Ordinal)
                       .Replace("slot_", string.Empty, StringComparison.Ordinal)
                       .Replace("slot", string.Empty, StringComparison.Ordinal)
                       .Replace("quickbar", string.Empty, StringComparison.Ordinal)
                       .Trim();

            if (!int.TryParse(text, out int parsedHumanIndex))
            {
                return false;
            }

            if (parsedHumanIndex < 1 || parsedHumanIndex > 5)
            {
                return false;
            }

            slotIndex = parsedHumanIndex - 1;
            return true;
        }

        private static bool ShouldExecuteImmediately(AiDecision decision)
        {
            return decision.Intent switch
            {
                "use_skill" => true,
                "switch_weapon" => true,
                _ => false
            };
        }

        private void UpdateIntentStreak(string intent)
        {
            string normalized = string.IsNullOrWhiteSpace(intent)
                ? string.Empty
                : intent.Trim().ToLowerInvariant();

            if (normalized == _lastStructuredIntent)
            {
                _sameIntentStreak++;
            }
            else
            {
                _sameIntentStreak = 1;
                _lastStructuredIntent = normalized;
            }
        }

        private static Vector2 ComputeLateralDirection(Vector2 toEnemy, ulong nowMs)
        {
            if (toEnemy.LengthSquared() < 0.0001f)
            {
                return Vector2.Zero;
            }

            Vector2 baseLateral = new Vector2(-toEnemy.Y, toEnemy.X).Normalized();
            long phase = (long)(nowMs / 450UL);
            return (phase % 2 == 0) ? baseLateral : -baseLateral;
        }

        private void PublishReject(string reason)
        {
            LastExecutionError = reason ?? string.Empty;
            LastExecutionJson = string.Empty;
            EmitSignal(SignalName.ExecutionRejected, LastExecutionError);
        }

        private static Godot.Collections.Dictionary<string, Variant> BuildAppliedResult(AiDecision decision, string appliedAction, string detail)
        {
            return BuildBaseResult(decision, "applied", "ok", appliedAction, detail);
        }

        private static Godot.Collections.Dictionary<string, Variant> BuildNoopResult(AiDecision decision, string status, string message)
        {
            return BuildBaseResult(decision, status, message, "noop", string.Empty);
        }

        private static Godot.Collections.Dictionary<string, Variant> BuildRejectResult(AiDecision decision, string message)
        {
            return BuildBaseResult(decision, "rejected", message, "none", string.Empty);
        }

        private static Godot.Collections.Dictionary<string, Variant> BuildBaseResult(
            AiDecision decision,
            string status,
            string message,
            string appliedAction,
            string detail)
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["timestamp_ms"] = Time.GetTicksMsec(),
                ["status"] = status,
                ["message"] = message,
                ["intent"] = decision.Intent,
                ["target"] = decision.Target,
                ["urgency"] = decision.Urgency,
                ["duration_seconds"] = decision.DurationSeconds,
                ["reason"] = decision.Reason,
                ["applied_action"] = appliedAction,
                ["detail"] = detail
            };
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            if (path.IsEmpty)
            {
                return path;
            }

            string text = path.ToString();
            return text.StartsWith("../", StringComparison.Ordinal)
                ? new NodePath(text[3..])
                : path;
        }
    }
}
