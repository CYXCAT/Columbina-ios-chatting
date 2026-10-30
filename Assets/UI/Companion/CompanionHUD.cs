using UnityEngine;
using UnityEngine.UIElements;

namespace Companion
{
    [RequireComponent(typeof(UIDocument))]
    public class CompanionHUD : MonoBehaviour
    {
        [SerializeField] CompanionApp app;
        [SerializeField] CompanionMotion motion;
        [SerializeField] float dialogueHideDelay = 12f;
        [SerializeField] float replyTimeout = 20f;
        [Tooltip("Her voice can still turn up in the transcript this long after she stops talking.")]
        [SerializeField] float echoWindow = 3f;
        [Tooltip("An echo-like transcript this long is certainly her voice: drop it and listen afresh.")]
        [SerializeField] int echoDiscardTokens = 4;

        IChatService chat;
        ISpeechRecognizer speech;
        VisualElement root, reply;
        Label userLine, replyName, replyText, toast;
        TextField input;
        Button sendButton, voiceButton, languageEnglish, languageChinese;
        IVisualElementScheduledItem dots, hideDialogue, hideToast, timeout, pulse;
        bool waiting, conversing, userTurn;
        float lastAgentVoice = float.NegativeInfinity;

        void Awake()
        {
            if (motion == null) motion = FindAnyObjectByType<CompanionMotion>();
            if (app == null) app = FindAnyObjectByType<CompanionApp>();
            speech = SpeechRecognizers.Create();
        }

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement.Q("hud");
            reply = root.Q("reply");
            userLine = root.Q<Label>("userLine");
            replyName = root.Q<Label>("replyName");
            replyText = root.Q<Label>("replyText");
            toast = root.Q<Label>("toast");
            input = root.Q<TextField>("chatInput");
            sendButton = root.Q<Button>("sendButton");
            voiceButton = root.Q<Button>("voiceButton");
            languageEnglish = root.Q<Button>("languageEnglish");
            languageChinese = root.Q<Button>("languageChinese");

            root.Q<Button>("keyboardButton").clicked += OpenInput;
            root.Q<Button>("voiceModeButton").clicked += CloseInput;
            sendButton.clicked += SendFromInput;
            input.RegisterValueChangedCallback(_ => { RefreshSendButton(); NotifyActivity(); });
            input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);

            voiceButton.clicked += ToggleConversation;

            root.Q<Button>("menuButton").clicked += () => SetMenu(!root.ClassListContains("hud--menu-open"));
            root.Q("scrim").RegisterCallback<PointerDownEvent>(_ => SetMenu(false));
            root.Q<Button>("menuMotion").clicked += () => { SetMenu(false); if (motion != null) motion.PlayRandomIdle(); };
            root.Q<Button>("menuClear").clicked += () => { SetMenu(false); ClearDialogue(); };
            root.Q<Button>("menuSettings").clicked += () => { SetMenu(false); SetSettings(true); };
            root.Q<Button>("menuAbout").clicked += () => { SetMenu(false); ShowToast(L.Get("toast.about")); };

            root.Q("settingsBackdrop").RegisterCallback<PointerDownEvent>(_ => SetSettings(false));
            root.Q<Button>("settingsDone").clicked += () => SetSettings(false);
            languageEnglish.clicked += () => L.Current = Language.English;
            languageChinese.clicked += () => L.Current = Language.Chinese;

            root.Q<Button>("signinApple").clicked += () => SignIn(SignInProvider.Apple);
            root.Q<Button>("signinGoogle").clicked += () => SignIn(SignInProvider.Google);

            L.LanguageChanged += OnLanguageChanged;
            speech.SpeechStarted += OnSpeechStarted;
            speech.Partial += OnSpeechPartial;
            speech.Final += OnSpeechFinal;
            speech.Failed += OnSpeechFailed;
            if (app != null)
            {
                app.ChatReady += Bind;
                app.SignInRequiredChanged += SetSignIn;
                app.Notice += OnNotice;
                if (app.Chat != null) Bind(app.Chat);
            }
            Localize();
        }

        void OnDisable()
        {
            EndConversation();
            L.LanguageChanged -= OnLanguageChanged;
            speech.SpeechStarted -= OnSpeechStarted;
            speech.Partial -= OnSpeechPartial;
            speech.Final -= OnSpeechFinal;
            speech.Failed -= OnSpeechFailed;
            if (app != null)
            {
                app.ChatReady -= Bind;
                app.SignInRequiredChanged -= SetSignIn;
                app.Notice -= OnNotice;
            }
            Bind(null);
        }

        void OnDestroy() => speech.Dispose();

        void OnApplicationPause(bool pause)
        {
            if (pause) EndConversation();
        }

        // ---------- chat ----------

        void Bind(IChatService service)
        {
            if (chat != null)
            {
                chat.ReplyUpdated -= OnReplyUpdated;
                chat.ReplyCompleted -= OnReplyCompleted;
                chat.Error -= OnChatError;
                chat.AgentVoiceVolume -= OnAgentVoice;
            }
            chat = service;
            if (chat != null)
            {
                chat.ReplyUpdated += OnReplyUpdated;
                chat.ReplyCompleted += OnReplyCompleted;
                chat.Error += OnChatError;
                chat.AgentVoiceVolume += OnAgentVoice;
            }
        }

        void SendFromInput()
        {
            var text = input.value.Trim();
            if (text.Length == 0 || waiting) return;
            input.value = "";
            input.Focus();
            Submit(text);
        }

        async void Submit(string text)
        {
            if (chat == null || !chat.IsConnected)
            {
                userLine.RemoveFromClassList("user-line--visible");
                ShowToast(L.Get("toast.offline"));
                return;
            }

            NotifyActivity();
            hideDialogue?.Pause();
            userLine.text = text;
            userLine.AddToClassList("user-line--visible");
            SetWaiting(true);

            try { await chat.SendAsync(text); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                SetWaiting(false);
                ShowToast(L.Get("toast.offline"));
            }
        }

        void SetWaiting(bool value)
        {
            waiting = value;
            RefreshSendButton();
            dots?.Pause();
            timeout?.Pause();
            if (!value) return;

            int n = 0;
            reply.AddToClassList("reply--visible");
            dots = replyText.schedule.Execute(() => replyText.text = new string('·', n++ % 3 + 1)).Every(300);
            timeout = root.schedule.Execute(() =>
            {
                if (!waiting) return;
                SetWaiting(false);
                HideDialogue();
                ShowToast(L.Get("toast.offline"));
            }).StartingIn((long)(replyTimeout * 1000));
        }

        void OnReplyUpdated(string soFar)
        {
            if (waiting) SetWaiting(false);
            hideDialogue?.Pause();
            reply.AddToClassList("reply--visible");
            replyText.text = ForDisplay(soFar);
        }

        // Noto Sans SC draws curly quotes as full-width CJK punctuation, which leaves gaps in English text.
        static string ForDisplay(string text) =>
            L.Current == Language.English
                ? text.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
                : text;

        void OnReplyCompleted(string full)
        {
            OnReplyUpdated(full);
            hideDialogue = root.schedule.Execute(HideDialogue).StartingIn((long)(dialogueHideDelay * 1000));
        }

        void OnChatError(string message)
        {
            Debug.LogWarning("Chat error: " + message);
            SetWaiting(false);
            ShowToast(L.Get("toast.offline"));
        }

        void HideDialogue()
        {
            if (waiting) return;
            userLine.RemoveFromClassList("user-line--visible");
            reply.RemoveFromClassList("reply--visible");
        }

        void ClearDialogue()
        {
            hideDialogue?.Pause();
            SetWaiting(false);
            chat?.Reset();
            HideDialogue();
            ShowToast(L.Get("toast.cleared"));
        }

        // ---------- text input ----------

        void OpenInput()
        {
            EndConversation();
            NotifyActivity();
            root.AddToClassList("hud--typing");
            input.schedule.Execute(() => input.Focus()).StartingIn(50);
        }

        void CloseInput()
        {
            root.RemoveFromClassList("hud--typing");
            input.Blur();
        }

        void OnInputKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n')
            {
                SendFromInput();
                e.StopImmediatePropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseInput();
            }
        }

        void RefreshSendButton() =>
            sendButton.EnableInClassList("send-button--idle", waiting || string.IsNullOrWhiteSpace(input.value));

        // ---------- voice conversation ----------
        // Tap to start: she listens continuously, every pause in the user's speech ends a turn, and
        // talking over her interrupts her. Tap again to stop.

        void ToggleConversation()
        {
            if (conversing) EndConversation();
            else StartConversation();
        }

        void StartConversation()
        {
            if (!speech.IsAvailable)
            {
                ShowToast(L.Get("toast.voiceUnavailable"));
                return;
            }
            NotifyActivity();
            chat?.Cancel();
            SetWaiting(false);
            HideDialogue();
            conversing = true;
            voiceButton.AddToClassList("voice-button--active");
            pulse = voiceButton.schedule.Execute(() => voiceButton.ToggleInClassList("voice-button--pulse")).Every(700);
            ShowToast(L.Get("voice.listening"));
            speech.Start(L.SpeechLocale);
        }

        void EndConversation()
        {
            if (!conversing) return;
            conversing = false;
            userTurn = false;
            pulse?.Pause();
            voiceButton.RemoveFromClassList("voice-button--active");
            voiceButton.RemoveFromClassList("voice-button--pulse");
            speech.Stop();
            chat?.Cancel();
            SetWaiting(false);
            HideDialogue();
        }

        void OnAgentVoice(float level)
        {
            if (level > 0.02f) lastAgentVoice = Time.time;
        }

        // The echo canceller doesn't remove all of her voice from the microphone, so while she talks
        // (and just after) a transcript only counts as the user once it differs from what she says.
        bool HerVoiceMayBeInMic => Time.time - lastAgentVoice < echoWindow;

        bool IsEcho(string heard, out int tokens)
        {
            tokens = 0;
            return HerVoiceMayBeInMic && EchoFilter.IsEcho(heard, replyText.text, out tokens);
        }

        void OnSpeechStarted()
        {
            if (conversing && !HerVoiceMayBeInMic) BeginUserTurn();
        }

        // The user is talking: interrupt her, whether she is still thinking or already speaking.
        void BeginUserTurn()
        {
            userTurn = true;
            NotifyActivity();
            chat?.Cancel();
            SetWaiting(false);
            hideDialogue?.Pause();
            reply.RemoveFromClassList("reply--visible");
            userLine.text = "…";
            userLine.AddToClassList("user-line--visible");
        }

        void OnSpeechPartial(string text)
        {
            if (!conversing) return;
            if (!userTurn)
            {
                if (IsEcho(text, out int tokens))
                {
                    if (tokens >= echoDiscardTokens) speech.Discard();
                    return;
                }
                BeginUserTurn();
            }
            userLine.text = text;
        }

        void OnSpeechFinal(string text)
        {
            if (!conversing) return;
            bool turn = userTurn;
            userTurn = false;
            if (string.IsNullOrWhiteSpace(text) || (!turn && IsEcho(text, out _)))
            {
                if (turn) userLine.RemoveFromClassList("user-line--visible");
                return;
            }
            if (!turn) BeginUserTurn();
            Submit(text.Trim());
        }

        void OnSpeechFailed(string message)
        {
            Debug.LogWarning("Speech recognition: " + message);
            if (!conversing) return;
            EndConversation();
            ShowToast(L.Get(message == "permission denied" ? "toast.voicePermission" : "toast.voiceFailed"));
        }

        // ---------- menu / settings / sign-in ----------

        void SetMenu(bool open) => root.EnableInClassList("hud--menu-open", open);

        void SetSettings(bool open) => root.EnableInClassList("hud--settings-open", open);

        void SetSignIn(bool required) => root.EnableInClassList("hud--signin", required);

        async void SignIn(SignInProvider provider)
        {
            if (app != null) await app.SignInAsync(provider);
        }

        // Typing, sending and talking postpone and interrupt the character's idle animations.
        void NotifyActivity()
        {
            if (motion != null) motion.NotifyActivity();
        }

        void OnNotice(string key) => ShowToast(L.Get(key));

        void ShowToast(string message)
        {
            toast.text = message;
            toast.AddToClassList("toast--visible");
            hideToast?.Pause();
            hideToast = toast.schedule.Execute(() => toast.RemoveFromClassList("toast--visible")).StartingIn(1800);
        }

        // ---------- localization ----------

        void OnLanguageChanged()
        {
            EndConversation();
            SetWaiting(false);
            HideDialogue();
            Localize();
        }

        void Localize()
        {
            input.textEdition.placeholder = L.Get("input.placeholder");
            replyName.text = L.Get("character.name");
            root.Q<Label>("menuMotionLabel").text = L.Get("menu.motion");
            root.Q<Label>("menuClearLabel").text = L.Get("menu.clear");
            root.Q<Label>("menuSettingsLabel").text = L.Get("menu.settings");
            root.Q<Label>("menuAboutLabel").text = L.Get("menu.about");
            root.Q<Label>("settingsTitle").text = L.Get("settings.title");
            root.Q<Label>("settingsLanguageLabel").text = L.Get("settings.language");
            root.Q<Button>("settingsDone").text = L.Get("settings.done");
            root.Q<Label>("signinTitle").text = L.Get("signin.title");
            root.Q<Button>("signinApple").text = L.Get("signin.apple");
            root.Q<Button>("signinGoogle").text = L.Get("signin.google");
            languageEnglish.EnableInClassList("segmented__option--selected", L.Current == Language.English);
            languageChinese.EnableInClassList("segmented__option--selected", L.Current == Language.Chinese);
        }
    }
}
