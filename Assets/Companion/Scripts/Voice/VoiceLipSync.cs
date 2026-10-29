using UnityEngine;

namespace Companion
{
    // Feeds the agent's voice to uLipSync (uLipSync + uLipSyncBlendShape on the character).
    // The uLipSyncAudioSource proxy reads the AudioSource's output in OnAudioFilterRead, so Attach()
    // re-adds it as the last component, after StreamingVoicePlayer.
    public class VoiceLipSync : MonoBehaviour
    {
        [SerializeField] uLipSync.uLipSync lipSync;

        public uLipSync.uLipSync LipSync => lipSync;

        public void Attach(AudioSource voice)
        {
            if (lipSync == null || voice == null) return;
            var old = voice.GetComponent<uLipSync.uLipSyncAudioSource>();
            if (old != null) Destroy(old);
            lipSync.audioSourceProxy = voice.gameObject.AddComponent<uLipSync.uLipSyncAudioSource>();
        }
    }
}
