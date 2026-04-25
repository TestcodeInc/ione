using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Ione.Core;
using UnityEditor;
using UnityEngine;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // Generates PNGs via OpenAI's image endpoint and imports them as Sprites
    // under Assets/. Called by the agent when a task needs art.
    public static class ImageGenerationTools
    {
        const string Endpoint = "https://api.openai.com/v1/images/generations";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        public static async Task<ToolOutput> GenerateImageAsync(ToolRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.prompt))
                return ToolOutput.FromText(Err("prompt required"), true);
            if (string.IsNullOrWhiteSpace(r.path))
                return ToolOutput.FromText(Err("path required (e.g. 'Assets/Sprites/player.png')"), true);

            // EditorPrefs is main-thread only.
            var (apiKey, model) = MainThreadDispatcher.RunOnMain(
                () => (IoneSettings.OpenAIKey, IoneSettings.ImageModel));
            if (string.IsNullOrEmpty(apiKey))
                return ToolOutput.FromText(Err("OpenAI API key required for generate_image. Set it in Tools → ione → Settings."), true);
            if (string.IsNullOrEmpty(model))
                model = IoneSettings.DefaultImageModel;

            var size = NormalizeSize(r.imageSize);
            // Transparent background is forced on; sprites almost always
            // want alpha and JsonUtility can't tell absent-bool from false.
            var body = new StringBuilder();
            body.Append('{');
            body.Append("\"model\":").Append(Json.Str(model));
            body.Append(",\"prompt\":").Append(Json.Str(r.prompt));
            body.Append(",\"n\":1");
            body.Append(",\"size\":").Append(Json.Str(size));
            body.Append(",\"background\":\"transparent\"");
            body.Append('}');

            string b64;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    var resp = await http.SendAsync(req).ConfigureAwait(false);
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        return ToolOutput.FromText(Err($"image API {(int)resp.StatusCode}: {text}"), true);
                    b64 = ExtractB64(text);
                    if (string.IsNullOrEmpty(b64))
                        return ToolOutput.FromText(Err($"image API returned no b64_json: {text}"), true);
                }
            }
            catch (Exception e)
            {
                return ToolOutput.FromText(Err("image API call failed: " + e.Message), true);
            }

            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch (Exception e) { return ToolOutput.FromText(Err("bad base64 from image API: " + e.Message), true); }

            // AssetDatabase is main-thread only.
            return MainThreadDispatcher.RunOnMain(() => WriteAsSprite(r.path, bytes, size, r.prompt, b64));
        }

        static ToolOutput WriteAsSprite(string rawPath, byte[] bytes, string size, string prompt, string b64)
        {
            var norm = NormalizeAssetsPath(rawPath);
            if (!(norm.EndsWith(".png") || norm.EndsWith(".jpg") || norm.EndsWith(".jpeg")))
                norm += ".png";
            var parent = Path.GetDirectoryName(norm)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            var fullPath = Path.Combine(IonePaths.ProjectRoot, norm);
            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(norm, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(norm) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            var content = Ok($"{{\"path\":{Json.Str(norm)},\"size\":{Json.Str(size)},\"bytes\":{bytes.Length},\"prompt\":{Json.Str(prompt)}}}");
            return new ToolOutput
            {
                Content = content,
                IsError = false,
                Images = new List<ImageData> { new ImageData { MediaType = "image/png", Base64 = b64 } },
            };
        }

        static string NormalizeSize(string raw)
        {
            var allowed = new[] { "1024x1024", "1536x1024", "1024x1536", "auto" };
            if (string.IsNullOrEmpty(raw)) return "1024x1024";
            foreach (var a in allowed) if (string.Equals(a, raw, StringComparison.OrdinalIgnoreCase)) return a;
            return "1024x1024"; // silently round to supported default
        }

        static string ExtractB64(string responseJson)
        {
            var root = Json.ParseObject(responseJson);
            var data = Json.GetArray(root, "data");
            if (data == null || data.Count == 0) return null;
            var first = data[0] as Dictionary<string, object>;
            return Json.GetString(first, "b64_json");
        }
    }
}
