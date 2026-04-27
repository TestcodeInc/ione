using System;
using Ione.UI;
using UnityEditor;
using UnityEngine;

namespace Ione.Core
{
    // Menu entries under Tools/ione. Priority gaps create separators.
    public static class IoneBootstrap
    {
        public const string Version = "0.5.0";

        const string DiscordInviteUrl = "https://discord.gg/F9yUxnQjV8";
        const string FeedbackEmail = "help@ione.games";
        const string TermsUrl = "https://ione.games/terms";
        const string PrivacyUrl = "https://ione.games/privacy";

        // Per-machine flag so the chat window pops open exactly once after
        // install. delayCall defers past the package import so Unity is idle
        // when the window appears.
        const string FirstRunKey = "Ione.FirstRunShown.v1";

        [InitializeOnLoadMethod]
        static void FirstRunOpenChat()
        {
            if (EditorPrefs.GetBool(FirstRunKey, false)) return;
            EditorPrefs.SetBool(FirstRunKey, true);
            EditorApplication.delayCall += IoneChatWindow.ShowWindow;
        }

        [MenuItem("Tools/ione/Chat", priority = 0)]
        public static void OpenChat() => IoneChatWindow.ShowWindow();

        [MenuItem("Tools/ione/Settings...", priority = 1)]
        public static void OpenSettings() => IoneSettingsWindow.ShowWindow();

        [MenuItem("Tools/ione/Clear Chat History", priority = 20)]
        public static void ClearChatHistory() => IoneChatWindow.ClearHistory();

        [MenuItem("Tools/ione/Status", priority = 21)]
        public static void Status()
        {
            var provider = IoneSettings.Provider;
            var configured = IoneSettings.IsConfigured();
            var bridge = IoneHttpBridge.IsRunning ? $"on (http://127.0.0.1:{IoneHttpBridge.BoundPort})" : "off";
            Debug.Log($"[ione] v{Version}, provider={provider}, configured={configured}, bridge={bridge}");
        }

        [MenuItem("Tools/ione/HTTP Bridge/Start", priority = 30)]
        public static void StartBridge()
        {
            IoneSettings.HttpBridgeEnabled = true;
            IoneHttpBridge.Start();
        }

        [MenuItem("Tools/ione/HTTP Bridge/Stop", priority = 31)]
        public static void StopBridge()
        {
            IoneSettings.HttpBridgeEnabled = false;
            IoneHttpBridge.Stop();
            Debug.Log("[ione] HTTP bridge stopped.");
        }

        [MenuItem("Tools/ione/Join Discord", priority = 100)]
        public static void JoinDiscord() => Application.OpenURL(DiscordInviteUrl);

        [MenuItem("Tools/ione/Send Feedback", priority = 101)]
        public static void SendFeedback()
        {
            var subject = Uri.EscapeDataString($"ione v{Version} feedback");
            Application.OpenURL($"mailto:{FeedbackEmail}?subject={subject}");
        }

        [MenuItem("Tools/ione/Terms of Use", priority = 200)]
        public static void OpenTerms() => Application.OpenURL(TermsUrl);

        [MenuItem("Tools/ione/Privacy Policy", priority = 201)]
        public static void OpenPrivacy() => Application.OpenURL(PrivacyUrl);
    }
}
