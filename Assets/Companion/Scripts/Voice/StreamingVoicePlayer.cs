using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Companion
{
    // Plays streamed 16-bit mono PCM. Mark() runs a callback when playback reaches the current end of
    // the stream, which keeps subtitles in step with speech.
    //
    // On iOS the voice goes through the native audio engine (CompanionVoice.swift): its echo canceller
    // must hear her voice for the user to be able to talk over her. The played samples are fed to
    // uLipSync from here. Elsewhere a looping streamed AudioClip on this AudioSource pulls samples from
    // the buffer on Unity's audio thread, and uLipSync listens to the AudioSource (VoiceLipSync.Attach).
    [RequireComponent(typeof(AudioSource))]
    public class StreamingVoicePlayer : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        const bool Native = true;
        [DllImport("__Internal")] static extern void _CompanionVoicePlay(byte[] pcm, int length, int sampleRate);
        [DllImport("__Internal")] static extern void _CompanionVoiceStopPlayback();
        [DllImport("__Internal")] static extern long _CompanionVoicePlayedSamples();
#else
        const bool Native = false;
        static void _CompanionVoicePlay(byte[] pcm, int length, int sampleRate) { }
        static void _CompanionVoiceStopPlayback() { }
        static long _CompanionVoicePlayedSamples() => 0;
#endif

        const float VolumeInterval = 0.1f;

        AudioSource source;
        readonly object gate = new();
        float[] ring = new float[1 << 20];
        long written, played, fed; // in samples since the last Clear()
        float level;
        readonly Queue<(long at, Action action)> markers = new();
        readonly List<Action> due = new();
        float[] lipBuffer = new float[4096];
        int sampleRate = 24000;
        float nextVolume;

        // Fed directly on iOS, where the voice doesn't pass through the AudioSource.
        public uLipSync.uLipSync LipSync { get; set; }

        // Agent voice level [0,1], about 10 Hz, on the main thread.
        public event Action<float> Volume;

        public static bool PlaysNatively => Native;

        void Awake() => source = GetComponent<AudioSource>();

        public void Begin(int rate)
        {
            if (Native)
            {
                sampleRate = rate;
                return;
            }
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
            if (Native) _CompanionVoicePlay(pcm, pcm.Length, sampleRate);
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
                written = played = fed = 0;
                markers.Clear();
            }
            if (Native) _CompanionVoiceStopPlayback();
        }

        void Grow(long needed)
        {
            int size = ring.Length;
            while (size < needed) size *= 2;
            var bigger = new float[size];
            for (long i = played; i < written; i++) bigger[i & (size - 1)] = ring[i & (ring.Length - 1)];
            ring = bigger;
        }

        // Audio thread (AudioClip path only).
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
            if (Native) FeedNativeProgress();

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

        // Advances `played` to the native engine's position and gives uLipSync the audio heard since
        // the last frame, resampled to Unity's output rate (what uLipSync assumes), padded with silence
        // so the mouth closes when she stops.
        void FeedNativeProgress()
        {
            int outRate = AudioSettings.outputSampleRate;
            int frameSamples = Mathf.Clamp(Mathf.RoundToInt(Time.unscaledDeltaTime * outRate), 0, 8192);
            lock (gate)
            {
                played = Math.Min(written, Math.Max(played, _CompanionVoicePlayedSamples()));
                int m = (int)(played - fed);
                int count = m > 0 ? (int)((long)m * outRate / sampleRate) : 0;
                int total = Math.Max(count, frameSamples);
                if (lipBuffer.Length < total) lipBuffer = new float[total];

                int mask = ring.Length - 1;
                float step = (float)sampleRate / outRate, sum = 0;
                for (int j = 0; j < count; j++)
                {
                    float pos = j * step;
                    int i = (int)pos;
                    float a = ring[(fed + i) & mask];
                    float b = i + 1 < m ? ring[(fed + i + 1) & mask] : a;
                    float s = a + (b - a) * (pos - i);
                    lipBuffer[j] = s;
                    sum += s * s;
                }
                Array.Clear(lipBuffer, count, total - count);
                level = count > 0 ? Mathf.Sqrt(sum / count) : 0;
                fed = played;
                if (LipSync != null && total > 0)
                {
                    var data = lipBuffer.Length == total ? lipBuffer : lipBuffer[..total];
                    LipSync.OnDataReceived(data, 1);
                }
            }
        }
    }
}
