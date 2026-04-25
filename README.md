# ione

A free bridge between Unity and AI

ione solves the problem of AI coding agents not being able to fully interact
with Unity (or giving you manual wiring instructions).

With this plugin, AI can create GameObjects, write C# scripts, generate sprites, wire up AnimatorControllers, run Play
Mode, inspect the Game view, etc. Every tool interacts with the real editor.

ione is free to use and uses no backend. Your API key
goes straight into ione settings in Unity.

- Website: https://ione.games
- Discord: https://discord.gg/F9yUxnQjV8
- Email: help@ione.games

> **By installing or using ione you agree to the
> [Terms of Use](https://ione.games/terms) and
> [Privacy Policy](https://ione.games/privacy).**
> The plugin can run arbitrary AI-generated code inside your editor and
> your project. Back up your work before giving it large tasks.

---

## Install

### Option A: Unity Package Manager (recommended)

1. Open your Unity project.
2. `Window` > `Package Manager`.
3. Click the `+` button (top left) > `Add package from git URL...`
4. Paste:

   ```
   https://github.com/TestcodeInc/ione.git
   ```

5. Click `Add`. Unity downloads, imports, and compiles the plugin.

Done. Skip to [First run](#first-run).

### Option B: Drop-in folder

1. Download the repo as a zip: `Code` > `Download ZIP`.
2. Unzip.
3. Copy the `Editor/` folder (and `package.json`) into your Unity project's `Assets/ione/` folder.
4. Let Unity compile.

---

## First run

1. `Tools` > `ione` > `Settings...`
2. Paste an API key and pick a model.
   - Get an Anthropic key: https://console.anthropic.com
   - Get an OpenAI key: https://platform.openai.com
   - Image generation (`generate_image`) uses `gpt-image-2` and needs
     the OpenAI key regardless of which chat provider you choose.

   > **Tip:** `claude-opus-4-7` currently gives the best results, but
   > can also get a bit expensive in token use.
3. `Tools` > `ione` > `Chat`.
4. Type a prompt and hit `Cmd+Enter` (or the Send button).

REMINDER: back up your project before chatting! This is important [Safety](#safety).

### Prompts to try

```
Build a 2D platformer scene. Player sprite, three platforms,
camera that follows the player.
```

```
Generate a pixel-art knight sprite, a slime sprite, and a tileset
for a dungeon floor. Wire them into a new scene.
```

```
Write a CameraOrbit script that rotates around a target at a
configurable speed. Attach it to the Main Camera pointed at the Cube.
```

```
Make an idle and run AnimationClip for the Player GameObject and
wire up an AnimatorController with transitions driven by a Speed
parameter.
```

---

## What the agent can do

35+ tools across eight areas:

| Area        | Tools                                                                   |
| ----------- | ----------------------------------------------------------------------- |
| Scene graph | `create_primitive`, `create_empty`, `instantiate_prefab`, `set_transform`, `set_parent`, `rename_scene_object`, `delete_scene_object`, `get_hierarchy`, `find_scene_object`, `find_objects_by_component`, `set_tag`, `set_layer`, `set_selection`, `get_selection`, `play_mode` |
| Components  | `add_component`, `remove_component`, `get_components`, `set_field`, `get_field`, `save_prefab` |
| Assets      | `ensure_folder`, `rename_asset`, `move_asset`, `delete_asset`, `list_folder`, `read_asset`, `list_scenes`, `new_scene`, `open_scene`, `save_scene`, `save_project`, `add_scene_to_build`, `save_image_asset` |
| Materials   | `create_material`, `assign_material`, `set_material_property`          |
| Scripts     | `create_script`, `wait_for_compile`                                     |
| Animation   | `create_animator_controller`, `add_animator_parameter`, `add_animator_state`, `add_animator_transition`, `assign_animator_controller`, `create_animation_clip` |
| Vision      | `capture_game_view` (returns real image content to the model), `generate_image` (gpt-image-2 -> transparent PNG Sprite under Assets/) |
| Diagnostics | `get_logs`, `get_editor_state`, `execute_menu_item`                     |

### Things worth knowing

- **Undo:** scene and component mutations register with Unity Undo. The
  whole agent turn collapses into a single group, so one `Cmd+Z`
  reverses everything it did on that turn.
- **Compile:** after `create_script`, the agent calls `wait_for_compile`
  and reads errors back. New types written mid-turn are not usable
  until the next turn (the editor's domain reload is deferred until
  the turn ends).
- **Vision:** `capture_game_view` and `generate_image` go back to the
  model as real image content blocks. The agent sees what it made and
  iterates.
- **Cost:** Tokens can add up, even with caching. Make sure to keep an
  eye on it or use cheaper models.

---

## Safety

Five capabilities can be locked down in `Tools` > `ione` > `Settings` >
`Safety`. All default to **on** (permissive):

- Script writes (`create_script`)
- Asset deletion (`delete_asset`)
- Editor menu items (`execute_menu_item` - covers `Build & Run`,
  `Reimport All`, `Quit`, etc.)
- Play Mode (`play_mode`)
- Scene switching (`new_scene`, `open_scene` in single mode)

When a capability is off, the agent gets a specific error telling it
which setting to ask you to enable. It will not try workarounds.

**Back up your project and use version control!** Unity Undo does not
cover file writes (scripts, images) or most AssetDatabase operations
(create/move/delete).

---

## Configuration

All settings live in Unity `EditorPrefs`, per-machine, never written
into your project files:

- Provider (Anthropic or OpenAI) and model names
- API keys (stored as passwords in EditorPrefs)
- Image model (default `gpt-image-2`)
- Safety toggles
- First-run acknowledgment

Chat history lives in `SessionState` and survives domain reloads. It
clears when you quit the editor. `Tools` > `ione` > `Clear Chat
History` wipes it as well.

---

## Requirements

- Unity **2022.3** or newer.
- An Anthropic or OpenAI API key with credit.

---

## Architecture

```
Assets/ione/Editor/
├── Core/      bootstrap, settings, paths, JSON, log capture, main-thread dispatcher
├── Tools/     tool surface + schemas (scene, components, assets, animator, vision, diagnostics)
├── LLM/       provider-agnostic chat types, Anthropic client, OpenAI client
└── UI/        EditorWindow chat + settings + persistence
```

---

## Troubleshooting

**"OpenAI 400: tool_call_ids did not have response messages"** after
switching providers mid-session. This is auto-healed on the next send
(ione backfills any orphaned tool calls). If it keeps firing, use
`Tools` > `ione` > `Clear Chat History`.

**Agent can't find a type it just wrote.** New types aren't live in
the same turn the script was written. Say `go ahead` on the next
prompt and it will pick up.

**"Main thread timeout (120s)"** usually means Unity is mid-import
when the agent tried to run a main-thread action. Wait for the import
to finish and resend.

---

## Legal

By installing or using ione you agree to the
[Terms of Use](https://ione.games/terms) and
[Privacy Policy](https://ione.games/privacy). See also the
[Copyright Dispute Policy](https://ione.games/copyright).

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Copyright (c) 2026 Testcode, Inc.

---

## Contributing

Issues and PRs welcome. For larger design changes, please open an
issue first or join our Discord.

Contributions are accepted under the Apache License 2.0 (see Section 5
of [LICENSE](LICENSE)). First-time contributors are asked to sign a
one-line Contributor License Agreement when you open a PR - see
[CLA.md](CLA.md). This lets us relicense in the future if we need to
(e.g. to add a commercial edition) and keeps the project's copyright
clean.

## Privacy

Nothing leaves your machine except direct API calls to the provider
you configured. https://ione.games/privacy.

## Trademarks

ione and Testcode, Inc. are **not affiliated with, endorsed by, or
sponsored by Unity Technologies, Anthropic, or OpenAI**. Unity is a
trademark of Unity Technologies. Claude is a trademark of Anthropic.
ChatGPT and GPT are trademarks of OpenAI. All other trademarks are
the property of their respective owners.
