using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ione.Core;
using Ione.LLM;
using Ione.Tools;
using UnityEditor;
using UnityEngine;

namespace Ione.UI
{
    // Main chat window. State is static; persists across window close via
    // SessionState (see ChatPersistence) and is healed on load.
    public class IoneChatWindow : EditorWindow
    {
        static IoneChatWindow instance;
        static List<ChatMessage> history;
        static readonly object historyLock = new object();
        static readonly HashSet<string> expandedToolCalls = new HashSet<string>();
        static string input = "";
        static volatile bool isBusy;
        static string statusMessage;
        static string errorMessage;
        static CancellationTokenSource cts;
        static int pendingRepaints;
        static bool persistenceHooksInstalled;
        // DrawHistory consumes this to pin the scroll to the new bottom.
        static volatile bool scrollToBottom;

        static void EnsureHistoryLoaded()
        {
            if (history != null) return;
            history = ChatPersistence.Load();
            HealOrphanedToolUses(history);
        }

        // Backfill placeholder tool_results for any tool_use that lost its
        // matching response (mid-turn reload, cancel, or crash). Without
        // this the next send 400s: tool_use must be followed by tool_result.
        static void HealOrphanedToolUses(List<ChatMessage> h)
        {
            if (h == null) return;
            for (int i = 0; i < h.Count; i++)
            {
                var m = h[i];
                if (m.Role != ChatRole.Assistant) continue;
                if (m.ToolCalls == null || m.ToolCalls.Count == 0) continue;

                var next = i + 1 < h.Count ? h[i + 1] : null;
                var satisfied = new HashSet<string>();
                if (next != null && next.Role == ChatRole.Tool && next.ToolResults != null)
                    foreach (var tr in next.ToolResults) satisfied.Add(tr.ToolCallId);

                var missing = new List<ToolResult>();
                foreach (var tc in m.ToolCalls)
                    if (!satisfied.Contains(tc.Id))
                        missing.Add(new ToolResult
                        {
                            ToolCallId = tc.Id,
                            Content = "{\"ok\":false,\"error\":\"result lost - previous turn was interrupted\"}",
                            IsError = true,
                        });
                if (missing.Count == 0) continue;

                if (next != null && next.Role == ChatRole.Tool)
                {
                    // Append into the existing tool-results message in place.
                    if (next.ToolResults == null) next.ToolResults = new List<ToolResult>();
                    next.ToolResults.AddRange(missing);
                }
                else
                {
                    // Insert a brand-new tool-results message right after.
                    h.Insert(i + 1, ChatMessage.ToolResults_(missing));
                }
            }
        }

        // Main-thread only.
        static void SaveHistory()
        {
            if (history == null) return;
            lock (historyLock) ChatPersistence.Save(history);
        }

        // Background-safe; posts the save to main.
        static void SaveHistoryFromBackground()
        {
            if (history == null) return;
            MainThreadDispatcher.Post(SaveHistory);
        }

        static void InstallPersistenceHooks()
        {
            if (persistenceHooksInstalled) return;
            persistenceHooksInstalled = true;
            // Clear isBusy before a reload; the async task doesn't survive.
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                try { cts?.Cancel(); } catch { }
                isBusy = false;
                SaveHistory();
            };
        }

        Vector2 scroll;

        public static void ShowWindow()
        {
            var w = GetWindow<IoneChatWindow>("ione");
            w.minSize = new Vector2(520, 420);
            w.Focus();
            MaybeShowFirstRunBackupWarning();
        }

        static void MaybeShowFirstRunBackupWarning()
        {
            if (IoneSettings.HasSeenBackupWarning) return;
            EditorUtility.DisplayDialog(
                "ione - back up your project first",
                "ione can modify your scenes, assets, and scripts directly. Many changes are NOT reversible with Unity Undo - in particular script writes, image writes, and AssetDatabase operations.\n\n" +
                "Before giving the agent complex tasks, commit to git or make a project backup.\n\n" +
                "This warning won't appear again.",
                "I understand");
            IoneSettings.HasSeenBackupWarning = true;
        }

        public static void ClearHistory()
        {
            EnsureHistoryLoaded();
            lock (historyLock) history.Clear();
            ChatPersistence.Clear();
            expandedToolCalls.Clear();
            errorMessage = null;
            statusMessage = null;
            if (instance != null) instance.Repaint();
        }

        public static void NotifySettingsChanged()
        {
            if (instance != null) instance.Repaint();
        }

        void OnEnable()
        {
            instance = this;
            EnsureHistoryLoaded();
            InstallPersistenceHooks();
            EditorApplication.update += DrainRepaints;
        }

        void OnDisable()
        {
            EditorApplication.update -= DrainRepaints;
            if (instance == this) instance = null;
        }

        void DrainRepaints()
        {
            if (Interlocked.Exchange(ref pendingRepaints, 0) > 0) Repaint();
        }

        static void RequestRepaint() => Interlocked.Increment(ref pendingRepaints);

        void OnGUI()
        {
            DrawHeader();
            DrawHistory();
            DrawInput();
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var provider = IoneSettings.Provider == "openai" ? "OpenAI" : "Anthropic";
                var model = IoneSettings.Provider == "openai" ? IoneSettings.OpenAIModel : IoneSettings.AnthropicModel;
                GUILayout.Label($"{provider} · {model}", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Settings", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    IoneSettingsWindow.ShowWindow();
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    ClearHistory();
            }

            if (!IoneSettings.IsConfigured())
            {
                EditorGUILayout.HelpBox(
                    "Add an API key in Settings to start chatting.",
                    MessageType.Warning);
            }
        }

        void DrawHistory()
        {
            // BeginScrollView clamps scroll.y to content height, so
            // float.MaxValue lands at the bottom on the next paint.
            if (scrollToBottom)
            {
                scroll.y = float.MaxValue;
                scrollToBottom = false;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            ChatMessage[] snapshot;
            lock (historyLock) snapshot = history.ToArray();
            foreach (var m in snapshot) DrawMessage(m);
            if (isBusy)
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(statusMessage) ? "Thinking…" : statusMessage,
                    EditorStyles.miniLabel);
            }
            if (!string.IsNullOrEmpty(errorMessage))
            {
                EditorGUILayout.HelpBox(errorMessage, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawMessage(ChatMessage m)
        {
            switch (m.Role)
            {
                case ChatRole.User:
                    DrawBubble("You", m.Text, new Color(0.15f, 0.35f, 0.6f, 0.25f));
                    break;
                case ChatRole.Assistant:
                    if (!string.IsNullOrEmpty(m.Text))
                        DrawBubble("ione", m.Text, new Color(0.2f, 0.2f, 0.2f, 0.25f));
                    if (m.ToolCalls != null)
                        foreach (var tc in m.ToolCalls) DrawToolCall(tc);
                    break;
                case ChatRole.Tool:
                    if (m.ToolResults != null)
                        foreach (var tr in m.ToolResults) DrawToolResult(tr);
                    break;
            }
        }

        static void DrawBubble(string author, string text, Color tint)
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = prevBg;
                EditorGUILayout.LabelField(author, EditorStyles.boldLabel);
                var content = new GUIContent(text ?? "");
                var width = Mathf.Max(160, EditorGUIUtility.currentViewWidth - 60);
                var height = EditorStyles.wordWrappedLabel.CalcHeight(content, width);
                EditorGUILayout.SelectableLabel(text ?? "", EditorStyles.wordWrappedLabel,
                    GUILayout.Height(height));
            }
            GUI.backgroundColor = prevBg;
        }

        void DrawToolCall(ToolCall tc)
        {
            var expanded = expandedToolCalls.Contains(tc.Id);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.label, GUILayout.Width(16)))
                    {
                        if (expanded) expandedToolCalls.Remove(tc.Id);
                        else expandedToolCalls.Add(tc.Id);
                    }
                    EditorGUILayout.LabelField($"tool · {tc.Name}", EditorStyles.miniBoldLabel);
                }
                if (expanded)
                    EditorGUILayout.SelectableLabel(tc.ArgsJson ?? "", EditorStyles.textArea,
                        GUILayout.MinHeight(40));
            }
        }

        void DrawToolResult(ToolResult tr)
        {
            var expanded = expandedToolCalls.Contains("result:" + tr.ToolCallId);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.label, GUILayout.Width(16)))
                    {
                        if (expanded) expandedToolCalls.Remove("result:" + tr.ToolCallId);
                        else expandedToolCalls.Add("result:" + tr.ToolCallId);
                    }
                    EditorGUILayout.LabelField(
                        tr.IsError ? "result · error" : "result · ok",
                        EditorStyles.miniLabel);
                }
                if (expanded)
                    EditorGUILayout.SelectableLabel(tr.Content ?? "", EditorStyles.textArea,
                        GUILayout.MinHeight(40));
            }
        }

        // Lazily built in OnGUI; EditorStyles isn't available at load.
        static GUIStyle wrappingTextArea;

        void DrawInput()
        {
            if (wrappingTextArea == null)
                wrappingTextArea = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.SetNextControlName("IoneInput");
                input = EditorGUILayout.TextArea(input ?? "", wrappingTextArea,
                    GUILayout.MinHeight(48), GUILayout.ExpandHeight(false));
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (isBusy)
                    {
                        if (GUILayout.Button("Stop", GUILayout.Width(80)))
                        {
                            try { cts?.Cancel(); } catch { }
                        }
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(!IoneSettings.IsConfigured() || string.IsNullOrWhiteSpace(input)))
                        {
                            if (GUILayout.Button("Send", GUILayout.Width(80))) Send();
                        }
                    }
                }
            }

            // Cmd/Ctrl + Enter submits from the text area - single Enter keeps newlines.
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && (e.command || e.control) && !isBusy)
            {
                Send();
                e.Use();
            }
        }

        void Send()
        {
            if (isBusy || string.IsNullOrWhiteSpace(input)) return;
            if (!IoneSettings.IsConfigured())
            {
                errorMessage = "Add an API key in Settings first.";
                return;
            }
            var userText = input.Trim();
            input = "";
            GUI.FocusControl(null);
            errorMessage = null;
            EnsureHistoryLoaded();
            // Re-heal each turn; covers orphans from older builds or a
            // provider switch that never triggered a cold load.
            lock (historyLock) HealOrphanedToolUses(history);
            lock (historyLock) history.Add(ChatMessage.User(userText));
            scrollToBottom = true;
            SaveHistory();
            cts = new CancellationTokenSource();
            isBusy = true;
            var token = cts.Token;
            // EditorPrefs is main-thread only.
            var provider = BuildProvider();
            var systemPrompt = BuildSystemPrompt();
            // One undo group per turn: single Cmd+Z reverses the whole turn.
            Undo.IncrementCurrentGroup();
            var turnGroup = Undo.GetCurrentGroup();
            var label = userText.Length > 50 ? userText.Substring(0, 50) + "…" : userText;
            Undo.SetCurrentGroupName("ione: " + label);
            _ = Task.Run(() => RunLoopAsync(provider, systemPrompt, token, turnGroup));
        }

        static async Task RunLoopAsync(ILLMProvider provider, string systemPrompt, CancellationToken ct, int turnGroup)
        {
            bool locked = false;
            try
            {
                // Lock reloads for the duration of the turn. Compiles still
                // run; the domain reload is deferred until we unlock in
                // finally{}, so create_script mid-loop doesn't kill us.
                MainThreadDispatcher.RunOnMain(() =>
                {
                    EditorApplication.LockReloadAssemblies();
                    return 0;
                });
                locked = true;

                int safetyLimit = 50;
                while (!ct.IsCancellationRequested && safetyLimit-- > 0)
                {
                    statusMessage = "Thinking…";
                    RequestRepaint();
                    ChatMessage[] snapshot;
                    lock (historyLock) snapshot = history.ToArray();
                    var resp = await provider.SendAsync(systemPrompt, snapshot, ct).ConfigureAwait(false);
                    lock (historyLock) history.Add(ChatMessage.Assistant(resp.Text, resp.ToolCalls));
                    scrollToBottom = true;
                    SaveHistoryFromBackground();
                    RequestRepaint();
                    if (!resp.RequestsTools) break;

                    // Every tool_use id must have a matching tool_result in
                    // the next message; the finally below backfills any
                    // we didn't reach so we never leave an orphan on cancel.
                    var results = new List<ToolResult>();
                    try
                    {
                        foreach (var tc in resp.ToolCalls)
                        {
                            if (ct.IsCancellationRequested) break;
                            statusMessage = $"Running {tc.Name}…";
                            RequestRepaint();
                            ToolOutput output;
                            try
                            {
                                output = await ToolRouter.InvokeAsync(tc.Name, tc.ArgsJson).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                output = new ToolOutput
                                {
                                    Content = "{\"ok\":false,\"error\":" + Json.Str(e.Message) + "}",
                                    IsError = true,
                                };
                            }
                            results.Add(new ToolResult
                            {
                                ToolCallId = tc.Id,
                                Content = output.Content,
                                IsError = output.IsError,
                                Images = output.Images,
                            });
                            RequestRepaint();
                        }
                    }
                    finally
                    {
                        var seen = new HashSet<string>();
                        foreach (var r in results) seen.Add(r.ToolCallId);
                        foreach (var tc in resp.ToolCalls)
                        {
                            if (seen.Contains(tc.Id)) continue;
                            results.Add(new ToolResult
                            {
                                ToolCallId = tc.Id,
                                Content = "{\"ok\":false,\"error\":\"canceled before execution\"}",
                                IsError = true,
                            });
                        }
                        lock (historyLock) history.Add(ChatMessage.ToolResults_(results));
                        scrollToBottom = true;
                        SaveHistoryFromBackground();
                        RequestRepaint();
                    }
                    if (ct.IsCancellationRequested) break;
                }
                if (safetyLimit <= 0)
                {
                    errorMessage = "Tool call loop hit its safety limit (50 rounds).";
                }
            }
            catch (OperationCanceledException)
            {
                statusMessage = "Stopped.";
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                Debug.LogException(e);
            }
            finally
            {
                // Unlock + collapse the undo group on main thread. Refresh
                // picks up any create_script writes whose ImportAsset was
                // deferred via delayCall.
                var wasLocked = locked;
                MainThreadDispatcher.Post(() =>
                {
                    try { Undo.CollapseUndoOperations(turnGroup); } catch { }
                    if (wasLocked)
                    {
                        try { EditorApplication.UnlockReloadAssemblies(); } catch { }
                        try { AssetDatabase.Refresh(); } catch { }
                    }
                });
                isBusy = false;
                statusMessage = null;
                RequestRepaint();
            }
        }

        static ILLMProvider BuildProvider()
        {
            return IoneSettings.Provider == "openai"
                ? (ILLMProvider)new OpenAIProvider(IoneSettings.OpenAIKey, IoneSettings.OpenAIModel)
                : new AnthropicProvider(IoneSettings.AnthropicKey, IoneSettings.AnthropicModel);
        }

        static string BuildSystemPrompt()
        {
            return
                "You are ione, an agent operating inside the Unity Editor. You have tools that manipulate the real editor - scene graph, assets, components, scripts, animation, and play mode. All work happens on this user's machine; there is no sandbox.\n" +
                "\n" +
                "Observation first - don't guess the editor state:\n" +
                "- get_editor_state: one call returns isPlaying/isCompiling, active scene, open scenes, selection.\n" +
                "- get_hierarchy: full scene tree with transforms and components in one call (pass sceneObjectPath to zoom).\n" +
                "- find_objects_by_component: cross-scene search for everything that has a component type.\n" +
                "- read_asset: re-read a file (including scripts you just wrote).\n" +
                "- list_scenes: discover scenes before opening or building.\n" +
                "- capture_game_view: actually see the screen as an image on your next turn.\n" +
                "\n" +
                "Plan and reuse what's already in context:\n" +
                "- Before issuing tool calls, briefly state the goal and the tools you'll need, then call them in one batch when possible.\n" +
                "- Do NOT re-query data you already have in this conversation. If get_hierarchy returned a position or component list, use it; don't call find_scene_object on objects you already saw.\n" +
                "- Trust tool return values. set_transform returns both position and localPosition - read those instead of re-issuing the call to second-guess world vs local space. set_field returns the property type; check it instead of probing.\n" +
                "- Prefer one wide call (get_hierarchy, find_objects_by_component) over many narrow ones (find_scene_object x N).\n" +
                "- To fit geometry to existing geometry, call get_renderer_bounds on both sides and compute position/scale arithmetically. Don't iterate scale by screenshot - capture_game_view returns ~250 KB images and converging visually usually takes 5+ rounds.\n" +
                "- Every extra tool round-trip costs the user real money on Opus. Be deliberate.\n" +
                "\n" +
                "Execution guidelines:\n" +
                "- Do the work yourself with tools. Never ask the user to paste code, click menus, or edit fields by hand - call the tool instead.\n" +
                "- For ANY visual asset (sprite, texture, UI icon, background, character art, tileset piece, logo), call generate_image - never ask the user to provide artwork and never settle for a blank/placeholder. The generated PNG lands under Assets/ configured as a Sprite, ready to drop into a SpriteRenderer or UI Image.\n" +
                "- After create_script, always call wait_for_compile before telling the user you're done. If there are compile errors, fix them with create_script + wait_for_compile again.\n" +
                "- NEW types written with create_script are NOT usable in the same turn (the editor defers the domain reload until the turn ends). Plan: write+compile in turn N, then add_component or any type lookup in turn N+1. If the user asked for a behavior that needs the new type wired up, end turn N with a concise 'I've written X; tell me to wire it up' and do the wiring on their next message.\n" +
                "- Before claiming something visually works, capture_game_view and inspect the image.\n" +
                "- All asset paths are under Assets/, use forward slashes. Paths that escape Assets/ are rejected.\n" +
                "- Scene-graph and component mutations register Unity Undo entries; the whole user turn collapses to a single Cmd+Z. Script writes, image writes, and AssetDatabase operations (create/move/delete) are NOT reversible with Undo.\n" +
                "- Five capabilities can be disabled by the user under Tools → ione → Settings → Safety: script writes, asset deletion, editor menu items, play mode, scene switching. All are enabled by default. If a gated tool returns 'disabled by the user' - stop and ask them to enable it; don't try workarounds.\n" +
                "\n" +
                "execute_menu_item pitfalls (these have burned past sessions):\n" +
                "- Don't guess menu paths. If you're not sure a path exists, use a dedicated tool instead. 'Edit/Frame Selected' is NOT a menu item - it's a hotkey-only command.\n" +
                "- 'Edit/Duplicate' is unreliable through this API - it depends on which editor window has focus. To clone a scene object with all bindings intact, use save_prefab (destroySceneObject:false) + instantiate_prefab (unpack:true) instead.\n" +
                "- 'GameObject/Move To View' moves the selected object to the scene camera. It does NOT frame the camera on the object. Don't use it to 'see' something.\n" +
                "- 'Edit/Undo' undoes the most recent registered op, which may be a component you just added this turn - not the thing you actually want to undo. Fix forward instead.\n" +
                "\n" +
                "Building objects from a referenced model:\n" +
                "- For a wall/prop tile from an .fbx, you do NOT need to clone an existing instance. create_empty + add_component (MeshFilter, MeshRenderer, MeshCollider) + set_field with valueRef='Path/X.fbx' works directly - set_field auto-resolves the sub-asset to match the field's expected type. Use 'Path/X.fbx::SubName' if you need a specific sub-asset. get_field returns ObjectReferences in this same form, so you can copy-paste between them.\n" +
                "\n" +
                "- Final replies to the user are 1-3 sentences - short, concrete, what changed. No walkthroughs, no setup instructions.";
        }
    }
}
