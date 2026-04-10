using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// Ollama /api/generate client for local model inference.
    /// Supports both streaming (NDJSON) and non-streaming responses.
    /// </summary>
    [GlobalClass]
    public partial class OllamaGenerateClient : Node
    {
        [Signal] public delegate void StreamChunkReceivedEventHandler(string chunkText);
        [Signal] public delegate void StreamCompletedEventHandler(string fullText);
        [Signal] public delegate void RequestFailedEventHandler(string errorMessage);

        [Export] public string Endpoint { get; set; } = "http://localhost:11434/api/generate";
        [Export] public string DefaultModel { get; set; } = "llama3";
        [Export] public bool DefaultStream { get; set; } = false;
        [Export(PropertyHint.Range, "1,600,1")] public int TimeoutSeconds { get; set; } = 120;
        [Export(PropertyHint.Range, "16,4096,1")] public int MaxPredictTokens { get; set; } = 512;
        [Export(PropertyHint.Range, "0,2,0.01")] public float Temperature { get; set; } = 0.2f;
        [Export] public bool DisableThinking { get; set; } = true;

        private static readonly System.Net.Http.HttpClient SharedHttpClient = new();

        public async Task<OllamaGenerateResult> GenerateAsync(
            string prompt,
            string? model = null,
            bool? stream = null,
            string? system = null)
        {
            string requestModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
            bool requestStream = stream ?? DefaultStream;

            var payload = new Godot.Collections.Dictionary<string, Variant>
            {
                ["model"] = requestModel,
                ["prompt"] = prompt,
                ["stream"] = requestStream
            };

            payload["options"] = new Godot.Collections.Dictionary<string, Variant>
            {
                ["num_predict"] = Mathf.Max(16, MaxPredictTokens),
                ["temperature"] = Mathf.Clamp(Temperature, 0f, 2f)
            };

            if (!string.IsNullOrWhiteSpace(system))
            {
                payload["system"] = system;
            }

            if (DisableThinking)
            {
                // Qwen models may output only `thinking` with empty `response`.
                // Request non-thinking mode to increase chance of direct final answer text.
                payload["think"] = false;
            }

            return await SendGenerateRequestAsync(payload, requestStream);
        }

        public async Task<OllamaGenerateResult> GenerateFromGameStateAsync(
            GameStateProvider provider,
            string instruction,
            string? model = null,
            bool? stream = null)
        {
            if (provider == null)
            {
                return OllamaGenerateResult.FromError("GameStateProvider is null.");
            }

            string prompt = BuildGameStatePrompt(provider.CaptureGameState(), instruction);
            return await GenerateAsync(prompt, model, stream);
        }

        public static string BuildGameStatePrompt(GameState state, string instruction)
        {
            string safeInstruction = string.IsNullOrWhiteSpace(instruction)
                ? "Decide the next action for a fast-paced action game and prefer proactive combat behavior."
                : instruction.Trim();

            return string.Join("\n", new[]
            {
                "You are an in-game decision model.",
                "This is a fast-paced action game, not a cautious turn-based tactics game.",
                "Given the following current game state, return one executable decision.",
                string.Empty,
                "Instruction:",
                safeInstruction,
                string.Empty,
                "Decision policy:",
                "- When enemies are present, default to proactive combat behavior.",
                "- Prefer attack, use_skill, or switch_weapon over retreat.",
                "- Do not choose retreat just because enemies are nearby.",
                "- Choose retreat only if the player is in clear lethal danger, such as very low hp or being overwhelmed while under attack.",
                "- Prefer use_skill when pressure is high and a stronger immediate action makes sense.",
                "- Prefer switch_weapon only when it clearly improves the current combat situation.",
                "- Do not choose loot while enemies are actively threatening the player.",
                "- Reposition should stay combat-focused and short-term, not passive avoidance.",
                "- If alive_enemy_count > 0 and player hp is not critically low, usually return attack or use_skill.",
                "- Avoid repeating the same intent too many times in a row when situation is unchanged.",
                "- Under close-range pressure, alternate between attack and reposition to kite instead of face-tanking forever.",
                "- If hp is low and under_attack is true, prefer retreat or reposition over direct attack.",
                string.Empty,
                "GameState(JSON):",
                state.ToAiInputJson(pretty: false),
                string.Empty,
                "Output format:",
                "- Return strict JSON object only",
                "- Do not wrap JSON in markdown code fences",
                "- Required keys: intent, target, urgency, duration_seconds, reason",
                "- intent must be a short snake_case action such as attack, retreat, reposition, loot, switch_weapon, use_skill",
                "- target must be a short target label such as nearest_enemy, lowest_hp_enemy, safe_position, nearby_loot, none",
                "- urgency must be one of: low, medium, high, critical",
                "- duration_seconds must be a non-negative number",
                "- reason must be one short sentence",
                "- Keep the reason grounded in immediate action-game combat logic",
                string.Empty,
                "Example:",
                "{\"intent\":\"attack\",\"target\":\"nearest_enemy\",\"urgency\":\"high\",\"duration_seconds\":1.2,\"reason\":\"A nearby enemy is within range and the player is not under immediate lethal threat.\"}"
            });
        }

        private async Task<OllamaGenerateResult> SendGenerateRequestAsync(
            Godot.Collections.Dictionary<string, Variant> payload,
            bool streaming)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new StringContent(Json.Stringify(payload), Encoding.UTF8, "application/json")
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Mathf.Max(1, TimeoutSeconds)));

                using HttpResponseMessage response = await SharedHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    string err = $"Ollama request failed ({(int)response.StatusCode}): {errBody}";
                    EmitSignal(SignalName.RequestFailed, err);
                    return OllamaGenerateResult.FromError(err);
                }

                if (streaming)
                {
                    return await ParseStreamingResponseAsync(response, cts.Token);
                }

                return await ParseSingleJsonResponseAsync(response, cts.Token);
            }
            catch (OperationCanceledException)
            {
                string err = $"Ollama request timeout after {Mathf.Max(1, TimeoutSeconds)} second(s).";
                EmitSignal(SignalName.RequestFailed, err);
                return OllamaGenerateResult.FromError(err);
            }
            catch (Exception ex)
            {
                string err = $"Ollama request exception: {ex.Message}";
                EmitSignal(SignalName.RequestFailed, err);
                return OllamaGenerateResult.FromError(err);
            }
        }

        private async Task<OllamaGenerateResult> ParseSingleJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            Variant parsed = Json.ParseString(body);
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                string err = "Ollama non-stream response is not a JSON object.";
                EmitSignal(SignalName.RequestFailed, err);
                return OllamaGenerateResult.FromError(err);
            }

            var dict = parsed.AsGodotDictionary();
            string text = GetString(dict, "response");
            string thinking = GetString(dict, "thinking");
            var result = new OllamaGenerateResult
            {
                Success = true,
                Model = GetString(dict, "model"),
                CreatedAt = GetString(dict, "created_at"),
                ResponseText = text,
                ThinkingText = thinking,
                Done = GetBool(dict, "done"),
                DoneReason = GetString(dict, "done_reason"),
                Context = GetIntArray(dict, "context"),
                RawFinalObject = dict,
                TotalDuration = GetLong(dict, "total_duration"),
                LoadDuration = GetLong(dict, "load_duration"),
                PromptEvalCount = GetInt(dict, "prompt_eval_count"),
                PromptEvalDuration = GetLong(dict, "prompt_eval_duration"),
                EvalCount = GetInt(dict, "eval_count"),
                EvalDuration = GetLong(dict, "eval_duration")
            };

            ApplyThinkingFallbackIfNeeded(result);

            EmitSignal(SignalName.StreamCompleted, result.ResponseText);
            return result;
        }

        private async Task<OllamaGenerateResult> ParseStreamingResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var result = new OllamaGenerateResult { Success = true };
            var sb = new StringBuilder(1024);
            var thinkingSb = new StringBuilder(1024);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Variant parsed = Json.ParseString(line);
                if (parsed.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var chunkObj = parsed.AsGodotDictionary();
                result.RawChunks.Add(chunkObj);

                string chunkText = GetString(chunkObj, "response");
                if (!string.IsNullOrEmpty(chunkText))
                {
                    sb.Append(chunkText);
                    EmitSignal(SignalName.StreamChunkReceived, chunkText);
                }

                string thinkingChunk = GetString(chunkObj, "thinking");
                if (!string.IsNullOrEmpty(thinkingChunk))
                {
                    thinkingSb.Append(thinkingChunk);
                }

                result.Model = string.IsNullOrEmpty(result.Model) ? GetString(chunkObj, "model") : result.Model;
                result.CreatedAt = string.IsNullOrEmpty(result.CreatedAt) ? GetString(chunkObj, "created_at") : result.CreatedAt;

                bool done = GetBool(chunkObj, "done");
                if (done)
                {
                    result.Done = true;
                    result.DoneReason = GetString(chunkObj, "done_reason");
                    result.Context = GetIntArray(chunkObj, "context");
                    result.RawFinalObject = chunkObj;
                    result.TotalDuration = GetLong(chunkObj, "total_duration");
                    result.LoadDuration = GetLong(chunkObj, "load_duration");
                    result.PromptEvalCount = GetInt(chunkObj, "prompt_eval_count");
                    result.PromptEvalDuration = GetLong(chunkObj, "prompt_eval_duration");
                    result.EvalCount = GetInt(chunkObj, "eval_count");
                    result.EvalDuration = GetLong(chunkObj, "eval_duration");
                    break;
                }
            }

            result.ResponseText = sb.ToString();
            result.ThinkingText = thinkingSb.ToString();
            ApplyThinkingFallbackIfNeeded(result);
            EmitSignal(SignalName.StreamCompleted, result.ResponseText);
            return result;
        }

        private static void ApplyThinkingFallbackIfNeeded(OllamaGenerateResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.ResponseText))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ThinkingText))
            {
                return;
            }

            result.ResponseText = result.ThinkingText;
            result.UsedThinkingFallback = true;
        }

        private static string GetString(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return string.Empty;
            return value.VariantType == Variant.Type.String ? value.AsString() : value.ToString();
        }

        private static bool GetBool(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return false;
            return value.VariantType == Variant.Type.Bool && value.AsBool();
        }

        private static int GetInt(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return 0;
            return value.VariantType switch
            {
                Variant.Type.Int => (int)value.AsInt64(),
                Variant.Type.Float => (int)value.AsDouble(),
                _ => 0
            };
        }

        private static long GetLong(Godot.Collections.Dictionary dict, string key)
        {
            if (!dict.TryGetValue(key, out Variant value)) return 0;
            return value.VariantType switch
            {
                Variant.Type.Int => value.AsInt64(),
                Variant.Type.Float => (long)value.AsDouble(),
                _ => 0
            };
        }

        private static Godot.Collections.Array<int> GetIntArray(Godot.Collections.Dictionary dict, string key)
        {
            var result = new Godot.Collections.Array<int>();
            if (!dict.TryGetValue(key, out Variant value)) return result;
            if (value.VariantType != Variant.Type.Array) return result;

            var arr = value.AsGodotArray();
            foreach (Variant item in arr)
            {
                if (item.VariantType == Variant.Type.Int)
                {
                    result.Add((int)item.AsInt64());
                }
            }

            return result;
        }
    }

    public sealed class OllamaGenerateResult
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;

        public string Model { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string ResponseText { get; set; } = string.Empty;
        public string ThinkingText { get; set; } = string.Empty;
        public bool UsedThinkingFallback { get; set; }
        public bool Done { get; set; }
        public string DoneReason { get; set; } = string.Empty;

        public long TotalDuration { get; set; }
        public long LoadDuration { get; set; }
        public int PromptEvalCount { get; set; }
        public long PromptEvalDuration { get; set; }
        public int EvalCount { get; set; }
        public long EvalDuration { get; set; }

        public Godot.Collections.Array<int> Context { get; set; } = new();
        public Godot.Collections.Array<Godot.Collections.Dictionary> RawChunks { get; set; } = new();
        public Godot.Collections.Dictionary RawFinalObject { get; set; } = new();

        public static OllamaGenerateResult FromError(string error)
        {
            return new OllamaGenerateResult
            {
                Success = false,
                ErrorMessage = error
            };
        }
    }
}
