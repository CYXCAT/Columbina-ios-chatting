using System;
using System.Threading;
using UnityEngine;

namespace Companion
{
    // The app keeps its local iOS speech recognizer and posts the final text to /api/chat, which
    // streams the reply back sentence by sentence: the text, then that sentence's PCM audio
    // (companion-backend/web/src/app/api/chat/route.ts). Subtitles are released as playback reaches
    // each sentence.
    public class HttpChatService : IChatService
    {
        [Serializable]
        class ChatEvent
        {
            public string t;
            public string text;
            public string pcm;
            public int sampleRate;
            public string message;
        }

        [Serializable] class MessageBody { public string text; }
        [Serializable] class GreetingBody { public bool greeting = true; }

        readonly CompanionApi api;
        readonly StreamingVoicePlayer voice;
        CancellationTokenSource turn;
        bool connected;

        public event Action<string> ReplyUpdated;
        public event Action<string> ReplyCompleted;
        public event Action<string> Error;
        public event Action<float> AgentVoiceVolume;

        public bool IsConnected => connected;

        public HttpChatService(CompanionApi api, StreamingVoicePlayer voice)
        {
            this.api = api;
            this.voice = voice;
            voice.Volume += OnVolume;
        }

        // There is no server-side session: "connecting" syncs the reply language and says hello.
        public async Awaitable ConnectAsync(string language)
        {
            Cancel();
            connected = false;
            if (api.User == null || api.User.language != language) await api.SetLanguageAsync(language);
            connected = true;
            Greet();
        }

        async void Greet()
        {
            try { await Run(new GreetingBody()); }
            catch (Exception e) { Debug.LogWarning("Greeting failed: " + e.Message); }
        }

        public async Awaitable DisconnectAsync()
        {
            Cancel();
            connected = false;
            await Awaitable.NextFrameAsync();
        }

        public async Awaitable SendAsync(string message)
        {
            if (!connected) throw new InvalidOperationException("not connected");
            await Run(new MessageBody { text = message });
        }

        // Stops her mid-sentence: aborts the request (which stops generation server-side) and the audio.
        public void Cancel()
        {
            turn?.Cancel();
            turn = null;
            voice.Clear();
        }

        public void Reset() => Cancel();

        public void Dispose()
        {
            Cancel();
            voice.Volume -= OnVolume;
        }

        async Awaitable Run(object body)
        {
            Cancel();
            var cts = turn = new CancellationTokenSource();
            var token = cts.Token;
            try
            {
                await api.ChatStreamAsync(body, line => OnLine(line, token), token);
            }
            catch (OperationCanceledException)
            {
                // Interrupted by Cancel() or a newer turn.
            }
            finally
            {
                if (turn == cts) turn = null;
                cts.Dispose();
            }
        }

        void OnLine(string line, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            var e = JsonUtility.FromJson<ChatEvent>(line);
            switch (e?.t)
            {
                case "start":
                    voice.Begin(e.sampleRate);
                    break;
                case "text":
                    voice.Mark(() => ReplyUpdated?.Invoke(e.text));
                    break;
                case "audio":
                    voice.Enqueue(Convert.FromBase64String(e.pcm));
                    break;
                case "error":
                    Debug.LogWarning("Chat: " + e.message);
                    break;
                case "done":
                    if (string.IsNullOrEmpty(e.text)) Error?.Invoke("empty reply");
                    else voice.Mark(() => ReplyCompleted?.Invoke(e.text));
                    break;
            }
        }

        void OnVolume(float level) => AgentVoiceVolume?.Invoke(level);
    }
}
