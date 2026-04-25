using System;
using System.Threading.Tasks;
using Ione.Core;
using UnityEngine;

namespace Ione.Tools
{
    // Dispatches LLM tool calls to the right Tools class. Read-only actions
    // with thread-safe backing (logs, folder listing, compile-wait) run off
    // the main thread so the chat stays responsive during imports.
    public static class ToolRouter
    {
        public static async Task<ToolOutput> InvokeAsync(string action, string argsJson)
        {
            if (string.IsNullOrEmpty(action)) return Text(ToolHelpers.Err("action required"));
            ToolRequest req;
            try
            {
                req = string.IsNullOrWhiteSpace(argsJson)
                    ? new ToolRequest()
                    : JsonUtility.FromJson<ToolRequest>(argsJson);
                if (req == null) req = new ToolRequest();
            }
            catch (Exception e)
            {
                return Text(ToolHelpers.Err($"bad args for {action}: {e.Message}"));
            }
            req.action = action;

            try
            {
                // These two return images; route through ToolOutput directly.
                if (action == "capture_game_view")
                    return MainThreadDispatcher.RunOnMain(() => CaptureTools.CaptureGameView(req));
                if (action == "generate_image")
                    return await ImageGenerationTools.GenerateImageAsync(req);

                if (action == "get_logs")         return Text(DiagnosticsTools.GetLogs(req));
                if (action == "wait_for_compile") return Text(await DiagnosticsTools.WaitForCompileAsync(req));
                if (action == "list_folder")      return Text(DiagnosticsTools.ListFolder(req.path));
                return Text(MainThreadDispatcher.RunOnMain(() => Execute(req)));
            }
            catch (Exception e)
            {
                return Text(ToolHelpers.Err(e.Message));
            }
        }

        static ToolOutput Text(string content) =>
            new ToolOutput { Content = content, IsError = LooksLikeError(content) };

        static bool LooksLikeError(string json)
        {
            var parsed = Json.ParseObject(json);
            return parsed != null
                && parsed.TryGetValue("ok", out var v)
                && v is bool b && !b;
        }

        static string Execute(ToolRequest r)
        {
            switch (r.action)
            {
                case "create_primitive":      return SceneTools.CreatePrimitive(r);
                case "create_empty":          return SceneTools.CreateEmpty(r);
                case "instantiate_prefab":    return SceneTools.InstantiatePrefab(r);
                case "set_transform":         return SceneTools.SetTransform(r);
                case "set_parent":            return SceneTools.SetParent(r);
                case "find_scene_object":     return SceneTools.FindSceneObject(r);
                case "get_renderer_bounds":   return SceneTools.GetRendererBounds(r);
                case "get_hierarchy":         return SceneTools.GetHierarchy(r);
                case "list_scenes":           return SceneTools.ListScenes();
                case "get_editor_state":      return SceneTools.GetEditorState();
                case "find_objects_by_component": return SceneTools.FindObjectsByComponent(r);
                case "read_asset":            return AssetTools.ReadAsset(r);
                case "rename_scene_object":   return SceneTools.RenameSceneObject(r.oldName, r.newName);
                case "delete_scene_object":   return SceneTools.DeleteSceneObject(r.name);
                case "set_tag":               return SceneTools.SetTag(r);
                case "set_layer":             return SceneTools.SetLayer(r);
                case "set_selection":         return SceneTools.SetSelection(r);
                case "get_selection":         return SceneTools.GetSelection();
                case "play_mode":             return SceneTools.SetPlayMode(r.play);
                case "execute_menu_item":     return SceneTools.ExecuteMenuItem(r.menuPath);

                case "add_component":         return ComponentTools.AddComponent(r);
                case "remove_component":      return ComponentTools.RemoveComponent(r);
                case "get_components":        return ComponentTools.GetComponents(r);
                case "set_field":             return ComponentTools.SetField(r);
                case "get_field":             return ComponentTools.GetField(r);
                case "save_prefab":           return ComponentTools.SavePrefab(r.sceneObjectName, r.prefabPath, r.destroySceneObject);

                case "ensure_folder":         return AssetTools.EnsureFolder(r.path);
                case "rename_asset":          return AssetTools.RenameAsset(r.path, r.newName);
                case "move_asset":            return AssetTools.MoveAsset(r);
                case "delete_asset":          return AssetTools.DeleteAsset(r.path);
                case "create_material":       return AssetTools.CreateMaterial(r);
                case "assign_material":       return AssetTools.AssignMaterial(r);
                case "set_material_property": return AssetTools.SetMaterialProperty(r);
                case "create_script":         return AssetTools.CreateScript(r);
                case "save_project":          return AssetTools.SaveProject();
                case "new_scene":             return AssetTools.NewScene(r);
                case "open_scene":            return AssetTools.OpenScene(r);
                case "save_scene":            return AssetTools.SaveScene(r);
                case "add_scene_to_build":    return AssetTools.AddSceneToBuild(r);

                case "save_image_asset":      return CaptureTools.SaveImageAsset(r);

                case "create_animator_controller": return AnimatorTools.CreateAnimatorController(r);
                case "add_animator_parameter":     return AnimatorTools.AddAnimatorParameter(r);
                case "add_animator_state":         return AnimatorTools.AddAnimatorState(r);
                case "add_animator_transition":    return AnimatorTools.AddAnimatorTransition(r);
                case "assign_animator_controller": return AnimatorTools.AssignAnimatorController(r);
                case "create_animation_clip":      return AnimatorTools.CreateAnimationClip(r);

                default: return ToolHelpers.Err("unknown action: " + (r.action ?? "<null>"));
            }
        }
    }
}
