using Godot;
using Kuros.Items.Tags;

namespace Kuros.Companions
{
    /// <summary>
    /// Structured decision payload for P2 support behavior.
    /// </summary>
    public sealed class SupportDecision
    {
        public bool IsValid { get; init; }
        public string Intent { get; init; } = string.Empty;
        public string Target { get; init; } = "player";
        public string ItemTag { get; init; } = string.Empty;
        public string Urgency { get; init; } = "medium";
        public float DurationSeconds { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string SourceRule { get; init; } = string.Empty;

        public static SupportDecision Hint(
            string message,
            string sourceRule,
            string reason,
            string urgency = "medium",
            float durationSeconds = 1.8f,
            string target = "player")
        {
            return new SupportDecision
            {
                IsValid = !string.IsNullOrWhiteSpace(message),
                Intent = "show_hint",
                Target = string.IsNullOrWhiteSpace(target) ? "player" : target,
                Urgency = string.IsNullOrWhiteSpace(urgency) ? "medium" : urgency,
                DurationSeconds = Mathf.Max(0f, durationSeconds),
                Reason = reason ?? string.Empty,
                Message = message ?? string.Empty,
                SourceRule = sourceRule ?? string.Empty
            };
        }

        public static SupportDecision UseSupportItem(
            string sourceRule,
            string reason,
            string itemTag = ItemTagIds.Food,
            string urgency = "high",
            string target = "player")
        {
            return new SupportDecision
            {
                IsValid = true,
                Intent = "use_support_item",
                Target = string.IsNullOrWhiteSpace(target) ? "player" : target,
                ItemTag = itemTag ?? string.Empty,
                Urgency = string.IsNullOrWhiteSpace(urgency) ? "high" : urgency,
                Reason = reason ?? string.Empty,
                SourceRule = sourceRule ?? string.Empty
            };
        }

        public static SupportDecision TriggerSupportSkill(
            string sourceRule,
            string reason,
            string target = "shield",
            string urgency = "medium")
        {
            return new SupportDecision
            {
                IsValid = true,
                Intent = "trigger_support_skill",
                Target = string.IsNullOrWhiteSpace(target) ? "shield" : target,
                Urgency = string.IsNullOrWhiteSpace(urgency) ? "medium" : urgency,
                Reason = reason ?? string.Empty,
                SourceRule = sourceRule ?? string.Empty
            };
        }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["is_valid"] = IsValid,
                ["intent"] = Intent,
                ["target"] = Target,
                ["item_tag"] = ItemTag,
                ["urgency"] = Urgency,
                ["duration"] = DurationSeconds,
                ["reason"] = Reason,
                ["message"] = Message,
                ["source_rule"] = SourceRule
            };
        }

        public string ToJson(bool pretty = false)
        {
            return Json.Stringify(ToDictionary(), pretty ? "  " : string.Empty);
        }
    }
}