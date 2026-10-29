using System;
using UnityEngine;

namespace Companion
{
    // Owns the session: sign-in, the active chat backend, and reconnecting when the language changes.
    // CompanionHUD only renders and forwards user input.
    public class CompanionApp : MonoBehaviour
    {
        [SerializeField] CompanionConfig config;
        [Tooltip("Offline UI work: canned replies, no backend.")]
        [SerializeField] bool useMock;
        [SerializeField] string devUserName = "Unity Editor";
        [SerializeField] AudioSource voiceOutput;
        [SerializeField] VoiceLipSync lipSync;

        CompanionApi api;
        bool paused;

        public IChatService Chat { get; private set; }
        public event Action<IChatService> ChatReady;
        public event Action<bool> SignInRequiredChanged;
        public event Action<string> Notice; // localization key

        async void Start()
        {
            NativeBridge.Ensure();
            L.LanguageChanged += OnLanguageChanged;

            if (useMock)
            {
                await UseChat(new MockChatService());
                return;
            }

            api = new CompanionApi(config.apiBaseUrl);
            try
            {
                if (await api.TryRestoreAsync()) await AfterSignIn();
                else if (Application.platform == RuntimePlatform.IPhonePlayer) SignInRequiredChanged?.Invoke(true);
                else
                {
                    await api.SignInDevAsync(devUserName);
                    await AfterSignIn();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notice?.Invoke("toast.offline");
            }
        }

        public async Awaitable<bool> SignInAsync(SignInProvider provider)
        {
            try
            {
                var r = await NativeAuth.SignInAsync(provider);
                if (!r.ok) throw new Exception(r.error);
                if (provider == SignInProvider.Apple) await api.SignInAppleAsync(r.token, r.rawNonce, r.name);
                else await api.SignInGoogleAsync(r.token);
                SignInRequiredChanged?.Invoke(false);
                await AfterSignIn();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notice?.Invoke("signin.failed");
                return false;
            }
        }

        async Awaitable AfterSignIn()
        {
            // A fresh install adopts the language stored on the account.
            if (!L.HasSavedPreference && api.User?.language == "zh") L.Current = Language.Chinese;
            var chat = new AgoraChatService(api);
            await UseChat(chat);
        }

        async Awaitable UseChat(IChatService chat)
        {
            Chat?.Dispose();
            Chat = chat;
            ChatReady?.Invoke(chat);
            await Connect();
        }

        async Awaitable Connect()
        {
            try
            {
                await Chat.ConnectAsync(L.Code);
                // The app went to the background while connecting: don't keep the agent running.
                if (paused)
                {
                    await Chat.DisconnectAsync();
                    return;
                }
                WireAgentLipSync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notice?.Invoke("toast.offline");
            }
        }

        void WireAgentLipSync()
        {
            if (Chat is not AgoraChatService agora) return;
            if (lipSync?.LipSync == null) return;
            var bridge = gameObject.GetComponent<AgoraLipSyncBridge>()
                ?? gameObject.AddComponent<AgoraLipSyncBridge>();
            bridge.Attach(agora.Rtc, agora.AgentUid, lipSync.LipSync);
        }

        async void OnLanguageChanged()
        {
            if (Chat != null) await Connect();
        }

        // ConvoAI bills while the agent is in the channel, so leave it in the background and start a
        // fresh session when the app returns.
        async void OnApplicationPause(bool pause)
        {
            paused = pause;
            if (Chat == null) return;
            try
            {
                if (pause) await Chat.DisconnectAsync();
                else if (!Chat.IsConnected) await Connect();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OnDestroy()
        {
            L.LanguageChanged -= OnLanguageChanged;
            Chat?.Dispose();
        }
    }
}
