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
                Debug.Log($"[ione] HTTP bridge listening on http://127.0.0.1:{port}");
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
