using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Companion
{
    [Serializable] public class ApiUser { public string id; public string name; public string language; }
    [Serializable] public class AuthResponse { public string accessToken; public string refreshToken; public int expiresIn; public ApiUser user; }
    [Serializable] public class LiveKitTokenResponse { public string serverUrl; public string token; public string roomName; }

    public class ApiException : Exception
    {
        public readonly long Status;
        public ApiException(long status, string message) : base(message) => Status = status;
    }

    // HTTP client for the companion web backend (web/ in companion-backend).
    // Holds the short-lived access token and transparently refreshes it once on 401.
    public class CompanionApi
    {
        readonly string baseUrl;
        string accessToken;

        public ApiUser User { get; private set; }
        public bool HasSession => !string.IsNullOrEmpty(accessToken) || !string.IsNullOrEmpty(SessionStore.RefreshToken);

        public CompanionApi(string baseUrl) => this.baseUrl = baseUrl.TrimEnd('/');

        // ---- auth
        [Serializable] class DevLoginBody { public string name; }
        [Serializable] class IdTokenBody { public string idToken; public string nonce; public string name; }
        [Serializable] class RefreshBody { public string refreshToken; }
        [Serializable] class LanguageBody { public string language; }

        public Awaitable SignInDevAsync(string name) => Authenticate("/api/auth/dev", new DevLoginBody { name = name });

        public Awaitable SignInGoogleAsync(string idToken) => Authenticate("/api/auth/google", new IdTokenBody { idToken = idToken });

        public Awaitable SignInAppleAsync(string identityToken, string rawNonce, string name) =>
            Authenticate("/api/auth/apple", new IdTokenBody { idToken = identityToken, nonce = rawNonce, name = name });

        public async Awaitable<bool> TryRestoreAsync()
        {
            if (string.IsNullOrEmpty(SessionStore.RefreshToken)) return false;
            try { await RefreshAsync(); return true; }
            catch (ApiException e) when (e.Status == 401) { SignOut(); return false; }
        }

        public void SignOut()
        {
            accessToken = null;
            User = null;
            SessionStore.RefreshToken = null;
        }

        async Awaitable RefreshAsync() =>
            Apply(await Send<AuthResponse>("POST", "/api/auth/refresh", new RefreshBody { refreshToken = SessionStore.RefreshToken }, false));

        async Awaitable Authenticate(string path, object body) => Apply(await Send<AuthResponse>("POST", path, body, false));

        void Apply(AuthResponse r)
        {
            accessToken = r.accessToken;
            SessionStore.RefreshToken = r.refreshToken;
            User = r.user;
        }

        // ---- app
        public Awaitable<LiveKitTokenResponse> GetLiveKitTokenAsync() => Authorized<LiveKitTokenResponse>("POST", "/api/livekit/token", null);

        public async Awaitable SetLanguageAsync(string language)
        {
            User = await Authorized<ApiUser>("PATCH", "/api/me", new LanguageBody { language = language });
        }

        async Awaitable<T> Authorized<T>(string method, string path, object body)
        {
            if (string.IsNullOrEmpty(accessToken)) await RefreshAsync();
            try { return await Send<T>(method, path, body, true); }
            catch (ApiException e) when (e.Status == 401)
            {
                await RefreshAsync();
                return await Send<T>(method, path, body, true);
            }
        }

        async Awaitable<T> Send<T>(string method, string path, object body, bool authed)
        {
            using var req = new UnityWebRequest(baseUrl + path, method);
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            if (authed) req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            req.timeout = 15;

            await req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                throw new ApiException(req.responseCode, $"{method} {path} failed: {req.responseCode} {req.error} {req.downloadHandler.text}");
            return JsonUtility.FromJson<T>(req.downloadHandler.text);
        }
    }
}
