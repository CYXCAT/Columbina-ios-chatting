using UnityEngine;

namespace Companion
{
    // Plays a song (a dry vocal, so it can drive the lip sync) for the Sing idle. CompanionMotion starts
    // it once the Sing loop is reached and fades it out when the idle is interrupted.
    [RequireComponent(typeof(AudioSource))]
    public class CompanionSinger : MonoBehaviour
    {
        [SerializeField] AudioClip[] songs;
        [SerializeField] VoiceLipSync lipSync;
        [SerializeField] float fadeOutTime = 0.6f;

        AudioSource source;
        float volume, fadeStart;
        bool playing, fading;

        public bool HasSongs => songs != null && songs.Length > 0;

        // True from Play() until the song has ended or faded out.
        public bool IsActive => playing;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            volume = source.volume;
        }

        public void Play()
        {
            if (!HasSongs) return;
            source.clip = songs[Random.Range(0, songs.Length)];
            source.volume = volume;
            source.Play();
            playing = true;
            fading = false;
            if (lipSync != null) lipSync.Listen(source);
        }

        public void FadeOut()
        {
            if (!playing || fading) return;
            fading = true;
            fadeStart = Time.unscaledTime;
        }

        void Update()
        {
            if (!playing) return;
            if (fading)
            {
                float t = (Time.unscaledTime - fadeStart) / fadeOutTime;
                source.volume = volume * (1 - t);
                if (t >= 1) Stop();
            }
            // Ended by itself (the clip doesn't loop); a paused app keeps isPlaying true.
            else if (!source.isPlaying) Stop();
        }

        void Stop()
        {
            source.Stop();
            source.volume = volume;
            playing = fading = false;
            if (lipSync != null) lipSync.Release(source);
        }

        void OnDisable()
        {
            if (playing) Stop();
        }
    }
}
