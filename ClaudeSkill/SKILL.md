---
name: ione
description: Bridge between Unity and Claude Code via the ione HTTP bridge (http://127.0.0.1:7707). Use to have Claude Code interact with your Unity project that has the ione plugin installed and the bridge enabled.
---

# ione bridge

The ione Unity plugin exposes its full editor-tool API over a local HTTP server so external processes (like you) can drive Unity directly. Bridge is loopback-only (127.0.0.1) and opt-in.

## Preflight

Every endpoint requires a bearer token. The bridge generates one on first start and persists it at `~/.ione/bridge-token` (mode 0600). Read it once per session and reuse:

```bash
TOKEN=$(cat ~/.ione/bridge-token 2>/dev/null)
```

If `~/.ione/bridge-token` does not exist, the bridge has never been started on this machine — ask the user to enable it from inside Unity:
**Tools → ione → HTTP Bridge → Start**
(or set HttpBridgeEnabled in the ione Settings window). Then re-read the file.

Then confirm the bridge is reachable and your token is accepted:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" http://127.0.0.1:7707/
```

Expected: `{"name":"ione","version":"..."}`

- `connection refused` → bridge isn't running. Ask the user to start it (see above).
- `401` → token mismatch. Re-read `~/.ione/bridge-token` (it may have been regenerated) and retry. If it still fails, ask the user to restart the bridge.
- `200` → ready. Include `-H "Authorization: Bearer $TOKEN"` on every subsequent `/tools` and `/tool` call.

## Discovering tools

Get the live list of tools (name, description, JSON-schema parameters) — this is authoritative; the set evolves:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" http://127.0.0.1:7707/tools | jq '.[] | {name, description}'
```

Use `jq '.[] | select(.name=="<tool>")'` to get one tool's full schema before calling it.

## Invoking a tool

POST `/tool` with `{"name":"<tool>","args":{...}}`. Response is the tool's JSON envelope, typically `{"ok":true,"result":{...}}` or `{"ok":false,"error":"..."}`.

```bash
curl -sS -X POST http://127.0.0.1:7707/tool \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"get_editor_state","args":{}}'
```

Always pipe through `jq` for readability.

## Tool surface (high-level)

Don't memorize this — fetch `/tools` for current schemas. But common categories:

- **Observation** (start here, never guess editor state):
  - `get_editor_state` — isPlaying/isCompiling, active scene, selection
  - `get_hierarchy` — full scene tree (use `sceneObjectPath` to scope)
  - `list_scenes`, `list_folder`, `read_asset`
  - `find_objects_by_component`, `find_scene_object`
  - `get_components`, `get_field`, `get_renderer_bounds`
  - `get_logs`, `wait_for_compile` (pass `logSinceSeq` to dedupe)
  - `capture_game_view` — returns base64 PNG (response includes the image bytes; save to a file for inspection)

- **Scene editing**:
  - `create_primitive`, `create_empty`, `instantiate_prefab`
  - `set_transform`, `set_parent`, `rename_scene_object`, `delete_scene_object`
  - `set_tag`, `set_layer`, `set_selection`, `play_mode`

- **Components**:
  - `add_component`, `remove_component`, `set_field`, `save_prefab`

- **Assets**:
  - `ensure_folder`, `rename_asset`, `move_asset`, `delete_asset`
  - `create_material`, `assign_material`, `set_material_property`
  - `create_script` (writes a `.cs` under `Assets/`; **always** follow with `wait_for_compile` before continuing)
  - `new_scene`, `open_scene`, `save_scene`, `save_project`, `add_scene_to_build`

- **Animation**:
  - `create_animator_controller`, `add_animator_parameter`, `add_animator_state`, `add_animator_transition`, `assign_animator_controller`, `create_animation_clip`

- **Image generation**:
  - `generate_image` — calls OpenAI image API (uses the user's OpenAI key from Settings) and imports the result as a Sprite asset

- **Menu**:
  - `execute_menu_item` — runs any Unity Editor menu path

## Working patterns

- **Always observe first.** Run `get_editor_state` and `get_hierarchy` (scoped) before mutating. Don't assume scene contents.
- **After any `create_script`**, immediately call `wait_for_compile`. If the response has `errors`, fix them before doing anything else.
- **When a tool returns `"ok":false`**, read the `error` field and adjust — don't retry blindly. Common cause: a safety toggle is off (`AllowScriptWrites`, `AllowAssetDeletion`, `AllowMenuItems`, `AllowPlayMode`, `AllowSceneSwitching`). The error tells the user which Setting to flip.
- **Don't read `.meta` files** — `read_asset` refuses them. Read the underlying asset.
- **Large tool results**: `read_asset` caps at 32 KB (over-cap returns `truncated:true` + head). `get_hierarchy` can be huge — pass a `sceneObjectPath` to zoom in.

## Caveats

- The bridge runs Unity work on the main thread; long operations (compile, import) block other tool calls until they finish.
- Domain reloads (after `create_script` compile) tear down the listener briefly — calls during a reload may fail with connection refused. Wait a few hundred ms and retry, or use `wait_for_compile` first.
- Image-returning tools (`capture_game_view`, `generate_image`) attach PNG bytes as a top-level `images` array on the response, e.g. `{"ok":true,"result":{...metadata...},"images":[{"mediaType":"image/png","base64":"..."}]}`. Decode with `curl ... | jq -r '.images[0].base64' | base64 -d > out.png` to view.
- This skill assumes a single ione-equipped Unity Editor on `127.0.0.1:7707`. If the user changes the port (Settings → HttpBridgePort), substitute.
