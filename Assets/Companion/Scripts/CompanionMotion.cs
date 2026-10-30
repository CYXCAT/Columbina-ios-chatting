using UnityEngine;

namespace Companion
{
    // Drives Shaonv_Companion.controller (built by Companion/Build Animator):
    //  Speaking — true while the reply is streaming or the voice is audible.
    //  Idle     — 1..IdleCount plays a weighted random idle after idleDelay seconds without activity;
    //             0 ends it. Sing (1) lasts as long as the song, which starts once the Sing loop is
    //             reached and fades out when the idle is interrupted.
    public class CompanionMotion : MonoBehaviour
    {
        const int IdleCount = 4;
        const int SingIdle = 1;
        const float SingLoopTimeout = 10f;
        // uLipSync still reports the song for a frame or two after it stops.
        const float SongTail = 0.3f;
        static readonly int SpeakingParam = Animator.StringToHash("Speaking");
        static readonly int IdleParam = Animator.StringToHash("Idle");
        static readonly int RestState = Animator.StringToHash("Rest");
        static readonly int SingLoopState = Animator.StringToHash("Sing_Loop");

        [SerializeField] Animator animator;
        [SerializeField] CompanionApp app;
        [SerializeField] CompanionSinger singer;
        [Tooltip("Voice activity source (the character's uLipSync).")]
        [SerializeField] uLipSync.uLipSync lipSync;
        [SerializeField] float voiceThreshold = 0.1f;
        [Tooltip("Speaking stays on this long after the last reply chunk or audible voice frame.")]
        [SerializeField] float speechHold = 0.8f;
        [SerializeField] float idleDelay = 15f;
        [SerializeField] Vector2 idleDuration = new Vector2(12f, 20f);
        [Tooltip("Relative chance of each idle: Sing, SitFloor, SitLookAround, FlyWitch.")]
        [SerializeField] float[] idleWeights = { 1, 1, 1, 1 };

        IChatService chat;
        float lastSpeech = float.NegativeInfinity, lastActivity, idleStarted, idleEnds, songTailEnds;
        int idle, lastIdle;
        bool songStarted;

        // Off during a voice conversation: the echo canceller can't take the song out of the microphone.
        public bool AllowSinging { get; set; } = true;

        bool CanSing => AllowSinging && singer != null && singer.HasSongs;

        void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
        }

        void OnEnable()
        {
            lastActivity = Time.time;
            if (app == null) return;
            app.ChatReady += Bind;
            if (app.Chat != null) Bind(app.Chat);
        }

        void OnDisable()
        {
            if (app != null) app.ChatReady -= Bind;
            Bind(null);
        }

        void Bind(IChatService service)
        {
            if (chat != null)
            {
                chat.ReplyUpdated -= OnReply;
                chat.AgentVoiceVolume -= OnAgentVolume;
            }
            chat = service;
            if (chat != null)
            {
                chat.ReplyUpdated += OnReply;
                chat.AgentVoiceVolume += OnAgentVolume;
            }
        }

        void OnReply(string _) => lastSpeech = Time.time;

        void OnAgentVolume(float vol) { if (vol > voiceThreshold) lastSpeech = Time.time; }

        // User input (typing, sending, talking, tapping her) postpones and interrupts idles.
        public void NotifyActivity()
        {
            lastActivity = Time.time;
            EndIdle();
        }

        // Starts a random idle right away (menu "Change pose"), never the previous one.
        public void PlayRandomIdle()
        {
            if (Speaking) return;
            int next = PickIdle();
            if (next == 0) return;
            StopSong();
            idle = lastIdle = next;
            idleStarted = Time.time;
            idleEnds = idle == SingIdle ? float.PositiveInfinity : Time.time + Random.Range(idleDuration.x, idleDuration.y);
            songStarted = false;
            animator.SetInteger(IdleParam, idle);
        }

        int PickIdle()
        {
            float total = 0;
            for (int n = 1; n <= IdleCount; n++) total += Weight(n);
            float r = Random.value * total;
            for (int n = 1; n <= IdleCount; n++)
            {
                float w = Weight(n);
                if (w <= 0) continue;
                if (r < w) return n;
                r -= w;
            }
            return 0;
        }

        float Weight(int n)
        {
            if (n == lastIdle || (n == SingIdle && !CanSing)) return 0;
            return idleWeights != null && n - 1 < idleWeights.Length ? Mathf.Max(0, idleWeights[n - 1]) : 1;
        }

        bool Speaking => Time.time - lastSpeech < speechHold;

        void Update()
        {
            // While she sings the mouth follows the song, so only the reply itself counts as speech.
            if (singer != null && singer.IsActive) songTailEnds = Time.time + SongTail;
            if (Time.time >= songTailEnds && lipSync != null && lipSync.result.volume > voiceThreshold) lastSpeech = Time.time;
            bool speaking = Speaking;
            animator.SetBool(SpeakingParam, speaking);

            if (speaking)
            {
                NotifyActivity();
                return;
            }
            if (idle == SingIdle) UpdateSinging();
            if (idle > 0)
            {
                if (Time.time >= idleEnds)
                {
                    EndIdle();
                    lastActivity = Time.time;
                }
            }
            else if (Time.time - lastActivity >= idleDelay && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == RestState
                     && !animator.IsInTransition(0))
            {
                PlayRandomIdle();
            }
        }

        // The song starts once she is in the singing pose and the idle ends with the song.
        void UpdateSinging()
        {
            if (!AllowSinging) idleEnds = Time.time;
            else if (songStarted)
            {
                if (!singer.IsActive) idleEnds = Time.time;
            }
            else if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == SingLoopState)
            {
                singer.Play();
                songStarted = true;
            }
            else if (Time.time - idleStarted > SingLoopTimeout) idleEnds = Time.time;
        }

        void EndIdle()
        {
            if (idle == 0) return;
            idle = 0;
            StopSong();
            animator.SetInteger(IdleParam, 0);
        }

        void StopSong()
        {
            if (singer != null) singer.FadeOut();
            songStarted = false;
        }
    }
}
