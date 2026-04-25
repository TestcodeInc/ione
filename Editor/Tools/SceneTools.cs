using System;
using System.Text;
using Ione.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // Scene-graph authoring plus tags, layers, selection, play mode.
    public static class SceneTools
    {
        public static string CreatePrimitive(ToolRequest r)
        {
            var type = string.IsNullOrEmpty(r.primitive) ? "Cube" : r.primitive;
            if (!Enum.TryParse<PrimitiveType>(type, true, out var ptype))
                return Err($"unknown primitive: {type}");
            var go = GameObject.CreatePrimitive(ptype);
            if (!string.IsNullOrEmpty(r.name)) go.name = r.name;
            ApplyTransform(go, r);
            if (!string.IsNullOrEmpty(r.parentName) && !ReparentByName(go, r.parentName))
            {
                UnityEngine.Object.DestroyImmediate(go);
                return Err($"parent not found: {r.parentName}");
            }
            Undo.RegisterCreatedObjectUndo(go, $"ione: create {ptype}");
            MarkSceneDirty(go.scene);
            return Ok($"{{\"name\":{Json.Str(go.name)}}}");
        }

        public static string CreateEmpty(ToolRequest r)
        {
            var go = new GameObject(string.IsNullOrEmpty(r.name) ? "GameObject" : r.name);
            ApplyTransform(go, r);
            if (!string.IsNullOrEmpty(r.parentName) && !ReparentByName(go, r.parentName))
            {
                UnityEngine.Object.DestroyImmediate(go);
                return Err($"parent not found: {r.parentName}");
            }
            Undo.RegisterCreatedObjectUndo(go, "ione: create empty");
            MarkSceneDirty(go.scene);
            return Ok($"{{\"name\":{Json.Str(go.name)}}}");
        }

        public static string InstantiatePrefab(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.prefabPath)) return Err("prefabPath required");
            var norm = NormalizeAssetsPath(r.prefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(norm);
            if (prefab == null) return Err($"prefab not found: {norm}");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return Err("InstantiatePrefab returned null");
            if (!string.IsNullOrEmpty(r.name)) go.name = r.name;
            ApplyTransform(go, r);
            if (!string.IsNullOrEmpty(r.parentName) && !ReparentByName(go, r.parentName))
            {
                UnityEngine.Object.DestroyImmediate(go);
                return Err($"parent not found: {r.parentName}");
            }
            if (r.unpack) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(go, "ione: instantiate prefab");
            MarkSceneDirty(go.scene);
            return Ok($"{{\"name\":{Json.Str(go.name)}}}");
        }

        public static string SetTransform(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err($"scene object not found: {r.sceneObjectName ?? r.sceneObjectPath}");
            Undo.RecordObject(go.transform, "ione: set transform");
            if (r.position != null && r.position.Length >= 3)
            {
                var v = new Vector3(r.position[0], r.position[1], r.position[2]);
                if (r.worldSpace) go.transform.position = v; else go.transform.localPosition = v;
            }
            if (r.rotation != null && r.rotation.Length >= 3)
            {
                var v = new Vector3(r.rotation[0], r.rotation[1], r.rotation[2]);
                if (r.worldSpace) go.transform.eulerAngles = v; else go.transform.localEulerAngles = v;
            }
            if (r.scale != null && r.scale.Length >= 3) go.transform.localScale = new Vector3(r.scale[0], r.scale[1], r.scale[2]);
            MarkSceneDirty(go.scene);
            return Ok($"{{\"position\":{V3ToJson(go.transform.position)},\"localPosition\":{V3ToJson(go.transform.localPosition)}}}");
        }

        public static string SetParent(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            Transform parent = null;
            if (!string.IsNullOrEmpty(r.parentName))
            {
                var p = GameObject.Find(r.parentName);
                if (p == null) return Err($"parent not found: {r.parentName}");
                parent = p.transform;
            }
            Undo.SetTransformParent(go.transform, parent, "ione: set parent");
            MarkSceneDirty(go.scene);
            return Ok($"{{\"parent\":{Json.Str(parent != null ? parent.name : "")}}}");
        }

        public static string FindSceneObject(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("not found");
            var comps = go.GetComponents<Component>();
            var compsSb = new StringBuilder("[");
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) compsSb.Append(",");
                compsSb.Append(Json.Str(comps[i] != null ? comps[i].GetType().Name : "<missing>"));
            }
            compsSb.Append("]");
            var childrenSb = new StringBuilder("[");
            for (int i = 0; i < go.transform.childCount; i++)
            {
                if (i > 0) childrenSb.Append(",");
                childrenSb.Append(Json.Str(go.transform.GetChild(i).name));
            }
            childrenSb.Append("]");
            return Ok(
                $"{{\"name\":{Json.Str(go.name)}," +
                $"\"position\":{V3ToJson(go.transform.position)},\"localPosition\":{V3ToJson(go.transform.localPosition)}," +
                $"\"rotation\":{V3ToJson(go.transform.eulerAngles)}," +
                $"\"components\":{compsSb},\"children\":{childrenSb},\"tag\":{Json.Str(go.tag)},\"layer\":{go.layer},\"active\":{(go.activeSelf ? "true" : "false")}}}"
            );
        }

        // World-space AABB from active Renderers. Encapsulates self + all
        // descendants by default so a parent like Door_S returns the union
        // of every panel/frame piece. Lets the agent fit geometry to
        // existing geometry without eyeballing screenshots.
        public static string GetRendererBounds(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");

            var rends = r.selfOnly
                ? go.GetComponents<Renderer>()
                : go.GetComponentsInChildren<Renderer>(includeInactive: true);

            Bounds? combined = null;
            int hits = 0;
            foreach (var rend in rends)
            {
                if (rend == null || !rend.enabled) continue;
                hits++;
                if (combined.HasValue) { var b = combined.Value; b.Encapsulate(rend.bounds); combined = b; }
                else combined = rend.bounds;
            }

            if (!combined.HasValue) return Err($"no enabled Renderers under {go.name}");
            var bb = combined.Value;
            return Ok(
                $"{{\"name\":{Json.Str(go.name)}," +
                $"\"rendererCount\":{hits}," +
                $"\"center\":{V3ToJson(bb.center)}," +
                $"\"extents\":{V3ToJson(bb.extents)}," +
                $"\"size\":{V3ToJson(bb.size)}," +
                $"\"min\":{V3ToJson(bb.min)}," +
                $"\"max\":{V3ToJson(bb.max)}}}"
            );
        }

        // Active scene as a tree. Only non-default fields are emitted; cap
        // via logLimit. Pass sceneObjectPath to zoom into a subtree.
        public static string GetHierarchy(ToolRequest r)
        {
            var scene = SceneManager.GetActiveScene();
            int cap = r.logLimit > 0 ? Math.Min(r.logLimit, 5000) : 200;

            Transform[] roots;
            if (!string.IsNullOrEmpty(r.sceneObjectName) || !string.IsNullOrEmpty(r.sceneObjectPath))
            {
                var go = FindGameObject(r);
                if (go == null) return Err("scene object not found (for subtree)");
                roots = new[] { go.transform };
            }
            else
            {
                var gos = scene.GetRootGameObjects();
                roots = new Transform[gos.Length];
                for (int i = 0; i < gos.Length; i++) roots[i] = gos[i].transform;
            }

            var sb = new StringBuilder("{");
            sb.Append("\"scene\":").Append(Json.Str(scene.name));
            sb.Append(",\"scenePath\":").Append(Json.Str(scene.path ?? ""));
            sb.Append(",\"isDirty\":").Append(scene.isDirty ? "true" : "false");
            sb.Append(",\"nodes\":[");
            int count = 0;
            bool first = true;
            bool truncated = false;
            foreach (var t in roots)
            {
                if (!EmitNode(sb, t, 0, cap, ref count, ref first)) { truncated = true; break; }
            }
            sb.Append("]");
            if (truncated) sb.Append(",\"truncated\":true,\"cap\":").Append(cap);
            sb.Append("}");
            return Ok(sb.ToString());
        }

        static bool EmitNode(StringBuilder sb, Transform t, int depth, int cap, ref int count, ref bool first)
        {
            if (count >= cap) return false;
            count++;
            if (!first) sb.Append(',');
            first = false;
            var go = t.gameObject;
            // Only emit fields that differ from Unity's defaults; cuts a
            // typical hierarchy response 2-3× - cheap root GameObjects ("Main
            // Camera", "Directional Light") now encode as ~60 chars instead
            // of ~200. The agent can infer unlisted fields are defaults.
            sb.Append('{');
            sb.Append("\"name\":").Append(Json.Str(go.name));
            if (depth > 0) { sb.Append(",\"depth\":").Append(depth); }
            sb.Append(",\"path\":").Append(Json.Str(TransformPath(t)));
            if (!go.activeInHierarchy) sb.Append(",\"active\":false");
            if (go.tag != "Untagged") sb.Append(",\"tag\":").Append(Json.Str(go.tag));
            if (go.layer != 0) sb.Append(",\"layer\":").Append(go.layer);
            var lp = t.localPosition;
            if (lp != Vector3.zero) sb.Append(",\"localPosition\":").Append(V3ToJson(lp));
            var lr = t.localEulerAngles;
            if (lr != Vector3.zero) sb.Append(",\"localRotation\":").Append(V3ToJson(lr));
            var ls = t.localScale;
            if (ls != Vector3.one) sb.Append(",\"localScale\":").Append(V3ToJson(ls));
            var comps = go.GetComponents<Component>();
            // Skip the implicit Transform - every GameObject has one.
            var emitted = 0;
            sb.Append(",\"components\":[");
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                var type = comps[i].GetType();
                if (type == typeof(Transform) || type == typeof(RectTransform)) continue;
                if (emitted > 0) sb.Append(',');
                sb.Append(Json.Str(type.Name));
                emitted++;
            }
            sb.Append(']');
            sb.Append('}');
            for (int i = 0; i < t.childCount; i++)
            {
                if (!EmitNode(sb, t.GetChild(i), depth + 1, cap, ref count, ref first)) return false;
            }
            return true;
        }

        static string TransformPath(Transform t)
        {
            if (t.parent == null) return t.name;
            return TransformPath(t.parent) + "/" + t.name;
        }

        public static string ListScenes()
        {
            var sb = new StringBuilder();
            sb.Append("{\"buildScenes\":[");
            var build = EditorBuildSettings.scenes;
            for (int i = 0; i < build.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"path\":").Append(Json.Str(build[i].path));
                sb.Append(",\"enabled\":").Append(build[i].enabled ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("],\"allScenes\":[");
            var guids = AssetDatabase.FindAssets("t:Scene");
            for (int i = 0; i < guids.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Json.Str(AssetDatabase.GUIDToAssetPath(guids[i])));
            }
            sb.Append("]}");
            return Ok(sb.ToString());
        }

        // Walks every loaded scene for GameObjects carrying componentType.
        // Includes inactive objects; accepts short or fully-qualified names.
        public static string FindObjectsByComponent(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.componentType)) return Err("componentType required");
            var type = FindType(r.componentType);
            if (type == null) return Err($"component type not found: {r.componentType}");
            int cap = r.logLimit > 0 ? Math.Min(r.logLimit, 2000) : 200;

            var sb = new StringBuilder("{\"type\":");
            sb.Append(Json.Str(type.FullName));
            sb.Append(",\"matches\":[");
            int count = 0;
            bool first = true;
            bool truncated = false;
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = root.GetComponentsInChildren(type, includeInactive: true);
                    foreach (var comp in found)
                    {
                        if (count >= cap) { truncated = true; break; }
                        if (!first) sb.Append(',');
                        first = false;
                        count++;
                        var go = (comp as Component)?.gameObject;
                        if (go == null) continue;
                        sb.Append('{');
                        sb.Append("\"name\":").Append(Json.Str(go.name));
                        sb.Append(",\"path\":").Append(Json.Str(TransformPath(go.transform)));
                        sb.Append(",\"scene\":").Append(Json.Str(scene.name));
                        sb.Append(",\"active\":").Append(go.activeInHierarchy ? "true" : "false");
                        sb.Append('}');
                    }
                    if (truncated) break;
                }
                if (truncated) break;
            }
            sb.Append(']');
            if (truncated) sb.Append(",\"truncated\":true,\"cap\":").Append(cap);
            sb.Append('}');
            return Ok(sb.ToString());
        }

        public static string GetEditorState()
        {
            var active = SceneManager.GetActiveScene();
            var sb = new StringBuilder("{");
            sb.Append("\"isPlaying\":").Append(EditorApplication.isPlaying ? "true" : "false");
            sb.Append(",\"isPaused\":").Append(EditorApplication.isPaused ? "true" : "false");
            sb.Append(",\"isCompiling\":").Append(MainThreadDispatcher.IsCompilingCached ? "true" : "false");
            sb.Append(",\"isUpdating\":").Append(MainThreadDispatcher.IsUpdatingCached ? "true" : "false");
            sb.Append(",\"activeScene\":{\"name\":").Append(Json.Str(active.name));
            sb.Append(",\"path\":").Append(Json.Str(active.path ?? ""));
            sb.Append(",\"isDirty\":").Append(active.isDirty ? "true" : "false").Append('}');
            sb.Append(",\"openScenes\":[");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (i > 0) sb.Append(',');
                var s = SceneManager.GetSceneAt(i);
                sb.Append("{\"name\":").Append(Json.Str(s.name));
                sb.Append(",\"path\":").Append(Json.Str(s.path ?? ""));
                sb.Append(",\"isLoaded\":").Append(s.isLoaded ? "true" : "false").Append('}');
            }
            sb.Append("],\"selection\":[");
            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var obj = Selection.objects[i];
                var path = AssetDatabase.GetAssetPath(obj);
                sb.Append(Json.Str(string.IsNullOrEmpty(path) ? (obj != null ? obj.name : "") : path));
            }
            sb.Append("]}");
            return Ok(sb.ToString());
        }

        public static string RenameSceneObject(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName)) return Err("oldName required");
            if (string.IsNullOrEmpty(newName)) return Err("newName required");
            var go = GameObject.Find(oldName);
            if (go == null) return Err($"scene object not found: {oldName}");
            Undo.RecordObject(go, "ione: rename");
            go.name = newName;
            MarkSceneDirty(go.scene);
            return Ok($"{{\"name\":{Json.Str(go.name)}}}");
        }

        public static string DeleteSceneObject(string name)
        {
            if (string.IsNullOrEmpty(name)) return Err("name required");
            var go = GameObject.Find(name);
            if (go == null) return Err($"scene object not found: {name}");
            Undo.DestroyObjectImmediate(go);
            return Ok("{}");
        }

        public static string SetTag(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            if (string.IsNullOrEmpty(r.tag)) return Err("tag required");
            Undo.RecordObject(go, "ione: set tag");
            try { go.tag = r.tag; }
            catch (UnityException) { return Err($"tag not defined: {r.tag} (add in Project Settings > Tags first)"); }
            MarkSceneDirty(go.scene);
            return Ok("{}");
        }

        public static string SetLayer(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            if (string.IsNullOrEmpty(r.layer)) return Err("layer required");
            int idx = LayerMask.NameToLayer(r.layer);
            if (idx < 0) return Err($"layer not defined: {r.layer}");
            Undo.RecordObject(go, "ione: set layer");
            go.layer = idx;
            MarkSceneDirty(go.scene);
            return Ok("{}");
        }

        public static string SetSelection(ToolRequest r)
        {
            if (r.selection == null || r.selection.Length == 0)
            {
                Selection.objects = Array.Empty<UnityEngine.Object>();
                return Ok("{}");
            }
            var objs = new System.Collections.Generic.List<UnityEngine.Object>();
            foreach (var s in r.selection)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(NormalizeAssetsPath(s));
                if (asset != null) { objs.Add(asset); continue; }
                var go = GameObject.Find(s);
                if (go != null) objs.Add(go);
            }
            Selection.objects = objs.ToArray();
            return Ok($"{{\"count\":{objs.Count}}}");
        }

        public static string GetSelection()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var obj = Selection.objects[i];
                var path = AssetDatabase.GetAssetPath(obj);
                sb.Append(Json.Str(string.IsNullOrEmpty(path) ? (obj != null ? obj.name : "") : path));
            }
            sb.Append("]");
            return Ok($"{{\"selection\":{sb}}}");
        }

        public static string SetPlayMode(bool play)
        {
            if (EditorApplication.isPlaying == play)
                return Ok($"{{\"isPlaying\":{(play ? "true" : "false")},\"changed\":false}}");
            if (!IoneSettings.AllowPlayMode)
                return Err("play_mode is disabled by the user in Tools → ione → Settings → Safety. Ask them to enable 'Play Mode'.");
            EditorApplication.isPlaying = play;
            return Ok($"{{\"isPlaying\":{(play ? "true" : "false")},\"changed\":true}}");
        }

        public static string ExecuteMenuItem(string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath)) return Err("menuPath required");
            if (!IoneSettings.AllowMenuItems)
                return Err("execute_menu_item is disabled by the user in Tools → ione → Settings → Safety. Ask them to enable 'Editor menu items'.");
            return EditorApplication.ExecuteMenuItem(menuPath)
                ? Ok("{}")
                : Err($"menu item not found or not executable: {menuPath}");
        }
    }
}
