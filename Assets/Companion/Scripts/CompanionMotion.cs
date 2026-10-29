using UnityEngine;

namespace Companion
{
    // Drives Shaonv_Companion.controller (built by Companion/Build Animator):
    //  Speaking — true while the reply is streaming or the voice is audible.
    //  Idle     — 1..IdleCount plays a random idle after idleDelay seconds without activity; 0 ends it.
    public class CompanionMotion : MonoBehaviour
    {
        const int IdleCount = 4;
        static readonly int SpeakingParam = Animator.StringToHash("Speaking");
        static readonly int IdleParam = Animator.StringToHash("Idle");
        static readonly int RestState = Animator.StringToHash("Rest");

        [SerializeField] Animator animator;
        [SerializeField] CompanionApp app;
        [Tooltip("Voice activity source (the character's uLipSync).")]
        [SerializeField] uLipSync.uLipSync lipSync;
        [SerializeField] float voiceThreshold = 0.1f;
        [Tooltip("Speaking stays on this long after the last reply chunk or audible voice frame.")]
        [SerializeField] float speechHold = 0.8f;
        [SerializeField] float idleDelay = 15f;
        [SerializeField] Vector2 idleDuration = new Vector2(12f, 20f);

        IChatService chat;
        float lastSpeech = float.NegativeInfinity, lastActivity, idleEnds;
        int idle, lastIdle;

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

        // User input (typing, sending, push-to-talk) postpones and interrupts idles.
        public void NotifyActivity()
        {
            lastActivity = Time.time;
            EndIdle();
        }

        // Starts a random idle right away (menu "Change pose").
        public void PlayRandomIdle()
        {
            if (Speaking) return;
            int next = Random.Range(1, IdleCount + (lastIdle > 0 ? 0 : 1));
            if (lastIdle > 0 && next >= lastIdle) next++; // never repeat the previous idle
            idle = lastIdle = next;
            idleEnds = Time.time + Random.Range(idleDuration.x, idleDuration.y);
            animator.SetInteger(IdleParam, idle);
        }

        bool Speaking => Time.time - lastSpeech < speechHold;

        void Update()
        {
            if (lipSync != null && lipSync.result.volume > voiceThreshold) lastSpeech = Time.time;
            bool speaking = Speaking;
            animator.SetBool(SpeakingParam, speaking);

            if (speaking)
            {
                NotifyActivity();
                return;
            }
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

        void EndIdle()
        {
            if (idle == 0) return;
            idle = 0;
            animator.SetInteger(IdleParam, 0);
        }
    }
}
