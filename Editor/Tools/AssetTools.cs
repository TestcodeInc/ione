using System;
using System.IO;
using System.Linq;
using Ione.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // Assets: folders, materials, scripts, scenes, build settings.
    public static class AssetTools
    {
        public static string EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return Err("path required");
            var norm = NormalizeAssetsPath(path);
            if (AssetDatabase.IsValidFolder(norm)) return Ok($"{{\"path\":{Json.Str(norm)},\"created\":false}}");
            EnsureFolderInternal(norm);
            AssetDatabase.Refresh();
            return Ok($"{{\"path\":{Json.Str(norm)},\"created\":true}}");
        }

        public static string RenameAsset(string path, string newName)
        {
            if (string.IsNullOrEmpty(path)) return Err("path required");
            if (string.IsNullOrEmpty(newName)) return Err("newName required");
            var withoutExt = Path.GetFileNameWithoutExtension(newName);
            var err = AssetDatabase.RenameAsset(path, withoutExt);
            if (!string.IsNullOrEmpty(err)) return Err($"rename failed: {err}");
            AssetDatabase.Refresh();
            var newPath = (Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "") + "/" + withoutExt + Path.GetExtension(path);
            return Ok($"{{\"newPath\":{Json.Str(newPath)}}}");
        }

        public static string MoveAsset(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            if (string.IsNullOrEmpty(r.newPath)) return Err("newPath required");
            var src = NormalizeAssetsPath(r.path);
            var dst = NormalizeAssetsPath(r.newPath);
            var parent = Path.GetDirectoryName(dst).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            var err = AssetDatabase.MoveAsset(src, dst);
            if (!string.IsNullOrEmpty(err)) return Err($"move failed: {err}");
            return Ok($"{{\"newPath\":{Json.Str(dst)}}}");
        }

        public static string DeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return Err("path required");
            if (!IoneSettings.AllowAssetDeletion)
                return Err("delete_asset is disabled by the user in Tools → ione → Settings → Safety. Ask them to enable 'Asset deletion', or use move_asset instead.");
            return AssetDatabase.DeleteAsset(NormalizeAssetsPath(path)) ? Ok("{}") : Err($"delete failed: {path}");
        }

        // Read a text asset under Assets/. Capped at 32 KB; over-cap files
        // return a head snippet with truncated:true. .meta files are import
        // settings (often huge YAML for binary assets like .psd) and are
        // refused outright since the agent has no business reading them.
        public static string ReadAsset(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            var norm = NormalizeAssetsPath(r.path);
            if (norm.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                return Err("refusing to read .meta file (import settings are not useful context). Read the underlying asset instead.");
            var full = Path.Combine(IonePaths.ProjectRoot, norm.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return Err($"file not found: {norm}");
            const int cap = 32 * 1024;
            var info = new FileInfo(full);
            if (info.Length > cap)
            {
                using (var fs = File.OpenRead(full))
                using (var sr = new StreamReader(fs))
                {
                    var buf = new char[cap];
                    var n = sr.Read(buf, 0, cap);
                    var head = new string(buf, 0, n);
                    return Ok($"{{\"path\":{Json.Str(norm)},\"bytes\":{info.Length},\"truncated\":true,\"head\":{Json.Str(head)}}}");
                }
            }
            var text = File.ReadAllText(full);
            return Ok($"{{\"path\":{Json.Str(norm)},\"bytes\":{info.Length},\"content\":{Json.Str(text)}}}");
        }

        public static string CreateMaterial(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            var norm = NormalizeAssetsPath(r.path);
            if (!norm.EndsWith(".mat")) norm += ".mat";
            var shaderName = string.IsNullOrEmpty(r.shaderName) ? "Standard" : r.shaderName;
            var shader = Shader.Find(shaderName);
            if (shader == null) return Err($"shader not found: {shaderName}");
            var mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(norm) };
            var parent = Path.GetDirectoryName(norm).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            AssetDatabase.CreateAsset(mat, norm);
            AssetDatabase.Refresh();
            return Ok($"{{\"path\":{Json.Str(norm)}}}");
        }

        public static string AssignMaterial(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            if (string.IsNullOrEmpty(r.assetPath)) return Err("assetPath required");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(NormalizeAssetsPath(r.assetPath));
            if (mat == null) return Err($"material not found: {r.assetPath}");
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return Err("no Renderer on scene object");
            Undo.RecordObject(rend, "ione: assign material");
            rend.sharedMaterial = mat;
            MarkSceneDirty(go.scene);
            return Ok("{}");
        }

        public static string SetMaterialProperty(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.assetPath)) return Err("assetPath required");
            if (string.IsNullOrEmpty(r.propertyName)) return Err("propertyName required");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(NormalizeAssetsPath(r.assetPath));
            if (mat == null) return Err($"material not found: {r.assetPath}");
            Undo.RecordObject(mat, "ione: set material property");
            if (r.valueArray != null && r.valueArray.Length >= 3)
            {
                mat.SetColor(r.propertyName, new Color(Safe(r.valueArray, 0), Safe(r.valueArray, 1), Safe(r.valueArray, 2), r.valueArray.Length >= 4 ? r.valueArray[3] : 1f));
            }
            else if (!string.IsNullOrEmpty(r.valueRef))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(NormalizeAssetsPath(r.valueRef));
                if (tex == null) return Err($"texture not found: {r.valueRef}");
                mat.SetTexture(r.propertyName, tex);
            }
            else
            {
                mat.SetFloat(r.propertyName, r.valueNumber);
            }
            EditorUtility.SetDirty(mat);
            return Ok("{}");
        }

        public static string CreateScript(ToolRequest r)
        {
            if (!IoneSettings.AllowScriptWrites)
                return Err("create_script is disabled by the user in Tools → ione → Settings → Safety. Ask them to enable 'Script writes'.");
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            var norm = NormalizeAssetsPath(r.path);
            if (!norm.EndsWith(".cs")) norm += ".cs";
            var parent = Path.GetDirectoryName(norm).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            File.WriteAllText(Path.Combine(IonePaths.ProjectRoot, norm), r.content ?? "");
            // Defer ImportAsset so the tool result flushes before the reload.
            var pending = norm;
            EditorApplication.delayCall += () =>
            {
                try { AssetDatabase.ImportAsset(pending); }
                catch (Exception e) { Debug.LogError($"[ione] deferred ImportAsset failed: {e.Message}"); }
            };
            return Ok($"{{\"path\":{Json.Str(norm)}}}");
        }

        public static string SaveProject()
        {
            AssetDatabase.SaveAssets();
            return Ok("{}");
        }

        public static string NewScene(ToolRequest r)
        {
            if (!IoneSettings.AllowSceneSwitching)
                return Err("new_scene is disabled by the user in Tools → ione → Settings → Safety (scene switching can discard unsaved edits). Ask them to enable 'Scene switching'.");
            var setup = string.Equals(r.sceneName, "empty", StringComparison.OrdinalIgnoreCase)
                ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            var scene = EditorSceneManager.NewScene(setup, NewSceneMode.Single);
            return Ok($"{{\"name\":{Json.Str(scene.name)},\"path\":{Json.Str(scene.path)}}}");
        }

        public static string OpenScene(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            // Only gate single-mode opens; additive is non-destructive.
            if (!r.additive && !IoneSettings.AllowSceneSwitching)
                return Err("open_scene (single mode) is disabled by the user in Tools → ione → Settings → Safety. Ask them to enable 'Scene switching', or pass additive:true.");
            var scene = EditorSceneManager.OpenScene(NormalizeAssetsPath(r.path),
                r.additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
            return Ok($"{{\"name\":{Json.Str(scene.name)},\"path\":{Json.Str(scene.path)}}}");
        }

        // Saves the active scene. `path` is save-as (works for unsaved
        // scenes too). Without `path`, the active scene must already have one.
        public static string SaveScene(ToolRequest r)
        {
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid()) return Err("no active scene");

            bool ok;
            string savedAs;
            if (!string.IsNullOrEmpty(r.path))
            {
                var norm = NormalizeAssetsPath(r.path);
                var parent = Path.GetDirectoryName(norm)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
                ok = EditorSceneManager.SaveScene(active, norm);
                savedAs = norm;
            }
            else if (string.IsNullOrEmpty(active.path))
            {
                return Err("active scene has no path yet - pass 'path' to save it as a new file");
            }
            else
            {
                ok = EditorSceneManager.SaveScene(active);
                savedAs = active.path;
            }
            return ok ? Ok($"{{\"path\":{Json.Str(savedAs)}}}") : Err("save failed");
        }

        public static string AddSceneToBuild(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            var norm = NormalizeAssetsPath(r.path);
            var list = EditorBuildSettings.scenes.ToList();
            var idx = list.FindIndex(s => s.path == norm);
            if (idx >= 0) return Ok($"{{\"added\":false,\"index\":{idx}}}");
            list.Add(new EditorBuildSettingsScene(norm, true));
            EditorBuildSettings.scenes = list.ToArray();
            return Ok($"{{\"added\":true,\"index\":{list.Count - 1}}}");
        }
    }
}
