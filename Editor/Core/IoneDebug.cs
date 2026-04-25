using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ione.Core
{
    // Console logging for LLM I/O. Reads happen on background threads, so
    // the toggle is cached from EditorPrefs on main-thread init and on
    // Settings save (Refresh()). The cached bool is volatile and read
    // atomically from any thread.
    [InitializeOnLoad]
    public static class IoneDebug
    {
        static volatile bool logRequestsCached;

        // Long base64 runs (screenshots, generated images) get collapsed
        // so the console stays useful. Threshold of 256 chars is well
        // above any normal token id but well below an image payload.
        static readonly Regex Base64Run = new Regex(@"[A-Za-z0-9+/=]{256,}");

        static IoneDebug() { Refresh(); }

        // Call from the main thread whenever Settings change.
        public static void Refresh()
        {
            logRequestsCached = IoneSettings.LogRequests;
        }

        public static void LogRequest(string label, string body)
        {
            if (!logRequestsCached) return;
            var redacted = Redact(body);
            Debug.Log($"[ione → {label}] {body.Length:N0} bytes\n{redacted}");
        }

        public static void LogResponse(string label, int status, string body)
        {
            if (!logRequestsCached) return;
            var redacted = Redact(body);
            Debug.Log($"[ione ← {label} {status}] {body.Length:N0} bytes\n{redacted}");
        }

        public static string Redact(string json) =>
            Base64Run.Replace(json, m => $"<base64 redacted: {m.Length:N0} chars>");
    }
}
