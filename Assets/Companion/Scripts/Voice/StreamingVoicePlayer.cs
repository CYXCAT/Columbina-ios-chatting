using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companion
{
    // Plays streamed 16-bit mono PCM through this AudioSource, so uLipSync (VoiceLipSync.Attach) hears
    // the voice. Mark() runs a callback when playback reaches the current end of the stream, which keeps
    // subtitles in step with speech.
    //
    // A looping streamed AudioClip pulls samples from a ring buffer on Unity's audio thread and plays
    // silence when the buffer is empty.
    [RequireComponent(typeof(AudioSource))]
    public class StreamingVoicePlayer : MonoBehaviour
    {
        const float VolumeInterval = 0.1f;

        AudioSource source;
        readonly object gate = new();
        float[] ring = new float[1 << 20];
        long written, played; // in samples since the player was created
        float level;
        readonly Queue<(long at, Action action)> markers = new();
        readonly List<Action> due = new();
        int sampleRate;
        float nextVolume;

        // Agent voice level [0,1], about 10 Hz, on the main thread.
        public event Action<float> Volume;

        public bool IsPlaying
        {
            get { lock (gate) return written > played; }
        }

        void Awake() => source = GetComponent<AudioSource>();

        public void Begin(int rate)
        {
            if (rate != sampleRate || source.clip == null)
            {
                sampleRate = rate;
                source.clip = AudioClip.Create("voice", rate, 1, rate, true, OnRead);
                source.loop = true;
            }
            if (!source.isPlaying) source.Play();
        }

        public void Enqueue(byte[] pcm)
        {
            int n = pcm.Length / 2;
            lock (gate)
            {
                if (written - played + n > ring.Length) Grow(written - played + n);
                int mask = ring.Length - 1;
                for (int i = 0; i < n; i++)
                    ring[(written + i) & mask] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
                written += n;
            }
        }

        public void Mark(Action action)
        {
            lock (gate) markers.Enqueue((written, action));
        }

        // Drops queued audio and pending marks (interruption).
        public void Clear()
        {
            lock (gate)
            {
                played = written;
                markers.Clear();
            }
        }

        void Grow(long needed)
        {
            int size = ring.Length;
            while (size < needed) size *= 2;
            var bigger = new float[size];
            for (long i = played; i < written; i++) bigger[i & (size - 1)] = ring[i & (ring.Length - 1)];
            ring = bigger;
        }

        // Audio thread.
        void OnRead(float[] data)
        {
            lock (gate)
            {
                int available = (int)Math.Min(written - played, data.Length);
                int mask = ring.Length - 1;
                float sum = 0;
                for (int i = 0; i < available; i++)
                {
                    float s = ring[(played + i) & mask];
                    data[i] = s;
                    sum += s * s;
                }
                Array.Clear(data, available, data.Length - available);
                played += available;
                level = Mathf.Sqrt(sum / data.Length);
            }
        }

        void Update()
        {
            lock (gate)
            {
                while (markers.Count > 0 && markers.Peek().at <= played) due.Add(markers.Dequeue().action);
            }
            foreach (var action in due) action();
            due.Clear();

            if (Time.unscaledTime >= nextVolume)
            {
                nextVolume = Time.unscaledTime + VolumeInterval;
                float rms;
                lock (gate) rms = level;
                // Speech RMS is roughly 0.02–0.25; scale it to the [0,1] range CompanionMotion expects.
                Volume?.Invoke(Mathf.Clamp01(rms * 4f));
            }
        }
    }
}
