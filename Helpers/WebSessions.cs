using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace InventorySystem.Helpers
{
    /// <summary>
    /// Login tokens for the web app (PC window and phones). Issued on login and required on every
    /// data request, so a device on the same Wi-Fi can't read data without signing in.
    /// Kept in memory: restarting the app signs everyone out once.
    /// </summary>
    public static class WebSessions
    {
        private static readonly ConcurrentDictionary<string, string> Tokens = new();

        public static string Issue(string username)
        {
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Tokens[token] = username ?? "";
            return token;
        }

        public static bool IsValid(string token) =>
            !string.IsNullOrWhiteSpace(token) && Tokens.ContainsKey(token.Trim());

        public static void Revoke(string token)
        {
            if (!string.IsNullOrWhiteSpace(token)) Tokens.TryRemove(token.Trim(), out _);
        }

        /// <summary>Addresses that must work before login (sign-in screen, branding, licence wall).</summary>
        public static bool IsPublicPath(string path)
        {
            path = (path ?? "").ToLowerInvariant();
            return path == "/api/login"
                || path == "/api/logout"
                || path == "/api/brand"
                || path.StartsWith("/api/license");
        }
    }
}
