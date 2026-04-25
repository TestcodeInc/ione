using System.IO;
using System.Text;
using Ione.Core;
using UnityEditor;
using UnityEngine;
using static Ione.Tools.ToolHelpers;

namespace Ione.Tools
{
    // Components, SerializedProperty reads/writes, prefab save.
    public static class ComponentTools
    {
        public static string AddComponent(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            var type = FindType(r.componentType);
            if (type == null) return Err($"component type not found: {r.componentType}");
            if (!typeof(Component).IsAssignableFrom(type)) return Err($"{type.FullName} is not a Component");
            var comp = Undo.AddComponent(go, type);
            if (comp == null) return Err($"failed to add component: {r.componentType}");
            MarkSceneDirty(go.scene);
            return Ok($"{{\"type\":{Json.Str(type.FullName)}}}");
        }

        public static string RemoveComponent(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            var type = FindType(r.componentType);
            if (type == null) return Err($"component type not found: {r.componentType}");
            var comp = go.GetComponent(type);
            if (comp == null) return Err($"component not on object: {r.componentType}");
            Undo.DestroyObjectImmediate(comp);
            MarkSceneDirty(go.scene);
            return Ok("{}");
        }

        public static string GetComponents(ToolRequest r)
        {
            var go = FindGameObject(r);
            if (go == null) return Err("scene object not found");
            var comps = go.GetComponents<Component>();
            var sb = new StringBuilder("[");
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) sb.Append(",");
                if (comps[i] == null) sb.Append("{\"type\":\"<missing>\"}");
                else sb.Append($"{{\"type\":{Json.Str(comps[i].GetType().FullName)}}}");
            }
            sb.Append("]");
            return Ok($"{{\"components\":{sb}}}");
        }

        public static string SetField(ToolRequest r)
        {
            var target = ResolveSerializedTarget(r);
            if (target == null) return Err("target not found");
            var so = new SerializedObject(target);
            var sp = FindProp(so, r.property);
            if (sp == null) return Err($"property not found: {r.property}");

            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask: sp.intValue = (int)r.valueNumber; break;
                case SerializedPropertyType.Float:     sp.floatValue = r.valueNumber; break;
                case SerializedPropertyType.Boolean:   sp.boolValue = r.valueBool; break;
                case SerializedPropertyType.String:    sp.stringValue = r.valueString ?? ""; break;
                case SerializedPropertyType.Enum:      sp.enumValueIndex = (int)r.valueNumber; break;
                case SerializedPropertyType.Color:
                {
                    var a = r.valueArray ?? new float[0];
                    sp.colorValue = new Color(Safe(a, 0), Safe(a, 1), Safe(a, 2), a.Length >= 4 ? a[3] : 1f);
                    break;
                }
                case SerializedPropertyType.Vector2:    sp.vector2Value = new Vector2(Safe(r.valueArray, 0), Safe(r.valueArray, 1)); break;
                case SerializedPropertyType.Vector3:    sp.vector3Value = new Vector3(Safe(r.valueArray, 0), Safe(r.valueArray, 1), Safe(r.valueArray, 2)); break;
                case SerializedPropertyType.Vector4:    sp.vector4Value = new Vector4(Safe(r.valueArray, 0), Safe(r.valueArray, 1), Safe(r.valueArray, 2), Safe(r.valueArray, 3)); break;
                case SerializedPropertyType.Quaternion: sp.quaternionValue = Quaternion.Euler(Safe(r.valueArray, 0), Safe(r.valueArray, 1), Safe(r.valueArray, 2)); break;
                case SerializedPropertyType.ObjectReference:
                {
                    UnityEngine.Object obj = null;
                    if (!string.IsNullOrEmpty(r.valueRef))
                    {
                        obj = ResolveObjectReference(r.valueRef, sp);
                        if (obj == null)
                            return Err($"valueRef did not resolve to a {sp.type} for {r.property}: {r.valueRef}");
                    }
                    // Empty/missing valueRef explicitly clears the reference.
                    sp.objectReferenceValue = obj;
                    break;
                }
                default: return Err($"unsupported property type: {sp.propertyType}");
            }
            so.ApplyModifiedProperties();
            if (target is Component c) MarkSceneDirty(c.gameObject.scene);
            else if (target is GameObject g) MarkSceneDirty(g.scene);
            else if (!string.IsNullOrEmpty(r.assetPath)) EditorUtility.SetDirty(target);
            return Ok($"{{\"property\":{Json.Str(r.property)},\"type\":{Json.Str(sp.propertyType.ToString())}}}");
        }

        public static string GetField(ToolRequest r)
        {
            var target = ResolveSerializedTarget(r);
            if (target == null) return Err("target not found");
            var so = new SerializedObject(target);
            var sp = FindProp(so, r.property);
            if (sp == null) return Err($"property not found: {r.property}");

            string valueJson;
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask: valueJson = sp.intValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Float:     valueJson = sp.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean:   valueJson = sp.boolValue ? "true" : "false"; break;
                case SerializedPropertyType.String:    valueJson = Json.Str(sp.stringValue); break;
                case SerializedPropertyType.Enum:      valueJson = sp.enumValueIndex.ToString(); break;
                case SerializedPropertyType.Color:     { var c = sp.colorValue; valueJson = $"[{Json.Num(c.r)},{Json.Num(c.g)},{Json.Num(c.b)},{Json.Num(c.a)}]"; break; }
                case SerializedPropertyType.Vector2:   { var v = sp.vector2Value; valueJson = $"[{Json.Num(v.x)},{Json.Num(v.y)}]"; break; }
                case SerializedPropertyType.Vector3:   { var v = sp.vector3Value; valueJson = $"[{Json.Num(v.x)},{Json.Num(v.y)},{Json.Num(v.z)}]"; break; }
                case SerializedPropertyType.Vector4:   { var v = sp.vector4Value; valueJson = $"[{Json.Num(v.x)},{Json.Num(v.y)},{Json.Num(v.z)},{Json.Num(v.w)}]"; break; }
                case SerializedPropertyType.ObjectReference:
                    valueJson = sp.objectReferenceValue != null
                        ? Json.Str(DescribeObjectReference(sp.objectReferenceValue))
                        : "null";
                    break;
                default: valueJson = Json.Str($"<unsupported: {sp.propertyType}>"); break;
            }
            return Ok($"{{\"property\":{Json.Str(r.property)},\"type\":{Json.Str(sp.propertyType.ToString())},\"value\":{valueJson}}}");
        }

        public static string SavePrefab(string sceneObjectName, string prefabPath, bool destroy)
        {
            if (string.IsNullOrEmpty(sceneObjectName)) return Err("sceneObjectName required");
            if (string.IsNullOrEmpty(prefabPath)) return Err("prefabPath required");
            var norm = NormalizeAssetsPath(prefabPath);
            if (!norm.EndsWith(".prefab")) norm += ".prefab";
            var go = GameObject.Find(sceneObjectName);
            if (go == null) return Err($"scene object not found: {sceneObjectName}");
            var parent = Path.GetDirectoryName(norm).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolderInternal(parent);
            GameObject prefab;
            if (destroy)
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(go, norm);
                if (prefab == null) return Err("SaveAsPrefabAsset returned null");
                Undo.DestroyObjectImmediate(go);
            }
            else
            {
                prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, norm, InteractionMode.UserAction);
                if (prefab == null) return Err("SaveAsPrefabAssetAndConnect returned null");
            }
            AssetDatabase.Refresh();
            return Ok($"{{\"path\":{Json.Str(norm)},\"destroyedSceneObject\":{(destroy ? "true" : "false")}}}");
        }
    }
}
