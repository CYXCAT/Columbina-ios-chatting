using System;
using System.Runtime.InteropServices;

namespace Companion
{
    // Hands-free listening for a voice conversation: Start() listens until Stop(), and each pause in
    // the user's speech ends one utterance. Events are raised on the main thread.
    public interface ISpeechRecognizer : IDisposable
    {
        bool IsAvailable { get; }
        event Action SpeechStarted;   // the user started talking (time to interrupt her)
        event Action<string> Partial; // the utterance so far
        event Action<string> Final;   // a finished utterance
        event Action<string> Failed;  // listening stopped on its own
        void Start(string locale);
        void Stop();
        // Forgets the utterance in progress (it was her own voice) and keeps listening.
        void Discard();
    }

    public static class SpeechRecognizers
    {
        public static ISpeechRecognizer Create()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new IOSSpeechRecognizer();
#else
            return new UnavailableSpeechRecognizer();
#endif
        }
    }

    public class UnavailableSpeechRecognizer : ISpeechRecognizer
    {
        public bool IsAvailable => false;
        public event Action SpeechStarted { add { } remove { } }
        public event Action<string> Partial { add { } remove { } }
        public event Action<string> Final { add { } remove { } }
        public event Action<string> Failed { add { } remove { } }
        public void Start(string locale) { }
        public void Stop() { }
        public void Discard() { }
        public void Dispose() { }
    }

#if UNITY_IOS && !UNITY_EDITOR
    // Native side: Assets/Plugins/iOS/CompanionVoice.swift.
    public class IOSSpeechRecognizer : ISpeechRecognizer
    {
        [DllImport("__Internal")] static extern void _CompanionSpeechStart(string locale);
        [DllImport("__Internal")] static extern void _CompanionSpeechStop();
        [DllImport("__Internal")] static extern void _CompanionSpeechDiscard();

        public bool IsAvailable => true;
        public event Action SpeechStarted;
        public event Action<string> Partial;
        public event Action<string> Final;
        public event Action<string> Failed;

        public IOSSpeechRecognizer()
        {
            NativeBridge.Ensure();
            NativeBridge.SpeechStarted += OnStarted;
            NativeBridge.SpeechPartial += OnPartial;
            NativeBridge.SpeechFinal += OnFinal;
            NativeBridge.SpeechError += OnError;
        }

        public void Start(string locale) => _CompanionSpeechStart(locale);
        public void Stop() => _CompanionSpeechStop();
        public void Discard() => _CompanionSpeechDiscard();

        void OnStarted() => SpeechStarted?.Invoke();
        void OnPartial(string t) => Partial?.Invoke(t);
        void OnFinal(string t) => Final?.Invoke(t);
        void OnError(string m) => Failed?.Invoke(m);

        public void Dispose()
        {
            NativeBridge.SpeechStarted -= OnStarted;
            NativeBridge.SpeechPartial -= OnPartial;
            NativeBridge.SpeechFinal -= OnFinal;
            NativeBridge.SpeechError -= OnError;
        }
    }
#endif
}
