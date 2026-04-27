using System.Collections.Generic;

namespace Ione.Tools
{
    // JSON schema for every tool exposed to the LLM. Both providers build
    // on top of this list.
    public static class ToolSchemas
    {
        public class ToolDef
        {
            public string Name;
            public string Description;
            public string ParametersJson; // raw JSON Schema for the parameters object
        }

        static List<ToolDef> cached;

        public static IReadOnlyList<ToolDef> All => cached ?? (cached = Build());

        static List<ToolDef> Build()
        {
            var t = new List<ToolDef>();

            // scene creation
            t.Add(new ToolDef {
                Name = "create_primitive",
                Description = "Create a primitive GameObject (Cube, Sphere, Capsule, Cylinder, Plane, Quad) in the active scene.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""primitive"":{""type"":""string"",""enum"":[""Cube"",""Sphere"",""Capsule"",""Cylinder"",""Plane"",""Quad""]},
                        ""name"":{""type"":""string""},
                        ""position"":{""type"":""array"",""items"":{""type"":""number""},""minItems"":3,""maxItems"":3},
                        ""rotation"":{""type"":""array"",""items"":{""type"":""number""},""minItems"":3,""maxItems"":3},
                        ""scale"":{""type"":""array"",""items"":{""type"":""number""},""minItems"":3,""maxItems"":3},
                        ""parentName"":{""type"":""string""}
                    },
                    ""required"":[""primitive""]
                }"
            });
            t.Add(new ToolDef {
                Name = "create_empty",
                Description = "Create an empty GameObject.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""name"":{""type"":""string""},
                        ""position"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""rotation"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""scale"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""parentName"":{""type"":""string""}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "instantiate_prefab",
                Description = "Instantiate a prefab asset into the active scene.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""prefabPath"":{""type"":""string"",""description"":""path under Assets/, e.g. Assets/Prefabs/Player.prefab""},
                        ""name"":{""type"":""string""},
                        ""position"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""rotation"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""scale"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""parentName"":{""type"":""string""},
                        ""unpack"":{""type"":""boolean""}
                    },
                    ""required"":[""prefabPath""]
                }"
            });

            // scene graph
            t.Add(new ToolDef {
                Name = "set_transform",
                Description = "Set position/rotation/scale on a scene object. worldSpace=true uses world coords; otherwise local.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""sceneObjectPath"":{""type"":""string""},
                        ""position"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""rotation"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""scale"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""worldSpace"":{""type"":""boolean""}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "set_parent",
                Description = "Reparent a scene object. Pass empty parentName to move it to the scene root.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""parentName"":{""type"":""string""}
                    },
                    ""required"":[""sceneObjectName""]
                }"
            });
            t.Add(new ToolDef {
                Name = "find_scene_object",
                Description = "Inspect a scene object: transform, components, children, tag, layer.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""sceneObjectPath"":{""type"":""string""}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "get_renderer_bounds",
                Description = "World-space axis-aligned bounding box from the GameObject's active Renderers (encapsulates self + all descendants by default). Returns center/extents/size/min/max. Use this to FIT geometry to existing geometry exactly - read the bounds of the thing you're aligning against, compute target position/scale arithmetically, then set_transform once. This avoids the eyeball-from-screenshots loop, which is expensive and rarely converges. Pass selfOnly:true to skip descendants.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""sceneObjectPath"":{""type"":""string""},
                        ""selfOnly"":{""type"":""boolean""}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "get_hierarchy",
                Description = "Return the active scene as a tree with name/path/components and only the non-default fields per GameObject (omitted fields = Unity defaults: active=true, tag=Untagged, layer=0, localPosition=0, localRotation=0, localScale=1; Transform/RectTransform components are implicit and omitted). Prefer this over repeated find_scene_object calls. Pass sceneObjectName or sceneObjectPath to zoom to a subtree. logLimit caps node count (default 200, max 5000) - 'truncated' signals when to re-query a smaller subtree.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""sceneObjectPath"":{""type"":""string""},
                        ""logLimit"":{""type"":""integer"",""description"":""max nodes to emit (default 200)""}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "list_scenes",
                Description = "List every scene asset under Assets/ plus the build-settings scene list (with enabled flag). Use to discover scenes before opening or adding to build.",
                ParametersJson = @"{""type"":""object"",""properties"":{}}"
            });
            t.Add(new ToolDef {
                Name = "get_editor_state",
                Description = "One-call snapshot of editor state: isPlaying, isPaused, isCompiling, isUpdating, activeScene (name/path/isDirty), open scenes, and current selection. Use instead of chaining several status queries.",
                ParametersJson = @"{""type"":""object"",""properties"":{}}"
            });
            t.Add(new ToolDef {
                Name = "find_objects_by_component",
                Description = "Walk all loaded scenes and return every GameObject that has componentType attached (searches derived types; includes inactive objects). Short or fully-qualified type names both work.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""componentType"":{""type"":""string""},
                        ""logLimit"":{""type"":""integer"",""description"":""max matches (default 200, max 2000)""}
                    },
                    ""required"":[""componentType""]
                }"
            });
            t.Add(new ToolDef {
                Name = "read_asset",
                Description = "Read a text asset from Assets/ (script, shader, JSON, YAML, etc.). Response is capped at 256 KB - larger files return the head with truncated:true. Use after create_script to verify content.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}},
                    ""required"":[""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "rename_scene_object",
                Description = "Rename a scene object.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""oldName"":{""type"":""string""},
                        ""newName"":{""type"":""string""}
                    },
                    ""required"":[""oldName"",""newName""]
                }"
            });
            t.Add(new ToolDef {
                Name = "delete_scene_object",
                Description = "Delete a scene object (Undo-registered).",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""name"":{""type"":""string""}},
                    ""required"":[""name""]
                }"
            });
            t.Add(new ToolDef {
                Name = "set_tag",
                Description = "Set a GameObject's tag. The tag must already exist in Project Settings > Tags.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""sceneObjectName"":{""type"":""string""},""tag"":{""type"":""string""}},
                    ""required"":[""sceneObjectName"",""tag""]
                }"
            });
            t.Add(new ToolDef {
                Name = "set_layer",
                Description = "Set a GameObject's layer by name.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""sceneObjectName"":{""type"":""string""},""layer"":{""type"":""string""}},
                    ""required"":[""sceneObjectName"",""layer""]
                }"
            });
            t.Add(new ToolDef {
                Name = "set_selection",
                Description = "Select one or more scene objects or asset paths in the editor.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""selection"":{""type"":""array"",""items"":{""type"":""string""}}}
                }"
            });
            t.Add(new ToolDef {
                Name = "get_selection",
                Description = "Get the current editor selection (scene objects and/or asset paths).",
                ParametersJson = @"{""type"":""object"",""properties"":{}}"
            });
            t.Add(new ToolDef {
                Name = "play_mode",
                Description = "Enter or exit Play Mode.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""play"":{""type"":""boolean""}},
                    ""required"":[""play""]
                }"
            });
            t.Add(new ToolDef {
                Name = "execute_menu_item",
                Description = "Execute ANY editor menu item by full path, including Unity's built-ins and third-party menus (e.g. 'GameObject/Align With View', 'File/Build And Run', 'Window/Rendering/Lighting'). No allow-list - trust the path the user's plugins expose. Use this as an escape hatch when no dedicated tool exists.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""menuPath"":{""type"":""string""}},
                    ""required"":[""menuPath""]
                }"
            });

            // components / fields
            t.Add(new ToolDef {
                Name = "add_component",
                Description = "Add a component to a scene object. componentType accepts short or fully-qualified names (e.g. 'Rigidbody', 'UnityEngine.Rigidbody').",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""componentType"":{""type"":""string""}
                    },
                    ""required"":[""sceneObjectName"",""componentType""]
                }"
            });
            t.Add(new ToolDef {
                Name = "remove_component",
                Description = "Remove a component from a scene object.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""componentType"":{""type"":""string""}
                    },
                    ""required"":[""sceneObjectName"",""componentType""]
                }"
            });
            t.Add(new ToolDef {
                Name = "get_components",
                Description = "List components on a scene object.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""sceneObjectName"":{""type"":""string""}},
                    ""required"":[""sceneObjectName""]
                }"
            });
            t.Add(new ToolDef {
                Name = "set_field",
                Description = @"Set a serialized (Inspector-visible) field on a component or asset. Only serialized Unity fields work - public fields or [SerializeField] privates; arbitrary C# properties and nested managed references are not supported. Use exactly one of the value fields that matches the target type:
- valueNumber: Integer, Float, Enum, LayerMask
- valueBool:   Boolean
- valueString: String
- valueArray:  Vector2/3/4, Color (RGBA), Quaternion (euler)
- valueRef:    ObjectReference - asset path (preferred) or scene-object name. For multi-asset containers like .fbx, the field's expected type is matched against sub-assets automatically (e.g. setting m_Mesh to 'Path/X.fbx' picks the inner Mesh, not the root GameObject). To pin a specific sub-asset by name, use 'Path/X.fbx::SubName' - get_field returns ObjectReferences in this exact form. An unresolvable valueRef returns an error rather than silently clearing the field; omit valueRef to clear explicitly.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""componentType"":{""type"":""string""},
                        ""assetPath"":{""type"":""string""},
                        ""property"":{""type"":""string""},
                        ""valueNumber"":{""type"":""number""},
                        ""valueBool"":{""type"":""boolean""},
                        ""valueString"":{""type"":""string""},
                        ""valueArray"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""valueRef"":{""type"":""string""}
                    },
                    ""required"":[""property""]
                }"
            });
            t.Add(new ToolDef {
                Name = "get_field",
                Description = "Read a serialized field from a component or asset.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""componentType"":{""type"":""string""},
                        ""assetPath"":{""type"":""string""},
                        ""property"":{""type"":""string""}
                    },
                    ""required"":[""property""]
                }"
            });
            t.Add(new ToolDef {
                Name = "save_prefab",
                Description = "Save a scene object as a prefab asset. destroySceneObject=true removes the scene instance after saving.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""prefabPath"":{""type"":""string""},
                        ""destroySceneObject"":{""type"":""boolean""}
                    },
                    ""required"":[""sceneObjectName"",""prefabPath""]
                }"
            });

            // assets / project
            t.Add(new ToolDef {
                Name = "ensure_folder",
                Description = "Ensure a folder exists under Assets/ (creates intermediate folders).",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}},
                    ""required"":[""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "rename_asset",
                Description = "Rename an asset (keeps its extension).",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""},""newName"":{""type"":""string""}},
                    ""required"":[""path"",""newName""]
                }"
            });
            t.Add(new ToolDef {
                Name = "move_asset",
                Description = "Move an asset to a new path under Assets/.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""},""newPath"":{""type"":""string""}},
                    ""required"":[""path"",""newPath""]
                }"
            });
            t.Add(new ToolDef {
                Name = "delete_asset",
                Description = "Delete an asset. Destructive and NOT reversible via Unity Undo. User can disable this category under Tools → ione → Settings → Safety (Asset deletion); if they have, the call returns a specific error. Prefer move_asset when you just want to relocate.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}},
                    ""required"":[""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "list_folder",
                Description = "List direct children of a folder (filesystem-backed - fast even during imports).",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}}
                }"
            });
            t.Add(new ToolDef {
                Name = "create_material",
                Description = "Create a new material asset. shaderName defaults to 'Standard'.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""},""shaderName"":{""type"":""string""}},
                    ""required"":[""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "assign_material",
                Description = "Assign a material to a scene object's Renderer.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""assetPath"":{""type"":""string""}
                    },
                    ""required"":[""sceneObjectName"",""assetPath""]
                }"
            });
            t.Add(new ToolDef {
                Name = "set_material_property",
                Description = "Set a material property. Use valueArray for Color (3 or 4 floats), valueRef for a texture asset path, or valueNumber for a float.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""assetPath"":{""type"":""string""},
                        ""propertyName"":{""type"":""string""},
                        ""valueNumber"":{""type"":""number""},
                        ""valueArray"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""valueRef"":{""type"":""string""}
                    },
                    ""required"":[""assetPath"",""propertyName""]
                }"
            });
            t.Add(new ToolDef {
                Name = "create_script",
                Description = "Write a C# MonoBehaviour (or any script) to disk under Assets/. Follow up with wait_for_compile to detect errors before continuing.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""},""content"":{""type"":""string""}},
                    ""required"":[""path"",""content""]
                }"
            });
            t.Add(new ToolDef {
                Name = "save_project",
                Description = "Save dirty assets (AssetDatabase.SaveAssets).",
                ParametersJson = @"{""type"":""object"",""properties"":{}}"
            });
            t.Add(new ToolDef {
                Name = "new_scene",
                Description = "Create a new scene. sceneName='empty' for an empty scene; otherwise default (with Camera + Light).",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""sceneName"":{""type"":""string""}}
                }"
            });
            t.Add(new ToolDef {
                Name = "open_scene",
                Description = "Open a scene asset. additive=true merges into the current scene.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""},""additive"":{""type"":""boolean""}},
                    ""required"":[""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "save_scene",
                Description = "Save the active scene to disk. Pass 'path' to save-as (works for an unsaved scene or to fork to a new file). Omit 'path' only if the active scene already has a saved location.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}}
                }"
            });
            t.Add(new ToolDef {
                Name = "add_scene_to_build",
                Description = "Add a scene to the build settings list.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}},
                    ""required"":[""path""]
                }"
            });

            // vision / image assets
            t.Add(new ToolDef {
                Name = "capture_game_view",
                Description = "Capture the Game (or Scene) view and return it as a visible image attached to the tool result - you will actually see the pixels on your next turn. Use this to verify what's on screen before and after changes.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""captureWidth"":{""type"":""integer""},
                        ""captureHeight"":{""type"":""integer""},
                        ""captureSource"":{""type"":""string"",""enum"":[""game"",""scene""]}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "generate_image",
                Description = "Generate a PNG using the configured image model (default gpt-image-1.5) and import it under Assets/ as a Sprite with alpha transparency. This is how you produce art - whenever the task calls for a sprite, texture, UI icon, background, tileset piece, character, or any other visual asset, call generate_image instead of asking the user to supply artwork or using a placeholder colored cube. Writes a single PNG per call. Transparent background is always on; describe an explicit background in the prompt if you need opacity. Cost: one image API call per invocation.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""prompt"":{""type"":""string"",""description"":""Describe the image. Include style ('pixel art', 'flat vector'), subject, pose, and framing. For sprites note alpha expectations (e.g. 'centered sprite, no background').""},
                        ""path"":{""type"":""string"",""description"":""Destination under Assets/, e.g. 'Assets/Sprites/player.png'. Folder is created if missing.""},
                        ""imageSize"":{""type"":""string"",""enum"":[""1024x1024"",""1536x1024"",""1024x1536"",""auto""],""description"":""Output resolution (default 1024x1024).""}
                    },
                    ""required"":[""prompt"",""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "save_image_asset",
                Description = "Write a PNG (base64) to Assets/ and import it as a Sprite.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""path"":{""type"":""string""},
                        ""imageBase64"":{""type"":""string""}
                    },
                    ""required"":[""path"",""imageBase64""]
                }"
            });

            // animator
            t.Add(new ToolDef {
                Name = "create_animator_controller",
                Description = "Create an AnimatorController asset.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{""path"":{""type"":""string""}},
                    ""required"":[""path""]
                }"
            });
            t.Add(new ToolDef {
                Name = "add_animator_parameter",
                Description = "Add a parameter to an AnimatorController.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""controllerPath"":{""type"":""string""},
                        ""name"":{""type"":""string""},
                        ""parameterType"":{""type"":""string"",""enum"":[""Float"",""Int"",""Bool"",""Trigger""]}
                    },
                    ""required"":[""controllerPath"",""name""]
                }"
            });
            t.Add(new ToolDef {
                Name = "add_animator_state",
                Description = "Add (or update) a state on the first layer of an AnimatorController. Optionally bind an AnimationClip via motionPath.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""controllerPath"":{""type"":""string""},
                        ""stateName"":{""type"":""string""},
                        ""motionPath"":{""type"":""string""}
                    },
                    ""required"":[""controllerPath"",""stateName""]
                }"
            });
            t.Add(new ToolDef {
                Name = "add_animator_transition",
                Description = "Add a transition between two states.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""controllerPath"":{""type"":""string""},
                        ""fromState"":{""type"":""string""},
                        ""toState"":{""type"":""string""},
                        ""hasExitTime"":{""type"":""boolean""},
                        ""transitionDuration"":{""type"":""number""}
                    },
                    ""required"":[""controllerPath"",""fromState"",""toState""]
                }"
            });
            t.Add(new ToolDef {
                Name = "assign_animator_controller",
                Description = "Attach an Animator with the given controller to a scene object.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""sceneObjectName"":{""type"":""string""},
                        ""controllerPath"":{""type"":""string""}
                    },
                    ""required"":[""sceneObjectName"",""controllerPath""]
                }"
            });
            t.Add(new ToolDef {
                Name = "create_animation_clip",
                Description = @"Create (or update) an AnimationClip asset. Optionally add one keyframe curve bound to a property on the animated root (empty hierarchy path - for child animations, call again with per-child clips or use a full curve binding):
- propertyPath e.g. 'm_LocalPosition.x' for Transform, 'm_Color.r' for SpriteRenderer
- targetType defaults to UnityEngine.Transform
- valueArray is the sequence of values at equally-spaced times across 'duration' seconds.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""clipPath"":{""type"":""string""},
                        ""propertyPath"":{""type"":""string""},
                        ""targetType"":{""type"":""string""},
                        ""valueArray"":{""type"":""array"",""items"":{""type"":""number""}},
                        ""duration"":{""type"":""number""},
                        ""loop"":{""type"":""boolean""}
                    },
                    ""required"":[""clipPath""]
                }"
            });

            // diagnostics
            t.Add(new ToolDef {
                Name = "get_logs",
                Description = "Read recent console + compile log entries. logLevel: 'info' | 'warning' | 'error' | 'exception' | 'compile-error' | 'compile-warning' | 'error+' (default) | 'warning+' | 'any'. Pass logSinceSeq from a previous call to page forward.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""logLevel"":{""type"":""string""},
                        ""logLimit"":{""type"":""integer""},
                        ""logSinceSeq"":{""type"":""integer""}
                    }
                }"
            });
            t.Add(new ToolDef {
                Name = "wait_for_compile",
                Description = "Block (up to logWaitMs, default 15000) until the editor finishes compiling, then return new compile errors and warnings. Call after create_script before continuing.",
                ParametersJson = @"{
                    ""type"":""object"",
                    ""properties"":{
                        ""logWaitMs"":{""type"":""integer""},
                        ""logSinceSeq"":{""type"":""integer""}
                    }
                }"
            });

            // Minify every schema once at cache-build time. The verbose
            // multi-line @"…" literals above are pleasant to read in source
            // but bloat every request body and the cache-write payload by
            // ~30%. Stripping insignificant JSON whitespace is lossless.
            foreach (var def in t)
                def.ParametersJson = MinifyJson(def.ParametersJson);

            return t;
        }

        // JSON-aware whitespace stripper. Preserves whitespace inside
        // double-quoted strings (including escaped quotes inside strings).
        // Drops every space / tab / CR / LF outside strings.
        static string MinifyJson(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            bool inString = false, escaped = false;
            foreach (var c in s)
            {
                if (escaped) { sb.Append(c); escaped = false; continue; }
                if (inString && c == '\\') { sb.Append(c); escaped = true; continue; }
                if (c == '"') { sb.Append(c); inString = !inString; continue; }
                if (!inString && (c == ' ' || c == '\t' || c == '\n' || c == '\r')) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
