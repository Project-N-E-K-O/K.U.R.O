using Godot;
using Kuros.Items.Tags;

namespace Kuros.Companions
{
    /// <summary>
    /// Applies structured support decisions through a local whitelist.
    /// </summary>
    public partial class P2SupportExecutor : Node
    {
        [Signal] public delegate void DecisionAppliedEventHandler(string decisionJson);
        [Signal] public delegate void DecisionRejectedEventHandler(string reason);

        [Export] public NodePath CompanionControllerPath { get; set; } = new("..");
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        [Export] public string DefaultSupportSkillAction { get; set; } = "weapon_skill_block";
        [Export] public bool ConsumeOnlyMatchingTag { get; set; } = true;
        [Export(PropertyHint.Range, "0,20,0.1")] public float SupportSkillCooldownSeconds { get; set; } = 3.0f;
        [Export(PropertyHint.Range, "0,20,0.1")] public float SupportItemCooldownSeconds { get; set; } = 6.0f;
        [Export] public bool EnableLogging { get; set; } = false;

        private P2CompanionController? _companionController;
        private global::SamplePlayer? _player;

        public string LastAppliedDecisionJson { get; private set; } = string.Empty;
        public string LastRejectedReason { get; private set; } = string.Empty;
        public string LastIntent { get; private set; } = string.Empty;
        public ulong LastDecisionAtMs { get; private set; }
        public string LastResult { get; private set; } = "none";
        public string LastActionDetail { get; private set; } = string.Empty;

        private ulong _nextSupportSkillAtMs;
        private ulong _nextSupportItemAtMs;

        public bool TryExecute(SupportDecision decision)
        {
            ResolveDependencies();

            if (_companionController == null)
            {
                LastResult = "rejected";
                EmitSignal(SignalName.DecisionRejected, "companion controller not available");
                return false;
            }

            if (decision == null || !decision.IsValid)
            {
                LastResult = "rejected";
                EmitSignal(SignalName.DecisionRejected, "invalid support decision");
                return false;
            }

            string intent = decision.Intent.Trim().ToLowerInvariant();
            LastIntent = intent;
            LastDecisionAtMs = Time.GetTicksMsec();
            switch (intent)
            {
                case "show_hint":
                    _companionController.PushHint(decision.Message);
                    if (EnableLogging)
                    {
                        GD.Print($"[P2SupportExecutor] applied show_hint: {decision.Message}");
                    }
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = decision.Message;
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "hold":
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = "hold";
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "trigger_support_skill":
                    return ExecuteSupportSkill(decision);

                case "use_support_item":
                    return ExecuteSupportItem(decision);

                default:
                    LastRejectedReason = $"intent '{intent}' is not in whitelist";
                    LastResult = "rejected";
                    LastActionDetail = intent;
                    EmitSignal(SignalName.DecisionRejected, $"intent '{intent}' is not in whitelist");
                    return false;
            }
        }

        private bool ExecuteSupportSkill(SupportDecision decision)
        {
            if (_player == null)
            {
                LastRejectedReason = "player not available for support skill";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "player not available for support skill");
                return false;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextSupportSkillAtMs)
            {
                LastRejectedReason = "support skill on cooldown";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "support skill on cooldown");
                return false;
            }

            string actionName = ResolveSupportSkillAction(decision.Target);
            if (string.IsNullOrWhiteSpace(actionName))
            {
                LastRejectedReason = "support skill action is empty";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "support skill action is empty");
                return false;
            }

            if (_player.WeaponSkillController?.TryTriggerActionSkill(actionName) != true)
            {
                LastRejectedReason = $"support skill '{actionName}' unavailable";
                LastResult = "rejected";
                LastActionDetail = actionName;
                EmitSignal(SignalName.DecisionRejected, $"support skill '{actionName}' unavailable");
                return false;
            }

            if (EnableLogging)
            {
                GD.Print($"[P2SupportExecutor] applied trigger_support_skill: {actionName}");
            }

            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = actionName;
            _nextSupportSkillAtMs = now + SecondsToMs(SupportSkillCooldownSeconds);
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        private bool ExecuteSupportItem(SupportDecision decision)
        {
            if (_player?.InventoryComponent == null)
            {
                LastRejectedReason = "inventory component unavailable for support item";
                LastResult = "rejected";
                LastActionDetail = "use_support_item";
                EmitSignal(SignalName.DecisionRejected, "inventory component unavailable for support item");
                return false;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextSupportItemAtMs)
            {
                LastRejectedReason = "support item on cooldown";
                LastResult = "rejected";
                LastActionDetail = "use_support_item";
                EmitSignal(SignalName.DecisionRejected, "support item on cooldown");
                return false;
            }

            var inventory = _player.InventoryComponent;
            string requiredTag = string.IsNullOrWhiteSpace(decision.ItemTag) ? ItemTagIds.Food : decision.ItemTag;
            if (!inventory.TryConsumeFirstTaggedItem(requiredTag, _player))
            {
                LastRejectedReason = $"no consumable support item found for tag '{requiredTag}'";
                LastResult = "rejected";
                LastActionDetail = requiredTag;
                EmitSignal(SignalName.DecisionRejected, $"no consumable support item found for tag '{requiredTag}'");
                return false;
            }

            if (EnableLogging)
            {
                GD.Print($"[P2SupportExecutor] applied use_support_item: tag={requiredTag}");
            }

            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = requiredTag;
            _nextSupportItemAtMs = now + SecondsToMs(SupportItemCooldownSeconds);
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        private void ResolveDependencies()
        {
            if (_companionController != null && IsInstanceValid(_companionController) && _companionController.IsInsideTree())
            {
                ResolvePlayer();
                return;
            }

            _companionController = GetNodeOrNull<P2CompanionController>(CompanionControllerPath)
                ?? GetNodeOrNull<P2CompanionController>(NormalizeRelativePath(CompanionControllerPath));

            ResolvePlayer();
        }

        private void ResolvePlayer()
        {
            if (_player != null && IsInstanceValid(_player) && _player.IsInsideTree())
            {
                return;
            }

            _player = GetNodeOrNull<global::SamplePlayer>(PlayerPath)
                ?? GetNodeOrNull<global::SamplePlayer>(NormalizeRelativePath(PlayerPath))
                ?? GetTree().GetFirstNodeInGroup("player") as global::SamplePlayer;
        }

        private string ResolveSupportSkillAction(string rawTarget)
        {
            string target = (rawTarget ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(target) || target == "player" || target == "self")
            {
                return DefaultSupportSkillAction;
            }

            return target switch
            {
                "block" => "weapon_skill_block",
                "shield" => "weapon_skill_block",
                _ => rawTarget.Trim()
            };
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

        private static ulong SecondsToMs(float seconds)
        {
            return (ulong)Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f);
        }
    }
}