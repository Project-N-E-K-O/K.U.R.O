using Godot;
using Kuros.Items.Tags;
using Kuros.Systems.AI;

namespace Kuros.Companions
{
    /// <summary>
    /// Bridge layer for converting structured JSON suggestions into safe SupportDecision objects.
    /// Model output is treated as suggestion only and must pass local whitelist validation.
    /// </summary>
    public partial class P2SupportDecisionBridge : Node
    {
        public bool TryBuildDecisionFromAiDecision(AiDecision aiDecision, out SupportDecision decision, out string rejectReason)
        {
            decision = new SupportDecision { IsValid = false };
            rejectReason = string.Empty;

            if (aiDecision == null || !aiDecision.IsValid)
            {
                rejectReason = aiDecision?.ParseError ?? "invalid ai decision";
                return false;
            }

            string intent = (aiDecision.Intent ?? string.Empty).Trim().ToLowerInvariant();
            string target = string.IsNullOrWhiteSpace(aiDecision.Target) ? "player" : aiDecision.Target;
            string urgency = NormalizeUrgency(aiDecision.Urgency);
            float duration = Mathf.Clamp(aiDecision.DurationSeconds, 0f, 8f);
            string reason = aiDecision.Reason ?? string.Empty;

            // Map combat intents into companion-support intents.
            string mappedIntent = intent switch
            {
                "retreat" => "suggest_retreat",
                "reposition" => "suggest_retreat",
                "loot" => "suggest_pickup",
                "use_skill" => "trigger_support_skill",
                "use_support_item" => "use_support_item",
                "heal" => "use_support_item",
                "use_item" => "use_support_item",
                "attack" => "hold",
                "switch_weapon" => "hold",
                _ => intent
            };

            string message = mappedIntent switch
            {
                "suggest_retreat" => "先拉开距离更安全",
                "suggest_pickup" => "附近有掉落，注意拾取",
                _ => string.Empty
            };

            return TryBuildDecisionCore(
                mappedIntent,
                target,
                ItemTagIds.Food,
                urgency,
                duration,
                reason,
                message,
                out decision,
                out rejectReason);
        }

        public bool TryBuildDecisionFromJson(string json, out SupportDecision decision, out string rejectReason)
        {
            decision = new SupportDecision { IsValid = false };
            rejectReason = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                rejectReason = "empty ai json";
                return false;
            }

            Variant parsed = Json.ParseString(json);
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                rejectReason = "ai json is not an object";
                return false;
            }

            var dict = (Godot.Collections.Dictionary)parsed;
            string intent = GetString(dict, "intent").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(intent))
            {
                rejectReason = "intent is required";
                return false;
            }

            string target = GetString(dict, "target", "player");
            string itemTag = GetString(dict, "item_tag", ItemTagIds.Food);
            string urgency = NormalizeUrgency(GetString(dict, "urgency", "medium"));
            float duration = Mathf.Clamp(GetFloat(dict, "duration", 1.8f), 0f, 8f);
            string reason = GetString(dict, "reason");
            string message = GetString(dict, "message");

            return TryBuildDecisionCore(intent, target, itemTag, urgency, duration, reason, message, out decision, out rejectReason);
        }

        private static bool TryBuildDecisionCore(
            string intent,
            string target,
            string itemTag,
            string urgency,
            float duration,
            string reason,
            string message,
            out SupportDecision decision,
            out string rejectReason)
        {
            rejectReason = string.Empty;

            // Convert suggestion intents into executable local decisions.
            decision = intent switch
            {
                "show_hint" => SupportDecision.Hint(
                    message: string.IsNullOrWhiteSpace(message) ? "收到建议" : message,
                    sourceRule: "ai_bridge",
                    reason: reason,
                    urgency: urgency,
                    durationSeconds: duration,
                    target: target),

                "suggest_retreat" => SupportDecision.Hint(
                    message: string.IsNullOrWhiteSpace(message) ? "先拉开距离更安全" : message,
                    sourceRule: "ai_bridge",
                    reason: string.IsNullOrWhiteSpace(reason) ? "ai suggested retreat" : reason,
                    urgency: urgency,
                    durationSeconds: Mathf.Max(1.2f, duration),
                    target: target),

                "suggest_pickup" => SupportDecision.Hint(
                    message: string.IsNullOrWhiteSpace(message) ? "附近有掉落，注意拾取" : message,
                    sourceRule: "ai_bridge",
                    reason: string.IsNullOrWhiteSpace(reason) ? "ai suggested pickup" : reason,
                    urgency: urgency,
                    durationSeconds: Mathf.Max(1.2f, duration),
                    target: target),

                "use_support_item" => SupportDecision.UseSupportItem(
                    sourceRule: "ai_bridge",
                    reason: string.IsNullOrWhiteSpace(reason) ? "ai suggested support item" : reason,
                    itemTag: string.IsNullOrWhiteSpace(itemTag) ? ItemTagIds.Food : itemTag,
                    urgency: urgency,
                    target: target),

                "trigger_support_skill" => SupportDecision.TriggerSupportSkill(
                    sourceRule: "ai_bridge",
                    reason: string.IsNullOrWhiteSpace(reason) ? "ai suggested support skill" : reason,
                    target: string.IsNullOrWhiteSpace(target) ? "shield" : target,
                    urgency: urgency),

                "hold" => new SupportDecision
                {
                    IsValid = true,
                    Intent = "hold",
                    Target = string.IsNullOrWhiteSpace(target) ? "player" : target,
                    Urgency = urgency,
                    Reason = string.IsNullOrWhiteSpace(reason) ? "ai suggested hold" : reason,
                    SourceRule = "ai_bridge"
                },

                _ => new SupportDecision { IsValid = false }
            };

            if (!decision.IsValid)
            {
                rejectReason = $"intent '{intent}' is not supported";
                return false;
            }

            return true;
        }

        public bool TryValidateDecision(SupportDecision decision, GameState state, out string rejectReason)
        {
            rejectReason = string.Empty;
            if (decision == null || !decision.IsValid)
            {
                rejectReason = "invalid decision";
                return false;
            }

            string intent = (decision.Intent ?? string.Empty).Trim().ToLowerInvariant();
            if (intent != "show_hint" &&
                intent != "suggest_retreat" &&
                intent != "suggest_pickup" &&
                intent != "use_support_item" &&
                intent != "trigger_support_skill" &&
                intent != "hold")
            {
                rejectReason = $"intent '{intent}' is not in local whitelist";
                return false;
            }

            if (intent == "use_support_item" && state.PlayerMaxHp > 0 && state.PlayerHp >= state.PlayerMaxHp)
            {
                rejectReason = "player hp already full";
                return false;
            }

            if (intent == "trigger_support_skill" && state.AliveEnemyCount <= 0)
            {
                rejectReason = "no alive enemy for support skill";
                return false;
            }

            return true;
        }

        private static string NormalizeUrgency(string urgency)
        {
            string value = (urgency ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "low" => "low",
                "high" => "high",
                _ => "medium"
            };
        }

        private static string GetString(Godot.Collections.Dictionary dict, string key, string fallback = "")
        {
            if (!dict.ContainsKey(key))
            {
                return fallback;
            }

            Variant value = dict[key];
            return value.VariantType == Variant.Type.Nil ? fallback : value.ToString();
        }

        private static float GetFloat(Godot.Collections.Dictionary dict, string key, float fallback)
        {
            if (!dict.ContainsKey(key))
            {
                return fallback;
            }

            Variant value = dict[key];
            return value.VariantType switch
            {
                Variant.Type.Float => (float)value,
                Variant.Type.Int => (int)value,
                _ => float.TryParse(value.ToString(), out float parsed) ? parsed : fallback
            };
        }
    }
}
