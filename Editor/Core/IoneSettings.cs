using UnityEditor;

namespace Ione.Core
{
    // API keys, model selection, safety toggles. EditorPrefs (per-machine)
    // so nothing lands in project files.
    public static class IoneSettings
    {
        const string KeyAnthropic      = "Ione.AnthropicKey";
        const string KeyOpenAI         = "Ione.OpenAIKey";
        const string KeyProvider       = "Ione.Provider";      // "anthropic" | "openai"
        const string KeyAnthropicModel = "Ione.AnthropicModel";
        const string KeyOpenAIModel    = "Ione.OpenAIModel";
        const string KeyImageModel     = "Ione.ImageModel";
        const string KeyAllowScriptWrites   = "Ione.Safety.AllowScriptWrites";
        const string KeyAllowAssetDeletion  = "Ione.Safety.AllowAssetDeletion";
        const string KeyAllowMenuItems      = "Ione.Safety.AllowMenuItems";
        const string KeyAllowPlayMode       = "Ione.Safety.AllowPlayMode";
        const string KeyAllowSceneSwitching = "Ione.Safety.AllowSceneSwitching";
        const string KeySeenBackupWarning   = "Ione.SeenBackupWarning.v1";
        const string KeyLogRequests         = "Ione.LogRequests";

        public const string DefaultAnthropicModel = "claude-opus-4-7";
        public const string DefaultOpenAIModel    = "gpt-5.5";
        public const string DefaultImageModel     = "gpt-image-2";
        public const string DefaultProvider       = "anthropic";

        public static string AnthropicKey
        {
            get => EditorPrefs.GetString(KeyAnthropic, "");
            set => EditorPrefs.SetString(KeyAnthropic, value ?? "");
        }

        public static string OpenAIKey
        {
            get => EditorPrefs.GetString(KeyOpenAI, "");
            set => EditorPrefs.SetString(KeyOpenAI, value ?? "");
        }

        public static string Provider
        {
            get => EditorPrefs.GetString(KeyProvider, DefaultProvider);
            set => EditorPrefs.SetString(KeyProvider, value ?? DefaultProvider);
        }

        public static string AnthropicModel
        {
            get => EditorPrefs.GetString(KeyAnthropicModel, DefaultAnthropicModel);
            set => EditorPrefs.SetString(KeyAnthropicModel, value ?? DefaultAnthropicModel);
        }

        public static string OpenAIModel
        {
            get => EditorPrefs.GetString(KeyOpenAIModel, DefaultOpenAIModel);
            set => EditorPrefs.SetString(KeyOpenAIModel, value ?? DefaultOpenAIModel);
        }

        // generate_image uses this model. Authenticates with OpenAIKey.
        public static string ImageModel
        {
            get => EditorPrefs.GetString(KeyImageModel, DefaultImageModel);
            set => EditorPrefs.SetString(KeyImageModel, value ?? DefaultImageModel);
        }

        // Safety toggles. Default true (permissive). When false, the gated
        // tool returns an error telling the LLM which setting to ask about.
        public static bool AllowScriptWrites
        {
            get => EditorPrefs.GetBool(KeyAllowScriptWrites, true);
            set => EditorPrefs.SetBool(KeyAllowScriptWrites, value);
        }

        public static bool AllowAssetDeletion
        {
            get => EditorPrefs.GetBool(KeyAllowAssetDeletion, true);
            set => EditorPrefs.SetBool(KeyAllowAssetDeletion, value);
        }

        public static bool AllowMenuItems
        {
            get => EditorPrefs.GetBool(KeyAllowMenuItems, true);
            set => EditorPrefs.SetBool(KeyAllowMenuItems, value);
        }

        public static bool AllowPlayMode
        {
            get => EditorPrefs.GetBool(KeyAllowPlayMode, true);
            set => EditorPrefs.SetBool(KeyAllowPlayMode, value);
        }

        public static bool AllowSceneSwitching
        {
            get => EditorPrefs.GetBool(KeyAllowSceneSwitching, true);
            set => EditorPrefs.SetBool(KeyAllowSceneSwitching, value);
        }

        public static bool HasSeenBackupWarning
        {
            get => EditorPrefs.GetBool(KeySeenBackupWarning, false);
            set => EditorPrefs.SetBool(KeySeenBackupWarning, value);
        }

        // When true, AnthropicProvider and OpenAIProvider dump every
        // request and response to the Unity Console.
        public static bool LogRequests
        {
            get => EditorPrefs.GetBool(KeyLogRequests, true);
            set => EditorPrefs.SetBool(KeyLogRequests, value);
        }

        public static bool IsConfigured()
        {
            return Provider == "openai"
                ? !string.IsNullOrEmpty(OpenAIKey)
                : !string.IsNullOrEmpty(AnthropicKey);
        }
    }
}
