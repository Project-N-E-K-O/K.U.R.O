using System;
using System.Text;
using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// Structured AI decision parsed from model output.
    /// </summary>
    public sealed class AiDecision
    {
        public bool IsValid { get; init; }
        public string Intent { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public string Urgency { get; init; } = "medium";
        public float DurationSeconds { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string RawResponse { get; init; } = string.Empty;
        public string ParseError { get; init; } = string.Empty;

        public static AiDecision FromError(string rawResponse, string parseError)
        {
            return new AiDecision
            {
                IsValid = false,
                RawResponse = rawResponse ?? string.Empty,
                ParseError = parseError ?? "Unknown parse error."
            };
        }

        public static AiDecision Parse(string rawResponse)
        {
            string safeRaw = rawResponse ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeRaw))
            {
                return FromError(safeRaw, "AI response is empty.");
            }

            string jsonCandidate = ExtractJsonObject(safeRaw);
            if (string.IsNullOrWhiteSpace(jsonCandidate))
            {
                return FromError(safeRaw, "AI response does not contain a JSON object.");
            }

            Variant parsed = Json.ParseString(jsonCandidate);
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                return FromError(safeRaw, "AI response JSON is not an object.");
            }

            var dict = parsed.AsGodotDictionary();
            string intent = NormalizeLower(GetFirstString(dict, "intent", "action", "next_action"));
            string target = GetFirstString(dict, "target", "target_hint", "focus", "subject").Trim();
            string urgency = NormalizeUrgency(GetFirstString(dict, "urgency", "priority", "risk_level"));
            float durationSeconds = GetFirstFloat(dict, "duration_seconds", "duration", "hold_seconds");
            string reason = GetFirstString(dict, "reason", "rationale", "summary").Trim();

            if (string.IsNullOrWhiteSpace(intent))
            {
                return FromError(safeRaw, "AI decision JSON is missing required field 'intent'.");
            }

            return new AiDecision
            {
                IsValid = true,
                Intent = intent,
                Target = string.IsNullOrWhiteSpace(target) ? "none" : target,
                Urgency = urgency,
                DurationSeconds = Mathf.Max(0f, durationSeconds),
                Reason = reason,
                RawResponse = safeRaw
            };
        }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["is_valid"] = IsValid,
                ["intent"] = Intent,
                ["target"] = Target,
                ["urgency"] = Urgency,
                ["duration_seconds"] = DurationSeconds,
                ["reason"] = Reason,
                ["parse_error"] = ParseError
            };
        }

        public string ToJson(bool pretty = true)
        {
            return Json.Stringify(ToDictionary(), pretty ? "  " : string.Empty);
        }

        public string ToDebugText()
        {
            if (!IsValid)
            {
                return string.Join("\n", new[]
                {
                    "status=invalid",
                    $"parse_error={ParseError}"
                });
            }

            return string.Join("\n", new[]
            {
                "status=valid",
                $"intent={Intent}",
                $"target={Target}",
                $"urgency={Urgency}",
                $"duration_seconds={DurationSeconds:0.##}",
                $"reason={Reason}"
            });
        }

        private static string ExtractJsonObject(string rawText)
        {
            string trimmed = rawText.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int start = trimmed.IndexOf('{');
            if (start < 0)
            {
                return string.Empty;
            }

            bool inString = false;
            bool escape = false;
            int depth = 0;
            var builder = new StringBuilder(trimmed.Length);

            for (int i = start; i < trimmed.Length; i++)
            {
                char current = trimmed[i];
                builder.Append(current);

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return builder.ToString();
                    }
                }
            }

            return string.Empty;
        }

        private static string GetFirstString(Godot.Collections.Dictionary dict, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!dict.TryGetValue(key, out Variant value))
                {
                    continue;
                }

                string text = value.VariantType == Variant.Type.String ? value.AsString() : value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return string.Empty;
        }

        private static float GetFirstFloat(Godot.Collections.Dictionary dict, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!dict.TryGetValue(key, out Variant value))
                {
                    continue;
                }

                switch (value.VariantType)
                {
                    case Variant.Type.Float:
                        return (float)value.AsDouble();
                    case Variant.Type.Int:
                        return value.AsInt64();
                    case Variant.Type.String:
                        if (float.TryParse(value.AsString(), out float parsed))
                        {
                            return parsed;
                        }

                        break;
                }
            }

            return 0f;
        }

        private static string NormalizeLower(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeUrgency(string value)
        {
            string normalized = NormalizeLower(value);
            return normalized switch
            {
                "low" => "low",
                "medium" => "medium",
                "high" => "high",
                "critical" => "critical",
                _ => "medium"
            };
        }
    }
}