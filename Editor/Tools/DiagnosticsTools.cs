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
            // Cap at 5 errors / 5 warnings each; this used to ship the entire
            // compile log (50+ KB repeated each turn) into history forever.
            const int displayLimit = 5;
            var errors   = IoneLogCapture.Snapshot(e => e.level == "compile-error",   startSeq, 500);
            var warnings = IoneLogCapture.Snapshot(e => e.level == "compile-warning", startSeq, 500);
            if (!compiling && errors.Count == 0) IoneLogCapture.ClearPersistedCompileErrors();
            var errOmitted  = Math.Max(0, errors.Count   - displayLimit);
            var warnOmitted = Math.Max(0, warnings.Count - displayLimit);
            if (errors.Count   > displayLimit) errors   = errors.GetRange(0, displayLimit);
            if (warnings.Count > displayLimit) warnings = warnings.GetRange(0, displayLimit);
            var sb = new StringBuilder("{");
            sb.Append("\"compiling\":").Append(compiling ? "true" : "false");
            sb.Append(",\"errors\":").Append(ListToJson(errors));
            if (errOmitted  > 0) sb.Append(",\"errorsOmitted\":").Append(errOmitted);
            sb.Append(",\"warnings\":").Append(ListToJson(warnings));
            if (warnOmitted > 0) sb.Append(",\"warningsOmitted\":").Append(warnOmitted);
            sb.Append(",\"lastSeq\":").Append(IoneLogCapture.LastSeq);
            sb.Append("}");
            return Ok(sb.ToString());
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
