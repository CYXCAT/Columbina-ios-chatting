using System;
using System.Text;
using LiveKit;
using UnityEngine;

namespace Companion
{
    // Talks to the LiveKit agent (companion-backend/agent): text goes out on lk.chat, the agent's
    // TTS-synced transcript comes back on lk.transcription (one text stream per reply segment,
    // incremental chunks, end-of-stream = segment complete) and its voice as a remote audio track.
    public class LiveKitChatService : IChatService
    {
        const string ChatTopic = "lk.chat";
        const string TranscriptionTopic = "lk.transcription";

        readonly CompanionApi api;
        readonly AudioSource output;
        Room room;
        AudioStream audioStream;

        public event Action<string> ReplyUpdated;
        public event Action<string> ReplyCompleted;
        public event Action<string> Error;
        // Raised after the agent's audio track is attached to the output AudioSource.
        public event Action<AudioSource> VoiceStreamStarted;

        public bool IsConnected => room != null && room.IsConnected;

        public LiveKitChatService(CompanionApi api, AudioSource output)
        {
            this.api = api;
            this.output = output;
        }

        public async Awaitable ConnectAsync(string language)
        {
            Disconnect();
            if (api.User == null || api.User.language != language) await api.SetLanguageAsync(language);
            var t = await api.GetLiveKitTokenAsync();

            var r = new Room();
            r.TrackSubscribed += OnTrackSubscribed;
            r.RegisterTextStreamHandler(TranscriptionTopic, OnTranscription);
            room = r;

            var connect = r.Connect(t.serverUrl, t.token, new RoomOptions());
            await connect;
            await Awaitable.MainThreadAsync();
            if (connect.IsError)
            {
                if (room == r) Disconnect();
                throw new Exception("LiveKit connection failed");
            }
        }

        public async Awaitable SendAsync(string message)
        {
            if (!IsConnected) throw new InvalidOperationException("not connected");
            var send = room.LocalParticipant.SendText(message, ChatTopic);
            await send;
            await Awaitable.MainThreadAsync();
            if (send.IsError) Error?.Invoke("send failed");
        }

        // Clearing only affects the on-screen dialogue; server-side history is kept.
        public void Reset() { }

        public void Dispose() => Disconnect();

        void Disconnect()
        {
            audioStream?.Dispose();
            audioStream = null;
            if (room == null) return;
            room.TrackSubscribed -= OnTrackSubscribed;
            room.UnregisterTextStreamHandler(TranscriptionTopic);
            room.Disconnect();
            room = null;
        }

        void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is not RemoteAudioTrack audio) return;
            audioStream?.Dispose();
            audioStream = new AudioStream(audio, output);
            VoiceStreamStarted?.Invoke(output);
        }

        void OnTranscription(TextStreamReader reader, string identity)
        {
            if (room?.LocalParticipant != null && identity == room.LocalParticipant.Identity)
            {
                reader.Dispose();
                return;
            }
            ReadSegment(reader, room);
        }

        async void ReadSegment(TextStreamReader reader, Room owner)
        {
            var text = new StringBuilder();
            try
            {
                var chunks = reader.ReadIncremental();
                while (true)
                {
                    await chunks;
                    await Awaitable.MainThreadAsync();
                    if (chunks.IsEos || owner != room) break;
                    text.Append(chunks.Text);
                    chunks.Reset();
                    ReplyUpdated?.Invoke(text.ToString());
                }
                if (owner == room && text.Length > 0) ReplyCompleted?.Invoke(text.ToString());
            }
            catch (Exception e)
            {
                await Awaitable.MainThreadAsync();
                Debug.LogException(e);
                Error?.Invoke("transcription failed");
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}
