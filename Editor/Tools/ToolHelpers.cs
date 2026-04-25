using System;
using System.IO;
using System.Text;
using Ione.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ione.Tools
{
    public static class ToolHelpers
    {
        public static GameObject FindGameObject(ToolRequest r)
        {
            if (!string.IsNullOrEmpty(r.sceneObjectPath))
            {
                var parts = r.sceneObjectPath.Split('/');
                var root = GameObject.Find(parts[0]);
                if (root == null) return null;
                if (parts.Length == 1) return root;
                var rest = string.Join("/", parts, 1, parts.Length - 1);
                var tr = root.transform.Find(rest);
                return tr != null ? tr.gameObject : null;
            }
            if (!string.IsNullOrEmpty(r.sceneObjectName)) return GameObject.Find(r.sceneObjectName);
            if (!string.IsNullOrEmpty(r.name)) return GameObject.Find(r.name);
            return null;
        }

        public static Type FindType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = Type.GetType(name);
            if (t != null) return t;
            string[] prefixes = { "UnityEngine.", "UnityEditor.", "UnityEngine.UI.", "TMPro." };
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name);
                if (t != null) return t;
                foreach (var p in prefixes)
                {
                    t = asm.GetType(p + name);
                    if (t != null) return t;
                }
            }
            return null;
        }

        public static void MarkSceneDirty(Scene s)
        {
            if (s.IsValid()) EditorSceneManager.MarkSceneDirty(s);
        }

        // Canonicalizes an Assets-relative path and throws on anything that
        // could resolve outside the project's Assets/ tree. ToolRouter's
        // catch converts the throw into a standard tool error.
        public static string NormalizeAssetsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is empty");

            var raw = path.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(raw))
                throw new ArgumentException($"absolute paths not allowed: {path}");

            var p = raw.Trim('/');
            if (!p.StartsWith("Assets/", StringComparison.Ordinal) && p != "Assets")
                p = "Assets/" + p;

            foreach (var seg in p.Split('/'))
            {
                if (string.IsNullOrEmpty(seg))
                    throw new ArgumentException($"empty path segment: {path}");
                if (seg == ".." || seg == ".")
                    throw new ArgumentException($"'..' and '.' segments not allowed: {path}");
            }

            // Canonical check. Uses the IonePaths cache so it's safe off-main.
            var full = Path.GetFullPath(Path.Combine(IonePaths.ProjectRoot, p.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.Equals(IonePaths.DataPath, StringComparison.Ordinal) &&
                !full.StartsWith(IonePaths.DataPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException($"path escapes Assets/: {path}");

            return p;
        }

        public static void ApplyTransform(GameObject go, ToolRequest r)
        {
            if (r.position != null && r.position.Length >= 3) go.transform.localPosition    = new Vector3(r.position[0], r.position[1], r.position[2]);
            if (r.rotation != null && r.rotation.Length >= 3) go.transform.localEulerAngles = new Vector3(r.rotation[0], r.rotation[1], r.rotation[2]);
            if (r.scale    != null && r.scale.Length    >= 3) go.transform.localScale       = new Vector3(r.scale[0],    r.scale[1],    r.scale[2]);
        }

        public static bool ReparentByName(GameObject go, string parentName)
        {
            var parent = GameObject.Find(parentName);
            if (parent == null) return false;
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return true;
        }

        public static void EnsureFolderInternal(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        public static float Safe(float[] a, int i) => (a != null && i < a.Length) ? a[i] : 0f;

        public static string V3ToJson(Vector3 v) =>
            $"[{Json.Num(v.x)},{Json.Num(v.y)},{Json.Num(v.z)}]";

        public static string CapitalizeFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public static SerializedProperty FindProp(SerializedObject so, string name)
        {
            if (so == null || string.IsNullOrEmpty(name)) return null;
            var sp = so.FindProperty(name);
            if (sp != null) return sp;
            sp = so.FindProperty("m_" + CapitalizeFirst(name));
            if (sp != null) return sp;
            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    if (string.Equals(it.name, name, StringComparison.OrdinalIgnoreCase)) return it.Copy();
                    if (string.Equals(it.displayName, name, StringComparison.OrdinalIgnoreCase)) return it.Copy();
                } while (it.NextVisible(false));
            }
            return null;
        }

        public static UnityEngine.Object ResolveSerializedTarget(ToolRequest r)
        {
            if (!string.IsNullOrEmpty(r.assetPath)) return AssetDatabase.LoadMainAssetAtPath(NormalizeAssetsPath(r.assetPath));
            var go = FindGameObject(r);
            if (go == null) return null;
            if (string.IsNullOrEmpty(r.componentType)) return go;
            var type = FindType(r.componentType);
            if (type == null) return null;
            return go.GetComponent(type);
        }

        // Resolves an ObjectReference for set_field. Handles the FBX/multi-asset
        // case: LoadMainAssetAtPath on a .fbx returns the importer's GameObject,
        // which won't satisfy a Mesh slot. We inspect the property's expected
        // type (via objectReferenceTypeString) and pick the first matching
        // sub-asset. An explicit "path::SubName" suffix selects by name.
        public static UnityEngine.Object ResolveObjectReference(string valueRef, SerializedProperty sp)
        {
            if (string.IsNullOrEmpty(valueRef)) return null;

            string subName = null;
            var sepIdx = valueRef.IndexOf("::", StringComparison.Ordinal);
            if (sepIdx >= 0)
            {
                subName = valueRef.Substring(sepIdx + 2);
                valueRef = valueRef.Substring(0, sepIdx);
            }

            string assetPath = null;
            try { assetPath = NormalizeAssetsPath(valueRef); } catch { /* not a path; try scene fallback */ }

            if (assetPath != null)
            {
                if (!string.IsNullOrEmpty(subName))
                {
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                        if (o != null && o.name == subName) return o;
                    return null;
                }

                var expected = ExpectedReferenceType(sp);
                var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (main != null && (expected == null || expected.IsInstanceOfType(main)))
                    return main;

                if (expected != null)
                {
                    foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                        if (o != null && expected.IsInstanceOfType(o)) return o;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                        if (o != null && expected.IsInstanceOfType(o)) return o;
                }

                if (main != null) return main;
            }

            return GameObject.Find(valueRef);
        }

        // Returns the field's accepted Object type. SerializedProperty.type
        // is "PPtr<$Mesh>" / "PPtr<Mesh>" for object-reference fields; we
        // strip the wrapper and look the inner name up.
        public static Type ExpectedReferenceType(SerializedProperty sp)
        {
            if (sp == null) return null;
            var s = sp.type;
            if (string.IsNullOrEmpty(s)) return null;
            var lt = s.IndexOf('<'); var gt = s.LastIndexOf('>');
            if (lt < 0 || gt <= lt) return FindType(s);
            return FindType(s.Substring(lt + 1, gt - lt - 1).TrimStart('$'));
        }

        // Renders an ObjectReference for get_field. For sub-assets of a
        // container (FBX, multi-object .asset) we emit "path::SubName" so the
        // agent gets a string it can hand back to set_field unchanged.
        public static string DescribeObjectReference(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return obj.name;
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            return (main != null && main != obj) ? path + "::" + obj.name : path;
        }

        public static string Ok(string resultJson) => "{\"ok\":true,\"result\":" + resultJson + "}";
        public static string Err(string msg) => "{\"ok\":false,\"error\":" + Json.Str(msg) + "}";
    }
}
