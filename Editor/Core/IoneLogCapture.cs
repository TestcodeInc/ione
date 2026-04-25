using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Ione.Core
{
    public struct LogEntry
    {
        public long seq;
        public long tMs;
        public string level;
        public string message;
        public string stack;
        public string file;
        public int line;
    }

    // Ring buffer of Debug.* output + compile messages, populated from any
    // thread. Exposed to the agent via get_logs / wait_for_compile.
    [InitializeOnLoad]
    public static class IoneLogCapture
    {
        const int BufferCap = 1000;
        const string CompileErrorsFileName = "ione-compile-errors.jsonl";

        static readonly LinkedList<LogEntry> buffer = new LinkedList<LogEntry>();
        static readonly object bufferLock = new object();
        static long seq = 0;
        static volatile string compileErrorsFilePath;

        static IoneLogCapture()
        {
            try { compileErrorsFilePath = Path.Combine(IonePaths.LibraryDir, CompileErrorsFileName); }
            catch { compileErrorsFilePath = null; }

            Application.logMessageReceivedThreaded += OnUnityLog;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;

            LoadPersistedCompileErrors();
        }

        public static long LastSeq { get { lock (bufferLock) return seq; } }

        public static List<LogEntry> Snapshot(Func<LogEntry, bool> predicate, long sinceSeq, int limit)
        {
            var list = new List<LogEntry>();
            lock (bufferLock)
            {
                foreach (var e in buffer)
                {
                    if (e.seq <= sinceSeq) continue;
                    if (predicate != null && !predicate(e)) continue;
                    list.Add(e);
                }
            }
            if (list.Count > limit) list = list.GetRange(list.Count - limit, limit);
            return list;
        }

        public static void ClearPersistedCompileErrors()
        {
            var p = compileErrorsFilePath;
            if (string.IsNullOrEmpty(p)) return;
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        static void OnUnityLog(string message, string stackTrace, LogType type)
        {
            string level;
            switch (type)
            {
                case LogType.Error:     level = "error";     break;
                case LogType.Assert:    level = "error";     break;
                case LogType.Warning:   level = "warning";   break;
                case LogType.Exception: level = "exception"; break;
                default:                level = "info";      break;
            }
            Append(level, message ?? "", stackTrace ?? "", null, 0);
        }

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null) return;
            foreach (var m in messages)
            {
                var level = m.type == CompilerMessageType.Error ? "compile-error" : "compile-warning";
                Append(level, m.message ?? "", "", m.file, m.line);
                PersistCompileError(level, m.message ?? "", m.file, m.line);
            }
        }

        static void Append(string level, string message, string stack, string file, int line)
        {
            var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (bufferLock)
            {
                var e = new LogEntry
                {
                    seq = ++seq,
                    tMs = ms,
                    level = level,
                    message = message,
                    stack = stack,
                    file = file,
                    line = line,
                };
                buffer.AddLast(e);
                while (buffer.Count > BufferCap) buffer.RemoveFirst();
            }
        }

        // Compile errors fire before domain reload, which wipes the in-memory
        // buffer. Persist them so wait_for_compile after the reload still sees
        // the errors.
        static void PersistCompileError(string level, string message, string file, int line)
        {
            var p = compileErrorsFilePath;
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                var tMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var entry = "{\"t\":" + tMs + ",\"level\":" + Json.Str(level) +
                            ",\"message\":" + Json.Str(message) +
                            (string.IsNullOrEmpty(file) ? "" : ",\"file\":" + Json.Str(file)) +
                            (line > 0 ? ",\"line\":" + line : "") + "}\n";
                File.AppendAllText(p, entry);
            }
            catch { }
        }

        static void LoadPersistedCompileErrors()
        {
            var p = compileErrorsFilePath;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;
            try
            {
                var lines = File.ReadAllLines(p);
                int start = Math.Max(0, lines.Length - 500);
                for (int i = start; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var obj = Json.ParseObject(line);
                    if (obj == null) continue;
                    var level = Json.GetString(obj, "level", "compile-error");
                    var message = Json.GetString(obj, "message", "");
                    var file = Json.GetString(obj, "file");
                    var lineNum = (int)Json.GetNumber(obj, "line", 0);
                    Append(level, message, "", file, lineNum);
                }
            }
            catch { }
        }
    }
}
