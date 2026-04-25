using System.IO;
using System.Linq;
using Ione.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // AnimatorController authoring: params, states, transitions, clips.
    public static class AnimatorTools
    {
        public static string CreateAnimatorController(ToolRequest r)
        {
            if (string.IsNullOrEmpty(r.path)) return Err("path required");
            var norm = NormalizeAssetsPath(r.path);
            if (!norm.EndsWith(".controller")) norm += ".controller";
            var parent = Path.GetDirectoryName(norm).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(norm);
            if (ctrl == null) return Err("CreateAnimatorControllerAtPath returned null");
            return Ok($"{{\"path\":{Json.Str(norm)}}}");
        }

        public static string AddAnimatorParameter(ToolRequest r)
        {
            var cp = !string.IsNullOrEmpty(r.controllerPath) ? r.controllerPath : r.assetPath;
            var ctrl = Load(cp);
            if (ctrl == null) return Err($"controller not found: {cp}");
            if (string.IsNullOrEmpty(r.name)) return Err("name required");
            AnimatorControllerParameterType ptype;
            switch ((r.parameterType ?? "Float").ToLowerInvariant())
            {
                case "int":     ptype = AnimatorControllerParameterType.Int; break;
                case "bool":    ptype = AnimatorControllerParameterType.Bool; break;
                case "trigger": ptype = AnimatorControllerParameterType.Trigger; break;
                default:        ptype = AnimatorControllerParameterType.Float; break;
            }
            if (ctrl.parameters.Any(p => p.name == r.name))
                return Ok($"{{\"name\":{Json.Str(r.name)},\"added\":false}}");
            ctrl.AddParameter(r.name, ptype);
            EditorUtility.SetDirty(ctrl);
            return Ok($"{{\"name\":{Json.Str(r.name)},\"type\":{Json.Str(ptype.ToString())},\"added\":true}}");
        }

        public static string AddAnimatorState(ToolRequest r)
        {
            var cp = !string.IsNullOrEmpty(r.controllerPath) ? r.controllerPath : r.assetPath;
            var ctrl = Load(cp);
            if (ctrl == null) return Err($"controller not found: {cp}");
            if (string.IsNullOrEmpty(r.stateName)) return Err("stateName required");
            if (ctrl.layers.Length == 0) return Err("controller has no layers");
            var sm = ctrl.layers[0].stateMachine;
            var existing = sm.states.FirstOrDefault(s => s.state.name == r.stateName).state;
            AnimatorState state = existing != null ? existing : sm.AddState(r.stateName);
            if (!string.IsNullOrEmpty(r.motionPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(NormalizeAssetsPath(r.motionPath));
                if (clip == null) return Err($"motion clip not found: {r.motionPath}");
                state.motion = clip;
            }
            EditorUtility.SetDirty(ctrl);
            return Ok($"{{\"stateName\":{Json.Str(state.name)}}}");
        }

        public static string AddAnimatorTransition(ToolRequest r)
        {
            var cp = !string.IsNullOrEmpty(r.controllerPath) ? r.controllerPath : r.assetPath;
            var ctrl = Load(cp);
            if (ctrl == null) return Err($"controller not found: {cp}");
            if (ctrl.layers.Length == 0) return Err("controller has no layers");
            if (string.IsNullOrEmpty(r.fromState) || string.IsNullOrEmpty(r.toState))
                return Err("fromState and toState required");
            var sm = ctrl.layers[0].stateMachine;
            var from = sm.states.FirstOrDefault(s => s.state.name == r.fromState).state;
            var to   = sm.states.FirstOrDefault(s => s.state.name == r.toState).state;
            if (from == null) return Err($"fromState not found: {r.fromState}");
            if (to == null)   return Err($"toState not found: {r.toState}");
            var t = from.AddTransition(to);
            t.hasExitTime = r.hasExitTime;
            t.duration = r.transitionDuration > 0 ? r.transitionDuration : 0.25f;
            EditorUtility.SetDirty(ctrl);
            return Ok($"{{\"from\":{Json.Str(from.name)},\"to\":{Json.Str(to.name)}}}");
        }

        public static string AssignAnimatorController(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            var cp = !string.IsNullOrEmpty(r.controllerPath) ? r.controllerPath : r.assetPath;
            var ctrl = Load(cp);
            if (ctrl == null) return Err($"controller not found: {cp}");
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = Undo.AddComponent<Animator>(go);
            Undo.RecordObject(anim, "ione: assign controller");
            anim.runtimeAnimatorController = ctrl;
            MarkSceneDirty(go.scene);
            return Ok("{}");
        }

        public static string CreateAnimationClip(ToolRequest r)
        {
            var cp = !string.IsNullOrEmpty(r.clipPath) ? r.clipPath : r.path;
            if (string.IsNullOrEmpty(cp)) return Err("clipPath required");
            var norm = NormalizeAssetsPath(cp);
            if (!norm.EndsWith(".anim")) norm += ".anim";
            var parent = Path.GetDirectoryName(norm).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(norm);
            if (clip == null)
            {
                clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(norm) };
                AssetDatabase.CreateAsset(clip, norm);
            }
            if (r.loop)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            if (!string.IsNullOrEmpty(r.propertyPath) && r.valueArray != null && r.valueArray.Length >= 2)
            {
                var typeName = string.IsNullOrEmpty(r.targetType) ? "UnityEngine.Transform" : r.targetType;
                var type = FindType(typeName);
                if (type == null) return Err($"target type not found: {typeName}");
                var dur = r.duration > 0 ? r.duration : 1f;
                var keys = new Keyframe[r.valueArray.Length];
                for (int i = 0; i < r.valueArray.Length; i++)
                {
                    var t = (r.valueArray.Length == 1) ? 0f : dur * i / (r.valueArray.Length - 1);
                    keys[i] = new Keyframe(t, r.valueArray[i]);
                }
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0.5f);
                var binding = new EditorCurveBinding { path = "", type = type, propertyName = r.propertyPath };
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return Ok($"{{\"path\":{Json.Str(norm)}}}");
        }

        static AnimatorController Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(NormalizeAssetsPath(path));
        }
    }
}
