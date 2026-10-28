using System;
using System.Runtime.InteropServices;

namespace Companion
{
    // Push-to-talk speech recognition. Events are raised on the main thread.
    public interface ISpeechRecognizer : IDisposable
    {
        bool IsAvailable { get; }
        event Action<string> Partial;
        event Action<string> Final;
        event Action<string> Failed;
        void Start(string locale);
        void Stop();
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
        public event Action<string> Partial { add { } remove { } }
        public event Action<string> Final { add { } remove { } }
        public event Action<string> Failed { add { } remove { } }
        public void Start(string locale) { }
        public void Stop() { }
        public void Dispose() { }
    }

#if UNITY_IOS && !UNITY_EDITOR
    // Native side: Assets/Plugins/iOS/SpeechBridge.swift (SpeechAnalyzer on iOS 26+, SFSpeechRecognizer before).
    public class IOSSpeechRecognizer : ISpeechRecognizer
    {
        [DllImport("__Internal")] static extern void _CompanionSpeechStart(string locale);
        [DllImport("__Internal")] static extern void _CompanionSpeechStop();

        public bool IsAvailable => true;
        public event Action<string> Partial;
        public event Action<string> Final;
        public event Action<string> Failed;

        public IOSSpeechRecognizer()
        {
            NativeBridge.Ensure();
            NativeBridge.SpeechPartial += OnPartial;
            NativeBridge.SpeechFinal += OnFinal;
            NativeBridge.SpeechError += OnError;
        }

        public void Start(string locale) => _CompanionSpeechStart(locale);
        public void Stop() => _CompanionSpeechStop();

        void OnPartial(string t) => Partial?.Invoke(t);
        void OnFinal(string t) => Final?.Invoke(t);
        void OnError(string m) => Failed?.Invoke(m);

        public void Dispose()
        {
            NativeBridge.SpeechPartial -= OnPartial;
            NativeBridge.SpeechFinal -= OnFinal;
            NativeBridge.SpeechError -= OnError;
        }
    }
#endif
}
