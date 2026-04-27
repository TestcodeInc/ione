using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ione.Tools;
using UnityEditor;
using UnityEngine;

namespace Ione.Core
{
    // Loopback HTTP server that exposes ToolRouter to local processes
    // (Claude Code, scripts, etc). Bound to 127.0.0.1 only; never reachable
    // from the network. Off by default - opt in via Settings.
    //
    // Endpoints:
    //   GET  /          → version + status
    //   GET  /tools     → JSON array of tool names + descriptions + schemas
    //   POST /tool      → body: {"name":"<tool>","args":{...}}; response: tool's JSON envelope
    [InitializeOnLoad]
    public static class IoneHttpBridge
    {
        static HttpListener listener;
        static CancellationTokenSource cts;
        static Thread acceptThread;
        static int boundPort;

        static IoneHttpBridge()
        {
            // Domain reload kills HttpListener silently; rebind on load.
            EditorApplication.delayCall += () => { if (IoneSettings.HttpBridgeEnabled) Start(); };
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public static bool IsRunning => listener != null && listener.IsListening;
        public static int BoundPort => boundPort;

        public static void Start()
        {
            if (IsRunning) return;
            var port = IoneSettings.HttpBridgePort;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                boundPort = port;
                cts = new CancellationTokenSource();
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "ione-http" };
                acceptThread.Start();
                // Prime the token so it gets generated + logged at startup
                // rather than on the first authenticated request.
                IoneBridgeToken.Get();
                Debug.Log($"[ione] HTTP bridge listening on http://127.0.0.1:{port} (token at {IoneBridgeToken.TokenPath})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ione] HTTP bridge failed to start on port {port}: {e.Message}");
                listener = null;
            }
        }

        public static void Stop()
        {
            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
            listener = null;
            cts = null;
            acceptThread = null;
        }

        static void AcceptLoop()
        {
            var l = listener;
            var token = cts.Token;
            while (!token.IsCancellationRequested && l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = l.GetContext(); }
                catch { break; }
                _ = HandleAsync(ctx);
            }
        }

        static async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                var method = ctx.Request.HttpMethod;

                // Block browser-originated requests (CSRF defense). Browsers
                // always set Origin on cross-origin POST/fetch; CLI clients
                // (curl, Claude Code) do not. We never expect to be called
                // from a webpage, so any Origin is rejected outright. This
                // also kills the CORS preflight (OPTIONS) without needing
                // to handle it explicitly.
                var origin = ctx.Request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin))
                {
                    await WriteJson(ctx, 403, "{\"ok\":false,\"error\":\"cross-origin requests are not allowed\"}");
                    return;
                }

                // All endpoints require the bearer token. Persisted at
                // ~/.ione/bridge-token (0600) — local clients read it from
                // there and send it as Authorization: Bearer. Even the
                // version probe is gated, so we never disclose plugin
                // version to unauthenticated callers.
                if (!IsAuthorized(ctx))
                {
                    await WriteJson(ctx, 401, "{\"ok\":false,\"error\":\"missing or invalid bearer token; read ~/.ione/bridge-token\"}");
                    return;
                }

                if (method == "GET" && path == "/")
                {
                    await WriteJson(ctx, 200, $"{{\"name\":\"ione\",\"version\":\"{IoneBootstrap.Version}\"}}");
                    return;
                }
                if (method == "GET" && path == "/tools")
                {
                    await WriteJson(ctx, 200, BuildToolsJson());
                    return;
                }
                if (method == "POST" && path == "/tool")
                {
                    // Require application/json. This forces browsers into a
                    // CORS preflight (which we'd reject via Origin), closing
                    // the "simple request" CSRF path that text/plain or
                    // form-encoded bodies would otherwise allow.
                    var contentType = ctx.Request.ContentType ?? "";
                    var semi = contentType.IndexOf(';');
                    var mediaType = (semi >= 0 ? contentType.Substring(0, semi) : contentType).Trim().ToLowerInvariant();
                    if (mediaType != "application/json")
                    {
                        await WriteJson(ctx, 415, "{\"ok\":false,\"error\":\"Content-Type must be application/json\"}");
                        return;
                    }

                    string body;
                    using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                        body = await sr.ReadToEndAsync().ConfigureAwait(false);

                    var obj = Json.ParseObject(body);
                    if (obj == null) { await WriteJson(ctx, 400, "{\"ok\":false,\"error\":\"bad json\"}"); return; }
                    var name = Json.GetString(obj, "name");
                    if (string.IsNullOrEmpty(name)) { await WriteJson(ctx, 400, "{\"ok\":false,\"error\":\"missing 'name'\"}"); return; }
                    var args = Json.GetObject(obj, "args");
                    var argsJson = args != null ? Json.Serialize(args) : "{}";

                    var output = await ToolRouter.InvokeAsync(name, argsJson).ConfigureAwait(false);
                    // Tool errors stay 200; only transport errors are 4xx/5xx.
                    var responseBody = MergeImagesIntoEnvelope(output);
                    await WriteJson(ctx, 200, responseBody);
                    return;
                }

                await WriteJson(ctx, 404, "{\"ok\":false,\"error\":\"not found\"}");
            }
            catch (Exception e)
            {
                try { await WriteJson(ctx, 500, "{\"ok\":false,\"error\":" + Json.Str(e.Message) + "}"); }
                catch { }
            }
        }

        static bool IsAuthorized(HttpListenerContext ctx)
        {
            var header = ctx.Request.Headers["Authorization"];
            if (string.IsNullOrEmpty(header)) return false;
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var presented = header.Substring(prefix.Length).Trim();
            var expected = IoneBridgeToken.Get();
            return ConstantTimeEquals(presented, expected);
        }

        // Avoid leaking token length / prefix via timing.
        static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static async Task WriteJson(HttpListenerContext ctx, int status, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.OutputStream.Close();
            ctx.Response.Close();
        }

        // Image-returning tools (e.g. capture_game_view) put PNG bytes on
        // ToolOutput.Images, separate from the text envelope. Inject them
        // into the response as an "images" array so HTTP callers see them.
        static string MergeImagesIntoEnvelope(Tools.ToolOutput output)
        {
            var content = output?.Content ?? "{\"ok\":false,\"error\":\"no output\"}";
            if (output == null || output.Images == null || output.Images.Count == 0)
                return content;
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < output.Images.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var img = output.Images[i];
                sb.Append("{\"mediaType\":").Append(Json.Str(img.MediaType ?? "image/png"));
                sb.Append(",\"base64\":").Append(Json.Str(img.Base64 ?? ""));
                sb.Append('}');
            }
            sb.Append("]");
            var imagesArr = sb.ToString();
            // Splice "images":<arr> in before the envelope's closing brace.
            // Cheap and avoids reparsing the whole tool envelope.
            var trimmed = content.TrimEnd();
            if (!trimmed.EndsWith("}")) return content;
            var prefix = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            var sep = prefix.EndsWith("{") ? "" : ",";
            return prefix + sep + "\"images\":" + imagesArr + "}";
        }

        static string BuildToolsJson()
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var t in ToolSchemas.All)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":").Append(Json.Str(t.Name));
                sb.Append(",\"description\":").Append(Json.Str(t.Description));
                sb.Append(",\"parameters\":").Append(t.ParametersJson);
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
