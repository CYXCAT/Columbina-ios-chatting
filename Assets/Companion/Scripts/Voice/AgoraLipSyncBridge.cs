using System.Collections.Concurrent;
using Agora.Rtc;
using UnityEngine;

namespace Companion
{
    // Feeds the agent's pre-mix PCM audio to uLipSync.OnDataReceived for phoneme-based lip sync.
    //
    // The Agora BEFORE_MIXING callback runs synchronously on Agora's native audio decode thread.
    // Doing any non-trivial work there delays audio output.  So the callback just converts bytes→floats
    // and enqueues; Update() drains the queue and calls OnDataReceived on the main thread, keeping
    // Agora's audio thread free.
    public class AgoraLipSyncBridge : MonoBehaviour
    {
        // Cap the queue to ~0.2 s worth of frames (at 100 frames/s = 20 frames).
        const int kMaxQueued = 20;

        IRtcEngine rtc;
        uLipSync.uLipSync lipSync;
        readonly ConcurrentQueue<(float[] buf, int channels)> frameQueue = new();

        public void Attach(IRtcEngine engine, uint agentUid, uLipSync.uLipSync ls)
        {
            rtc?.UnRegisterAudioFrameObserver();
            rtc = engine;
            lipSync = ls;
            engine.RegisterAudioFrameObserver(
                new Observer(agentUid, frameQueue),
                AUDIO_FRAME_POSITION.AUDIO_FRAME_POSITION_BEFORE_MIXING,
                OBSERVER_MODE.RAW_DATA);
        }

        void Update()
        {
            if (lipSync == null) return;
            while (frameQueue.TryDequeue(out var f))
                lipSync.OnDataReceived(f.buf, f.channels);
        }

        void OnDestroy()
        {
            rtc?.UnRegisterAudioFrameObserver();
        }

        sealed class Observer : IAudioFrameObserver
        {
            readonly uint agentUid;
            readonly ConcurrentQueue<(float[], int)> queue;

            public Observer(uint uid, ConcurrentQueue<(float[], int)> q)
            {
                agentUid = uid;
                queue = q;
            }

            public override bool OnPlaybackAudioFrameBeforeMixing(string channelId, uint uid, AudioFrame f)
            {
                if (uid != agentUid || f?.RawBuffer == null || f.RawBuffer.Length < 2)
                    return true;
                if (queue.Count >= kMaxQueued)
                    return true; // drop frames if Update() is falling behind; audio still plays

                int bps = (int)f.bytesPerSample;
                int total = f.samplesPerChannel * f.channels;
                var buf = new float[total];
                for (int i = 0; i < total; i++)
                {
                    int off = i * bps;
                    if (off + 1 >= f.RawBuffer.Length) break;
                    buf[i] = (short)(f.RawBuffer[off] | (f.RawBuffer[off + 1] << 8)) / 32768f;
                }
                queue.Enqueue((buf, f.channels));
                return true; // return immediately so Agora can mix and play without blocking
            }
        }
    }
}
