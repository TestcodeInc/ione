using System.Collections.Generic;
using System.Text;
using Ione.Core;
using Ione.LLM;
using Ione.Tools;
using UnityEditor;

namespace Ione.UI
{
    // Serialize chat history to SessionState so it survives domain reloads.
    internal static class ChatPersistence
    {
        const string Key = "Ione.ChatHistory.v1";

        public static void Save(IReadOnlyList<ChatMessage> history)
        {
            SessionState.SetString(Key, Serialize(history));
        }

        public static List<ChatMessage> Load()
        {
            var raw = SessionState.GetString(Key, "");
            return string.IsNullOrEmpty(raw) ? new List<ChatMessage>() : Deserialize(raw);
        }

        public static void Clear() => SessionState.EraseString(Key);

        static string Serialize(IReadOnlyList<ChatMessage> history)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < history.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var m = history[i];
                sb.Append("{\"role\":").Append(Json.Str(m.Role.ToString()));
                if (m.Text != null) sb.Append(",\"text\":").Append(Json.Str(m.Text));
                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    sb.Append(",\"toolCalls\":[");
                    for (int j = 0; j < m.ToolCalls.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        var tc = m.ToolCalls[j];
                        sb.Append("{\"id\":").Append(Json.Str(tc.Id));
                        sb.Append(",\"name\":").Append(Json.Str(tc.Name));
                        sb.Append(",\"args\":").Append(Json.Str(tc.ArgsJson ?? "")).Append('}');
                    }
                    sb.Append(']');
                }
                if (m.ToolResults != null && m.ToolResults.Count > 0)
                {
                    sb.Append(",\"toolResults\":[");
                    for (int j = 0; j < m.ToolResults.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        var tr = m.ToolResults[j];
                        sb.Append("{\"id\":").Append(Json.Str(tr.ToolCallId));
                        sb.Append(",\"content\":").Append(Json.Str(tr.Content ?? ""));
                        sb.Append(",\"isError\":").Append(tr.IsError ? "true" : "false");
                        if (tr.Images != null && tr.Images.Count > 0)
                        {
                            sb.Append(",\"images\":[");
                            for (int k = 0; k < tr.Images.Count; k++)
                            {
                                if (k > 0) sb.Append(',');
                                var img = tr.Images[k];
                                sb.Append("{\"mediaType\":").Append(Json.Str(img.MediaType ?? ""));
                                sb.Append(",\"base64\":").Append(Json.Str(img.Base64 ?? "")).Append('}');
                            }
                            sb.Append(']');
                        }
                        sb.Append('}');
                    }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        static List<ChatMessage> Deserialize(string raw)
        {
            var list = new List<ChatMessage>();
            var arr = Json.ParseArray(raw);
            if (arr == null) return list;
            foreach (var item in arr)
            {
                var o = item as Dictionary<string, object>;
                if (o == null) continue;
                var msg = new ChatMessage();
                var role = Json.GetString(o, "role", "User");
                msg.Role = role == "Assistant" ? ChatRole.Assistant
                         : role == "Tool" ? ChatRole.Tool
                         : ChatRole.User;
                msg.Text = Json.GetString(o, "text");
                var tcs = Json.GetArray(o, "toolCalls");
                if (tcs != null)
                {
                    msg.ToolCalls = new List<ToolCall>();
                    foreach (var tc in tcs)
                    {
                        var tco = tc as Dictionary<string, object>;
                        if (tco == null) continue;
                        msg.ToolCalls.Add(new ToolCall
                        {
                            Id = Json.GetString(tco, "id"),
                            Name = Json.GetString(tco, "name"),
                            ArgsJson = Json.GetString(tco, "args", ""),
                        });
                    }
                }
                var trs = Json.GetArray(o, "toolResults");
                if (trs != null)
                {
                    msg.ToolResults = new List<ToolResult>();
                    foreach (var tr in trs)
                    {
                        var tro = tr as Dictionary<string, object>;
                        if (tro == null) continue;
                        var result = new ToolResult
                        {
                            ToolCallId = Json.GetString(tro, "id"),
                            Content = Json.GetString(tro, "content", ""),
                            IsError = Json.GetBool(tro, "isError"),
                        };
                        var imgs = Json.GetArray(tro, "images");
                        if (imgs != null)
                        {
                            result.Images = new List<ImageData>();
                            foreach (var im in imgs)
                            {
                                var imo = im as Dictionary<string, object>;
                                if (imo == null) continue;
                                result.Images.Add(new ImageData
                                {
                                    MediaType = Json.GetString(imo, "mediaType"),
                                    Base64 = Json.GetString(imo, "base64"),
                                });
                            }
                        }
                        msg.ToolResults.Add(result);
                    }
                }
                list.Add(msg);
            }
            return list;
        }
    }
}
