using UnityEngine;

namespace Companion
{
    // Feeds the character's uLipSync (uLipSync + uLipSyncBlendShape) from her voice, or from a song
    // while she sings (Listen/Release).
    // Voice: on iOS it is played natively and StreamingVoicePlayer feeds the played samples itself;
    // elsewhere the uLipSyncAudioSource proxy reads the AudioSource's output in OnAudioFilterRead, so
    // Attach() re-adds it as the last component, after StreamingVoicePlayer.
    public class VoiceLipSync : MonoBehaviour
    {
        [SerializeField] uLipSync.uLipSync lipSync;

        StreamingVoicePlayer voice;
        uLipSync.uLipSyncAudioSource voiceProxy;
        AudioSource song;
        float[] silence;

        public uLipSync.uLipSync LipSync => lipSync;

        public void Attach(StreamingVoicePlayer player)
        {
            if (lipSync == null || player == null) return;
            voice = player;
            if (StreamingVoicePlayer.PlaysNatively)
            {
                if (song == null) player.LipSync = lipSync;
                return;
            }
            var old = player.GetComponent<uLipSync.uLipSyncAudioSource>();
            if (old != null) Destroy(old);
            voiceProxy = player.gameObject.AddComponent<uLipSync.uLipSyncAudioSource>();
            if (song == null) lipSync.audioSourceProxy = voiceProxy;
        }

        // The song drives the mouth until Release().
        public void Listen(AudioSource source)
        {
            if (lipSync == null) return;
            song = source;
            if (voice != null) voice.LipSync = null;
            var proxy = source.GetComponent<uLipSync.uLipSyncAudioSource>();
            if (proxy == null) proxy = source.gameObject.AddComponent<uLipSync.uLipSyncAudioSource>();
            lipSync.audioSourceProxy = proxy;
        }

        public void Release(AudioSource source)
        {
            if (lipSync == null || song != source) return;
            song = null;
            lipSync.audioSourceProxy = voiceProxy;
            if (voice != null && StreamingVoicePlayer.PlaysNatively) voice.LipSync = lipSync;
            // uLipSync keeps analysing the last samples it got; flush the song out so the mouth closes
            // (and her voice isn't detected) even when nothing else feeds it.
            silence ??= new float[AudioSettings.outputSampleRate];
            lipSync.OnDataReceived(silence, 1);
        }
    }
}
