using System;
using Agora.Rtc;
using Agora.Rtm;
using UnityEngine;

namespace Companion
{
    // The app keeps its existing local iOS speech recognizer and sends the final text to the
    // backend. Agora Conversational AI generates the reply, RTC plays its voice, and RTM carries
    // the synchronized transcript used by the HUD.
    public class AgoraChatService : IChatService
    {
        [Serializable]
        class Transcript
        {
            // Agora ConvoAI RTM format: object = "assistant.transcription.turn", is_final
            public string @object;
            public bool is_final;
            // Legacy / fallback field names
            public string type;
            public string role;
            public bool final;
            // Common
            public string text;
        }

        readonly CompanionApi api;
        IRtcEngine rtc;
        IRtmClient rtm;
        RtcEvents rtcEvents;
        AgoraSessionResponse session;
        bool joined;

        public event Action<string> ReplyUpdated;
        public event Action<string> ReplyCompleted;
        public event Action<string> Error;
        // Fires on main thread with normalized agent volume [0,1]; use to drive lip sync.
        public event Action<float> AgentVoiceVolume;

        public bool IsConnected => joined && rtc != null && rtm != null && session != null;
        public IRtcEngine Rtc => rtc;
        public uint AgentUid => session?.agentUid ?? 0;

        public AgoraChatService(CompanionApi api) => this.api = api;

        public async Awaitable ConnectAsync(string language)
        {
            await DisconnectAsync();
            if (api.User == null || api.User.language != language) await api.SetLanguageAsync(language);

            session = await api.StartAgoraSessionAsync();
            SetupRtc();
            await SetupRtmAsync();

            var options = new ChannelMediaOptions();
            options.autoSubscribeAudio.SetValue(true);
            options.autoSubscribeVideo.SetValue(false);
            options.publishMicrophoneTrack.SetValue(false);
            options.enableAudioRecordingOrPlayout.SetValue(true);
            options.channelProfile.SetValue(CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_COMMUNICATION);
            options.clientRoleType.SetValue(CLIENT_ROLE_TYPE.CLIENT_ROLE_BROADCASTER);

            var result = rtc.JoinChannel(session.rtcToken, session.channel, session.uid, options);
            if (result != 0) throw new Exception("Agora RTC join failed: " + result);

            var deadline = Time.realtimeSinceStartup + 30f;
            while (!joined && Time.realtimeSinceStartup < deadline) await Awaitable.NextFrameAsync();
            if (!joined) throw new TimeoutException("Agora RTC connection timed out");
        }

        public async Awaitable SendAsync(string message)
        {
            if (!IsConnected) throw new InvalidOperationException("not connected");
            await api.SendAgoraMessageAsync(session.sessionToken, message);
        }

        public void Reset() { }

        public void Dispose() => Disconnect();

        void SetupRtc()
        {
            rtc = RtcEngine.CreateAgoraRtcEngine();
            rtc.Initialize(new RtcEngineContext
            {
                appId = session.appId,
                channelProfile = CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_COMMUNICATION,
                audioScenario = AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_AI_CLIENT,
            });
            rtcEvents = new RtcEvents(this);
            rtc.InitEventHandler(rtcEvents);
            rtc.EnableAudio();
            rtc.SetDefaultAudioRouteToSpeakerphone(true);
            rtc.EnableAudioVolumeIndication(100, 3, true);
        }

        async Awaitable SetupRtmAsync()
        {
            rtm = RtmClient.CreateAgoraRtmClient(new RtmConfig
            {
                appId = session.appId,
                userId = session.uid.ToString(),
                areaCode = RTM_AREA_CODE.NA,
                useStringUserId = true,
            });
            rtm.OnMessageEvent += OnRtmMessage;

            var login = await rtm.LoginAsync(session.rtmToken);
            await Awaitable.MainThreadAsync();
            if (login.Status.Error) throw new Exception("Agora RTM login failed: " + login.Status.Reason);

            var subscribe = await rtm.SubscribeAsync(session.channel, new SubscribeOptions());
            await Awaitable.MainThreadAsync();
            if (subscribe.Status.Error) throw new Exception("Agora RTM subscribe failed: " + subscribe.Status.Reason);
        }

        void OnRtmMessage(MessageEvent messageEvent)
        {
            try
            {
                var json = messageEvent.message?.GetData<string>();
                if (string.IsNullOrEmpty(json)) return;
                var line = JsonUtility.FromJson<Transcript>(json);
                if (line == null) return;

                bool isAssistant =
                    (!string.IsNullOrEmpty(line.@object) && line.@object.StartsWith("assistant.transcription"))
                    || (line.type == "transcript" && (line.role == "agent" || line.role == "assistant"));
                if (!isAssistant || string.IsNullOrEmpty(line.text)) return;

                ReplyUpdated?.Invoke(line.text);
                if (line.is_final || line.final) ReplyCompleted?.Invoke(line.text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Error?.Invoke("transcription failed");
            }
        }

        async Awaitable DisconnectAsync()
        {
            var oldSession = session;
            Disconnect();
            if (!string.IsNullOrEmpty(oldSession?.sessionToken))
            {
                try { await api.StopAgoraSessionAsync(oldSession.sessionToken); }
                catch (Exception e) { Debug.LogWarning("Agora stop failed: " + e.Message); }
            }
        }

        void Disconnect()
        {
            joined = false;
            if (rtm != null)
            {
                rtm.OnMessageEvent -= OnRtmMessage;
                _ = rtm.LogoutAsync();
                rtm.Dispose();
                rtm = null;
            }
            if (rtc != null)
            {
                rtc.LeaveChannel();
                rtc.Dispose(true);
                rtc = null;
            }
            rtcEvents = null;
            session = null;
        }

        sealed class RtcEvents : IRtcEngineEventHandler
        {
            readonly AgoraChatService owner;
            public RtcEvents(AgoraChatService owner) => this.owner = owner;

            public override void OnJoinChannelSuccess(RtcConnection connection, int elapsed) => owner.joined = true;

            public override void OnAudioVolumeIndication(RtcConnection connection, AudioVolumeInfo[] speakers, uint speakerNumber, int totalVolume)
            {
                if (owner.session == null) return;
                float vol = 0;
                for (int i = 0; i < speakerNumber; i++)
                    if (speakers[i].uid == owner.session.agentUid) { vol = speakers[i].volume / 255f; break; }
                owner.AgentVoiceVolume?.Invoke(vol);
            }

            public override void OnError(int err, string msg)
            {
                Debug.LogError($"Agora RTC error {err}: {msg}");
                owner.Error?.Invoke("voice connection failed");
            }
        }
    }
}
