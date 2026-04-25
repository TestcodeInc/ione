using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ione.Core;
using Ione.Tools;

namespace Ione.LLM
{
    // OpenAI Chat Completions client with function-calling.
    public class OpenAIProvider : ILLMProvider
    {
        const string Endpoint = "https://api.openai.com/v1/chat/completions";

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        readonly string apiKey;
        readonly string model;

        public OpenAIProvider(string apiKey, string model)
        {
            this.apiKey = apiKey;
            this.model = string.IsNullOrEmpty(model) ? IoneSettings.DefaultOpenAIModel : model;
        }

        public string DisplayName => $"OpenAI · {model}";

        public async Task<LLMResponse> SendAsync(string systemPrompt, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("OpenAI API key not set. Open Tools → ione → Settings.");

            var body = BuildBody(systemPrompt, messages);
            IoneDebug.LogRequest($"OpenAI {model}", body);

            var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            IoneDebug.LogResponse($"OpenAI {model}", (int)resp.StatusCode, text);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"OpenAI {(int)resp.StatusCode}: {text}");

            return Parse(text);
        }

        string BuildBody(string systemPrompt, IReadOnlyList<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"model\":").Append(Json.Str(model));
            sb.Append(",\"messages\":").Append(BuildMessages(systemPrompt, messages));
            sb.Append(",\"tools\":").Append(BuildTools());
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
                sb.Append("{\"type\":\"function\",\"function\":{");
                sb.Append("\"name\":").Append(Json.Str(t.Name));
                sb.Append(",\"description\":").Append(Json.Str(t.Description));
                sb.Append(",\"parameters\":").Append(t.ParametersJson);
                sb.Append("}}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        static string BuildMessages(string systemPrompt, IReadOnlyList<ChatMessage> messages)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":").Append(Json.Str(systemPrompt)).Append('}');
                first = false;
            }
            foreach (var m in messages)
            {
                switch (m.Role)
                {
                    case ChatRole.User:
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"role\":\"user\",\"content\":").Append(Json.Str(m.Text ?? "")).Append('}');
                        break;
                    case ChatRole.Assistant:
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"role\":\"assistant\",\"content\":").Append(Json.Str(m.Text ?? ""));
                        if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                        {
                            sb.Append(",\"tool_calls\":[");
                            for (int i = 0; i < m.ToolCalls.Count; i++)
                            {
                                if (i > 0) sb.Append(',');
                                var tc = m.ToolCalls[i];
                                sb.Append("{\"id\":").Append(Json.Str(tc.Id));
                                sb.Append(",\"type\":\"function\",\"function\":{");
                                sb.Append("\"name\":").Append(Json.Str(tc.Name));
                                sb.Append(",\"arguments\":").Append(Json.Str(tc.ArgsJson ?? "{}"));
                                sb.Append("}}");
                            }
                            sb.Append(']');
                        }
                        sb.Append('}');
                        break;
                    case ChatRole.Tool:
                        if (m.ToolResults != null)
                        {
                            // Tool messages must be contiguous after the
                            // assistant's tool_calls block; emit them first.
                            foreach (var tr in m.ToolResults)
                            {
                                if (!first) sb.Append(',');
                                first = false;
                                sb.Append("{\"role\":\"tool\",\"tool_call_id\":").Append(Json.Str(tr.ToolCallId));
                                sb.Append(",\"content\":").Append(Json.Str(tr.Content ?? "")).Append('}');
                            }
                            // Chat Completions' tool role is text-only, so
                            // images get a follow-up user message.
                            foreach (var tr in m.ToolResults)
                            {
                                if (tr.Images == null || tr.Images.Count == 0) continue;
                                sb.Append(",{\"role\":\"user\",\"content\":[");
                                sb.Append("{\"type\":\"text\",\"text\":")
                                    .Append(Json.Str($"Image returned by tool call {tr.ToolCallId}:"))
                                    .Append('}');
                                foreach (var img in tr.Images)
                                {
                                    var mt = string.IsNullOrEmpty(img.MediaType) ? "image/png" : img.MediaType;
                                    var dataUrl = "data:" + mt + ";base64," + (img.Base64 ?? "");
                                    sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":")
                                        .Append(Json.Str(dataUrl)).Append("}}");
                                }
                                sb.Append("]}");
                            }
                        }
                        break;
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        static LLMResponse Parse(string responseJson)
        {
            var root = Json.ParseObject(responseJson);
            var choices = Json.GetArray(root, "choices");
            var resp = new LLMResponse { ToolCalls = new List<ToolCall>() };
            if (choices == null || choices.Count == 0) return resp;
            var first = choices[0] as Dictionary<string, object>;
            var msg = Json.GetObject(first, "message");
            if (msg == null) return resp;
            resp.Text = Json.GetString(msg, "content", "");
            var toolCalls = Json.GetArray(msg, "tool_calls");
            if (toolCalls != null)
            {
                foreach (var tcObj in toolCalls)
                {
                    var o = tcObj as Dictionary<string, object>;
                    if (o == null) continue;
                    var fn = Json.GetObject(o, "function");
                    if (fn == null) continue;
                    resp.ToolCalls.Add(new ToolCall
                    {
                        Id = Json.GetString(o, "id"),
                        Name = Json.GetString(fn, "name"),
                        ArgsJson = Json.GetString(fn, "arguments", "{}"),
                    });
                }
            }
            return resp;
        }
    }
}
