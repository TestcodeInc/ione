using System.Collections.Generic;
using Ione.Tools;

namespace Ione.LLM
{
    // Provider-agnostic chat types. Each provider translates these to its
    // own wire format.

    public enum ChatRole { User, Assistant, Tool }

    public class ToolCall
    {
        public string Id;       // provider-assigned id to correlate the result
        public string Name;
        public string ArgsJson; // JSON-serialized arguments
    }

    public class ToolResult
    {
        public string ToolCallId;
        public string Content;  // the tool's JSON envelope
        public bool IsError;
        public List<ImageData> Images; // optional - screenshots etc. sent as real image content
    }

    public class ChatMessage
    {
        public ChatRole Role;
        public string Text;                   // assistant/user text, may be empty
        public List<ToolCall> ToolCalls;      // assistant → requested tool calls
        public List<ToolResult> ToolResults;  // tool → results to feed back

        public static ChatMessage User(string text) =>
            new ChatMessage { Role = ChatRole.User, Text = text };

        public static ChatMessage Assistant(string text, List<ToolCall> toolCalls) =>
            new ChatMessage { Role = ChatRole.Assistant, Text = text, ToolCalls = toolCalls };

        public static ChatMessage ToolResults_(List<ToolResult> results) =>
            new ChatMessage { Role = ChatRole.Tool, ToolResults = results };
    }

    public class LLMResponse
    {
        public string Text;
        public List<ToolCall> ToolCalls;
        public bool RequestsTools => ToolCalls != null && ToolCalls.Count > 0;
    }
}
