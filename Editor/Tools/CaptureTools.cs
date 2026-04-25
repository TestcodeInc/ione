using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ione.Core;
using UnityEditor;
using UnityEngine;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // Screenshot capture + PNG import as Sprite assets.
    public static class CaptureTools
    {
        // PNG rides as an ImageData attachment; the text content holds
        // only metadata so base64 never bloats the LLM's text context.
        public static ToolOutput CaptureGameView(ToolRequest r)
        {
            int w = r.captureWidth  > 0 ? Math.Min(r.captureWidth, 2048)  : 1024;
            int h = r.captureHeight > 0 ? Math.Min(r.captureHeight, 2048) :  576;
            var source = string.IsNullOrEmpty(r.captureSource) ? "game" : r.captureSource.ToLowerInvariant();
            Camera cam = null;
            string cameraNote = null;
            if (source == "scene")
            {
                var sv = SceneView.lastActiveSceneView;
                cam = sv != null ? sv.camera : null;
            }
            else
            {
                cam = Camera.main;
                if (cam == null)
                {
                    var cams = UnityEngine.Object.FindObjectsByType<Camera>();
                    if (cams != null && cams.Length > 0)
                    {
                        cam = cams[0];
                        cameraNote = $"Camera.main is null; captured from first active Camera '{cam.name}'.";
                    }
                }
            }
            if (cam == null) return ToolOutput.FromText(Err("no camera available for capture (source=" + source + ")"), true);

            RenderTexture rt = null;
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                rt = RenderTexture.GetTemporary(w, h, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                var b64 = Convert.ToBase64String(png);
                var resultSb = new StringBuilder("{");
                resultSb.Append("\"width\":").Append(w);
                resultSb.Append(",\"height\":").Append(h);
                resultSb.Append(",\"source\":").Append(Json.Str(source));
                resultSb.Append(",\"camera\":").Append(Json.Str(cam.name));
                resultSb.Append(",\"mediaType\":\"image/png\"");
                if (!string.IsNullOrEmpty(cameraNote))
                    resultSb.Append(",\"note\":").Append(Json.Str(cameraNote));
                resultSb.Append('}');
                return new ToolOutput
                {
                    Content = Ok(resultSb.ToString()),
                    IsError = false,
                    Images = new List<ImageData> { new ImageData { MediaType = "image/png", Base64 = b64 } },
                };
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        public static string SaveImageAsset(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path) && string.IsNullOrEmpty(r.assetPath))
                return Err("path (or assetPath) required");
            if (string.IsNullOrEmpty(r.imageBase64)) return Err("imageBase64 required");
            var norm = NormalizeAssetsPath(!string.IsNullOrEmpty(r.path) ? r.path : r.assetPath);
            if (!(norm.EndsWith(".png") || norm.EndsWith(".jpg") || norm.EndsWith(".jpeg")))
                norm += ".png";
            byte[] bytes;
            try { bytes = Convert.FromBase64String(r.imageBase64); }
            catch (Exception e) { return Err("bad base64: " + e.Message); }
            var parent = Path.GetDirectoryName(norm).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            var fullPath = Path.Combine(IonePaths.ProjectRoot, norm);
            try { File.WriteAllBytes(fullPath, bytes); }
            catch (Exception e) { return Err("write failed: " + e.Message); }
            AssetDatabase.ImportAsset(norm, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(norm) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            return Ok($"{{\"path\":{Json.Str(norm)},\"bytes\":{bytes.Length}}}");
        }
    }
}
