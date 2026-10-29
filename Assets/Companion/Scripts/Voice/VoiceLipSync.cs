using UnityEngine;

namespace Companion
{
    // Feeds the agent's voice to uLipSync (uLipSync + uLipSyncBlendShape on the character).
    // LiveKit's AudioStream writes the audio in its AudioProbe's OnAudioFilterRead, so the
    // uLipSyncAudioSource proxy must sit after that probe in the filter chain: Attach() is called
    // each time a new stream is created and re-adds the proxy as the last component.
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
