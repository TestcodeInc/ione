using Ione.Core;
using UnityEditor;
using UnityEngine;

namespace Ione.UI
{
    // Settings panel: provider, API keys, models, safety toggles.
    public class IoneSettingsWindow : EditorWindow
    {
        string anthropicKey;
        string openAIKey;
        string anthropicModel;
        string openAIModel;
        string imageModel;
        int providerIdx;

        bool allowScriptWrites;
        bool allowAssetDeletion;
        bool allowMenuItems;
        bool allowPlayMode;
        bool allowSceneSwitching;
        bool logRequests;
        Vector2 scroll;

        static readonly string[] ProviderLabels = { "Anthropic (Claude)", "OpenAI (GPT)" };
        static readonly string[] ProviderValues = { "anthropic", "openai" };

        public static void ShowWindow()
        {
            var w = GetWindow<IoneSettingsWindow>(true, "ione - Settings", true);
            w.minSize = new Vector2(520, 460);
            w.Load();
        }

        void Load()
        {
            anthropicKey = IoneSettings.AnthropicKey;
            openAIKey = IoneSettings.OpenAIKey;
            anthropicModel = IoneSettings.AnthropicModel;
            openAIModel = IoneSettings.OpenAIModel;
            imageModel = IoneSettings.ImageModel;
            allowScriptWrites   = IoneSettings.AllowScriptWrites;
            allowAssetDeletion  = IoneSettings.AllowAssetDeletion;
            allowMenuItems      = IoneSettings.AllowMenuItems;
            allowPlayMode       = IoneSettings.AllowPlayMode;
            allowSceneSwitching = IoneSettings.AllowSceneSwitching;
            logRequests = IoneSettings.LogRequests;
            var p = IoneSettings.Provider;
            providerIdx = 0;
            for (int i = 0; i < ProviderValues.Length; i++)
                if (ProviderValues[i] == p) providerIdx = i;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Provider", EditorStyles.boldLabel);
            providerIdx = EditorGUILayout.Popup("Active", providerIdx, ProviderLabels);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Anthropic", EditorStyles.boldLabel);
            anthropicKey = EditorGUILayout.PasswordField("API Key", anthropicKey ?? "");
            anthropicModel = EditorGUILayout.TextField("Model", anthropicModel ?? "");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("OpenAI", EditorStyles.boldLabel);
            openAIKey = EditorGUILayout.PasswordField("API Key", openAIKey ?? "");
            openAIModel = EditorGUILayout.TextField("Chat Model", openAIModel ?? "");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Image Generation", EditorStyles.boldLabel);
            imageModel = EditorGUILayout.TextField("Model", imageModel ?? "");
            EditorGUILayout.LabelField(
                "Used by generate_image. Authenticates with the OpenAI API key above.",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Safety - agent capabilities", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "All capabilities are enabled by default. Turn off individual toggles to refuse a category.",
                EditorStyles.miniLabel);
            allowScriptWrites   = EditorGUILayout.ToggleLeft(
                "Script writes (create_script - writes arbitrary C# that compiles and runs)",
                allowScriptWrites);
            allowAssetDeletion  = EditorGUILayout.ToggleLeft(
                "Asset deletion (delete_asset - not reversible with Undo)",
                allowAssetDeletion);
            allowMenuItems      = EditorGUILayout.ToggleLeft(
                "Editor menu items (execute_menu_item - covers Build & Run, Reimport All, Quit, any third-party menu)",
                allowMenuItems);
            allowPlayMode       = EditorGUILayout.ToggleLeft(
                "Play Mode (play_mode - entering/exiting resets runtime state)",
                allowPlayMode);
            allowSceneSwitching = EditorGUILayout.ToggleLeft(
                "Scene switching (new_scene, open_scene single-mode - can discard unsaved edits)",
                allowSceneSwitching);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            logRequests = EditorGUILayout.ToggleLeft(
                "Log every API request and response to the Console (base64 image data is redacted)",
                logRequests);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Keys are stored in EditorPrefs on this machine only - they never touch your project files.\n" +
                "Get a key: console.anthropic.com or platform.openai.com.",
                MessageType.Info);

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel")) Close();
                if (GUILayout.Button("Save", GUILayout.Width(120)))
                {
                    IoneSettings.Provider = ProviderValues[providerIdx];
                    IoneSettings.AnthropicKey = (anthropicKey ?? "").Trim();
                    IoneSettings.OpenAIKey = (openAIKey ?? "").Trim();
                    IoneSettings.AnthropicModel = (anthropicModel ?? "").Trim();
                    IoneSettings.OpenAIModel = (openAIModel ?? "").Trim();
                    IoneSettings.ImageModel = (imageModel ?? "").Trim();
                    IoneSettings.AllowScriptWrites   = allowScriptWrites;
                    IoneSettings.AllowAssetDeletion  = allowAssetDeletion;
                    IoneSettings.AllowMenuItems      = allowMenuItems;
                    IoneSettings.AllowPlayMode       = allowPlayMode;
                    IoneSettings.AllowSceneSwitching = allowSceneSwitching;
                    IoneSettings.LogRequests         = logRequests;
                    IoneDebug.Refresh();
                    Close();
                    IoneChatWindow.NotifySettingsChanged();
                }
            }
        }
    }
}
