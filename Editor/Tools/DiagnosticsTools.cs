using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ione.Core;
using UnityEngine;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // Read-only introspection: log tail, compile-wait, folder listing.
    // Run off the main thread so a busy editor doesn't starve them.
    public static class DiagnosticsTools
    {
        public static string GetLogs(ToolRequest r)
        {
            var filter = string.IsNullOrEmpty(r.logLevel) ? "error+" : r.logLevel;
            var limit = r.logLimit > 0 ? Math.Min(r.logLimit, 500) : 100;
            var entries = IoneLogCapture.Snapshot(e => Matches(e.level, filter), r.logSinceSeq, limit);
            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = entries[i];
                sb.Append("{");
                sb.Append("\"seq\":").Append(e.seq);
                sb.Append(",\"t\":").Append(e.tMs);
                sb.Append(",\"level\":").Append(Json.Str(e.level));
                sb.Append(",\"message\":").Append(Json.Str(e.message));
                if (!string.IsNullOrEmpty(e.file)) sb.Append(",\"file\":").Append(Json.Str(e.file));
                if (e.line > 0) sb.Append(",\"line\":").Append(e.line);
                if (!string.IsNullOrEmpty(e.stack)) sb.Append(",\"stack\":").Append(Json.Str(TrimStack(e.stack)));
                sb.Append("}");
            }
            sb.Append("]");
            return Ok($"{{\"entries\":{sb},\"lastSeq\":{IoneLogCapture.LastSeq},\"filter\":{Json.Str(filter)}}}");
        }

        public static async Task<string> WaitForCompileAsync(ToolRequest r)
        {
            var waitMs = r.logWaitMs > 0 ? Math.Min(r.logWaitMs, 25_000) : 15_000;
            var startSeq = r.logSinceSeq;
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            await Task.Delay(200);
            while (MainThreadDispatcher.IsCompilingCached && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            var compiling = MainThreadDispatcher.IsCompilingCached;
            // Dedupe errors by message text; cascading errors (e.g. a missing
            // namespace causing 50 identical "CS0246" hits) collapse to one
            // entry with a list of locations. Warnings are excluded entirely
            // since the model rarely needs them and they bloat the response.
            var errors = IoneLogCapture.Snapshot(e => e.level == "compile-error", startSeq, 500);
            if (!compiling && errors.Count == 0) IoneLogCapture.ClearPersistedCompileErrors();
            var grouped = GroupByMessage(errors);
            var sb = new StringBuilder("{");
            sb.Append("\"compiling\":").Append(compiling ? "true" : "false");
            sb.Append(",\"uniqueErrorCount\":").Append(grouped.Count);
            sb.Append(",\"totalErrorCount\":").Append(errors.Count);
            sb.Append(",\"errors\":").Append(GroupedToJson(grouped));
            sb.Append(",\"lastSeq\":").Append(IoneLogCapture.LastSeq);
            sb.Append("}");
            return Ok(sb.ToString());
        }

        struct ErrorGroup
        {
            public string Message;
            public List<(string file, int line)> Locations;
        }

        static List<ErrorGroup> GroupByMessage(List<LogEntry> errors)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var groups = new List<ErrorGroup>();
            foreach (var e in errors)
            {
                var msg = e.message ?? "";
                if (!index.TryGetValue(msg, out var i))
                {
                    i = groups.Count;
                    index[msg] = i;
                    groups.Add(new ErrorGroup { Message = msg, Locations = new List<(string, int)>() });
                }
                groups[i].Locations.Add((e.file, e.line));
            }
            return groups;
        }

        static string GroupedToJson(List<ErrorGroup> groups)
        {
            const int locationCap = 20;
            var sb = new StringBuilder("[");
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var g = groups[i];
                sb.Append("{\"message\":").Append(Json.Str(g.Message));
                sb.Append(",\"count\":").Append(g.Locations.Count);
                var emit = Math.Min(g.Locations.Count, locationCap);
                sb.Append(",\"locations\":[");
                for (int k = 0; k < emit; k++)
                {
                    if (k > 0) sb.Append(",");
                    var (file, line) = g.Locations[k];
                    sb.Append("{");
                    if (!string.IsNullOrEmpty(file)) sb.Append("\"file\":").Append(Json.Str(file));
                    if (line > 0)
                    {
                        if (!string.IsNullOrEmpty(file)) sb.Append(",");
                        sb.Append("\"line\":").Append(line);
                    }
                    sb.Append("}");
                }
                sb.Append("]");
                if (g.Locations.Count > locationCap)
                    sb.Append(",\"locationsOmitted\":").Append(g.Locations.Count - locationCap);
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // Filesystem-backed listing. AssetDatabase.FindAssets can block for
        // minutes on pending imports; this is instant and thread-safe.
        public static string ListFolder(string path)
        {
            var norm = string.IsNullOrEmpty(path) ? "Assets" : NormalizeAssetsPath(path);
            var full = Path.Combine(IonePaths.ProjectRoot, norm.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full)) return Err($"folder not found: {norm}");
            var items = new SortedSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(full))
                {
                    var name = Path.GetFileName(entry);
                    if (name.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    items.Add(norm + "/" + name);
                }
            }
            catch (Exception e) { return Err($"enumerate failed: {e.Message}"); }
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var p in items)
            {
                if (!first) sb.Append(",");
                sb.Append(Json.Str(p));
                first = false;
            }
            sb.Append("]");
            return Ok($"{{\"path\":{Json.Str(norm)},\"items\":{sb}}}");
        }

        static bool Matches(string level, string filter)
        {
            if (string.IsNullOrEmpty(filter) || filter == "any") return true;
            if (filter == "error+") return level == "error" || level == "exception" || level == "compile-error";
            if (filter == "warning+") return level != "info";
            return string.Equals(level, filter, StringComparison.OrdinalIgnoreCase);
        }

        static string TrimStack(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var lines = s.Split('\n');
            if (lines.Length <= 8) return s;
            return string.Join("\n", lines, 0, 8) + "\n…";
        }

        static string ListToJson(List<LogEntry> list)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = list[i];
                sb.Append("{");
                sb.Append("\"seq\":").Append(e.seq);
                sb.Append(",\"level\":").Append(Json.Str(e.level));
                sb.Append(",\"message\":").Append(Json.Str(e.message));
                if (!string.IsNullOrEmpty(e.file)) sb.Append(",\"file\":").Append(Json.Str(e.file));
                if (e.line > 0) sb.Append(",\"line\":").Append(e.line);
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
