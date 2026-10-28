using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Companion
{
    public enum SignInProvider { Apple, Google }

    [Serializable]
    public class NativeAuthResult
    {
        public bool ok;
        public string provider;
        public string token;   // Google id_token / Apple identityToken
        public string name;    // Apple only returns it on first sign-in
        public string error;
        [NonSerialized] public string rawNonce;
    }

    // Native side: Assets/Plugins/iOS/CompanionAuth.swift (AuthenticationServices + GoogleSignIn SDK).
    public static class NativeAuth
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _CompanionAppleSignIn(string hashedNonce);
        [DllImport("__Internal")] static extern void _CompanionGoogleSignIn();
#endif

        public static async Awaitable<NativeAuthResult> SignInAsync(SignInProvider provider)
        {
#if UNITY_IOS && !UNITY_EDITOR
            NativeBridge.Ensure();
            var done = new AwaitableCompletionSource<string>();
            void OnResult(string json) => done.TrySetResult(json);
            NativeBridge.AuthResult += OnResult;
            string rawNonce = null;
            try
            {
                if (provider == SignInProvider.Apple)
                {
                    rawNonce = RandomNonce();
                    _CompanionAppleSignIn(Sha256Hex(rawNonce));
                }
                else
                {
                    // Client IDs come from Info.plist (GIDClientID / GIDServerClientID), see IOSPostBuild.
                    _CompanionGoogleSignIn();
                }
                var result = JsonUtility.FromJson<NativeAuthResult>(await done.Awaitable);
                result.rawNonce = rawNonce;
                return result;
            }
            finally
            {
                NativeBridge.AuthResult -= OnResult;
            }
#else
            await Awaitable.NextFrameAsync();
            return new NativeAuthResult { ok = false, provider = provider.ToString(), error = "native sign-in is iOS-only" };
#endif
        }

        static string RandomNonce()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static string Sha256Hex(string s)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
