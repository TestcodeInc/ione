using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ione.Core;
using Ione.Tools;

namespace Ione.LLM
{
    // Anthropic Messages API client. Auth is the user's own x-api-key.
    public class AnthropicProvider : ILLMProvider
    {
        const string Endpoint = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2023-06-01";

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        readonly string apiKey;
        readonly string model;

        public AnthropicProvider(string apiKey, string model)
        {
            this.apiKey = apiKey;
            this.model = string.IsNullOrEmpty(model) ? IoneSettings.DefaultAnthropicModel : model;
        }

        public string DisplayName => $"Anthropic · {model}";

        public async Task<LLMResponse> SendAsync(string systemPrompt, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Anthropic API key not set. Open Tools → ione → Settings.");

            var body = BuildBody(systemPrompt, messages);
            IoneDebug.LogRequest($"Anthropic {model}", body);

            var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", ApiVersion);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            IoneDebug.LogResponse($"Anthropic {model}", (int)resp.StatusCode, text);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Anthropic {(int)resp.StatusCode}: {text}");

            return Parse(text);
        }

        string BuildBody(string systemPrompt, IReadOnlyList<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"model\":").Append(Json.Str(model));
            sb.Append(",\"max_tokens\":8192");
            // cache_control on system caches tools+system as the stable
            // prefix; subsequent turns pay ~10% of input for it.
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append(",\"system\":[{\"type\":\"text\",\"text\":");
                sb.Append(Json.Str(systemPrompt));
                sb.Append(",\"cache_control\":{\"type\":\"ephemeral\"}}]");
            }
            sb.Append(",\"tools\":").Append(BuildTools());
            sb.Append(",\"messages\":").Append(BuildMessages(messages));
            sb.Append('}');
            return sb.ToString();
        }

        static string BuildTools()
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var t in ToolSchemas.All)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                sb.Append("\"name\":").Append(Json.Str(t.Name));
                sb.Append(",\"description\":").Append(Json.Str(t.Description));
                sb.Append(",\"input_schema\":").Append(t.ParametersJson);
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Anthropic cache_control marker. Placing it on the last content
        // block of the last message creates a second cache breakpoint past
        // the system+tools prefix, so subsequent turns read the entire
        // prior conversation at the cache rate (~10% of input cost).
        const string CacheTag = ",\"cache_control\":{\"type\":\"ephemeral\"}";

        static string BuildMessages(IReadOnlyList<ChatMessage> messages)
        {
            var sb = new StringBuilder("[");
            int lastIdx = messages.Count - 1;
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(',');
                EmitMessage(sb, messages[i], cacheLast: i == lastIdx);
            }
            sb.Append(']');
            return sb.ToString();
        }

        static void EmitMessage(StringBuilder sb, ChatMessage m, bool cacheLast)
        {
            switch (m.Role)
            {
                case ChatRole.User:
                    if (cacheLast && !string.IsNullOrEmpty(m.Text))
                    {
                        // Switch to array form so we can attach cache_control
                        // to the text block.
                        sb.Append("{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":")
                          .Append(Json.Str(m.Text))
                          .Append(CacheTag).Append("}]}");
                    }
                    else
                    {
                        sb.Append("{\"role\":\"user\",\"content\":").Append(Json.Str(m.Text ?? "")).Append('}');
                    }
                    break;

                case ChatRole.Assistant:
                {
                    // Build each content block as its own string so we can
                    // append cache_control to the last one before closing.
                    var blocks = new List<string>();
                    if (!string.IsNullOrEmpty(m.Text))
                        blocks.Add("{\"type\":\"text\",\"text\":" + Json.Str(m.Text) + "}");
                    if (m.ToolCalls != null)
                    {
                        foreach (var tc in m.ToolCalls)
                        {
                            var input = string.IsNullOrEmpty(tc.ArgsJson) ? "{}" : tc.ArgsJson;
                            blocks.Add("{\"type\":\"tool_use\",\"id\":" + Json.Str(tc.Id) +
                                       ",\"name\":" + Json.Str(tc.Name) +
                                       ",\"input\":" + input + "}");
                        }
                    }
                    sb.Append("{\"role\":\"assistant\",\"content\":[");
                    EmitBlocks(sb, blocks, cacheLast);
                    sb.Append("]}");
                    break;
                }

                case ChatRole.Tool:
                {
                    // Anthropic expects tool_results inside a user-role message.
                    var blocks = new List<string>();
                    if (m.ToolResults != null)
                    {
                        foreach (var tr in m.ToolResults)
                        {
                            var b = new StringBuilder();
                            b.Append("{\"type\":\"tool_result\",\"tool_use_id\":").Append(Json.Str(tr.ToolCallId));
                            if (tr.Images != null && tr.Images.Count > 0)
                            {
                                b.Append(",\"content\":[");
                                b.Append("{\"type\":\"text\",\"text\":").Append(Json.Str(tr.Content ?? "")).Append('}');
                                foreach (var img in tr.Images)
                                {
                                    b.Append(",{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":")
                                     .Append(Json.Str(img.MediaType ?? "image/png"))
                                     .Append(",\"data\":").Append(Json.Str(img.Base64 ?? ""))
                                     .Append("}}");
                                }
                                b.Append(']');
                            }
                            else
                            {
                                b.Append(",\"content\":").Append(Json.Str(tr.Content ?? ""));
                            }
                            if (tr.IsError) b.Append(",\"is_error\":true");
                            b.Append('}');
                            blocks.Add(b.ToString());
                        }
                    }
                    sb.Append("{\"role\":\"user\",\"content\":[");
                    EmitBlocks(sb, blocks, cacheLast);
                    sb.Append("]}");
                    break;
                }
            }
        }

        // Writes each block, attaching cache_control to the closing brace
        // of the final block when cacheLast is true.
        static void EmitBlocks(StringBuilder sb, List<string> blocks, bool cacheLast)
        {
            for (int b = 0; b < blocks.Count; b++)
            {
                if (b > 0) sb.Append(',');
                var block = blocks[b];
                if (cacheLast && b == blocks.Count - 1 && block.EndsWith("}"))
                {
                    sb.Append(block, 0, block.Length - 1);
                    sb.Append(CacheTag);
                    sb.Append('}');
                }
                else
                {
                    sb.Append(block);
                }
            }
        }

        static LLMResponse Parse(string responseJson)
        {
            var root = Json.ParseObject(responseJson);
            var content = Json.GetArray(root, "content");
            var resp = new LLMResponse { ToolCalls = new List<ToolCall>() };
            var textSb = new StringBuilder();
            if (content != null)
            {
                foreach (var blk in content)
                {
                    var o = blk as Dictionary<string, object>;
                    if (o == null) continue;
                    var type = Json.GetString(o, "type");
                    if (type == "text")
                    {
                        if (textSb.Length > 0) textSb.Append('\n');
                        textSb.Append(Json.GetString(o, "text", ""));
                    }
                    else if (type == "tool_use")
                    {
                        var input = Json.GetObject(o, "input");
                        resp.ToolCalls.Add(new ToolCall
                        {
                            Id = Json.GetString(o, "id"),
                            Name = Json.GetString(o, "name"),
                            ArgsJson = input != null ? Json.Serialize(input) : "{}",
                        });
                    }
                }
            }
            resp.Text = textSb.ToString();
            return resp;
        }
    }
}
