using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Ione.Core
{
    // Bearer token used by the HTTP bridge. Generated on first start and
    // persisted at ~/.ione/bridge-token (mode 0600 on Unix). CLI clients
    // read the same file to authenticate. Lives outside the Unity project
    // so it's never committed and survives across projects on the same
    // machine.
    public static class IoneBridgeToken
    {
        public static readonly string TokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ione",
            "bridge-token");

        static string cached;

        // Returns the persisted token, generating + writing one if absent.
        // Cached for the editor session.
        public static string Get()
        {
            if (!string.IsNullOrEmpty(cached)) return cached;

            try
            {
                if (File.Exists(TokenPath))
                {
                    var existing = File.ReadAllText(TokenPath).Trim();
                    if (!string.IsNullOrEmpty(existing))
                    {
                        cached = existing;
                        return cached;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ione] failed to read bridge token at {TokenPath}: {e.Message}");
            }

            cached = Generate();
            return cached;
        }

        static string Generate()
        {
            var buf = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            // base64url, no padding — safe in headers and shells.
            var token = Convert.ToBase64String(buf)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            try
            {
                var dir = Path.GetDirectoryName(TokenPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(TokenPath, token);
                TryRestrictPerms(TokenPath);
                Debug.Log($"[ione] generated new bridge token at {TokenPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ione] failed to persist bridge token to {TokenPath}: {e.Message}");
            }
            return token;
        }

        static void TryRestrictPerms(string path)
        {
            // Windows: NTFS perms inherit user-only access from %USERPROFILE%.
            // Unix: chmod 600 explicitly so other users on a shared machine
            // can't read the token.
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"600 \"{path}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(2000);
                }
            }
            catch { /* best-effort */ }
        }
    }
}
