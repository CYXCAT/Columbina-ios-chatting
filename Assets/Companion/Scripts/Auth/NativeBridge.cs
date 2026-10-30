using System;
using UnityEngine;

namespace Companion
{
    // Receives UnitySendMessage callbacks from the iOS plugins (Assets/Plugins/iOS/*.swift).
    // The GameObject name must stay "CompanionNative": the Swift side targets it by name.
    public class NativeBridge : MonoBehaviour
    {
        public const string ObjectName = "CompanionNative";

        static NativeBridge instance;

        public static event Action<string> AuthResult;     // JSON: {"ok":true,"provider":"google","token":"...","name":"..."} or {"ok":false,"error":"..."}
        public static event Action SpeechStarted;          // the user started talking
        public static event Action<string> SpeechPartial;  // interim transcript
        public static event Action<string> SpeechFinal;    // final transcript
        public static event Action<string> SpeechError;

        // The project enters Play mode without a domain reload, so statics must be reset explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            AuthResult = null;
            SpeechStarted = null;
            SpeechPartial = null;
            SpeechFinal = null;
            SpeechError = null;
        }

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NativeBridge>();
        }

        void OnAuthResult(string json) => AuthResult?.Invoke(json);
        void OnSpeechStarted(string _) => SpeechStarted?.Invoke();
        void OnSpeechPartial(string text) => SpeechPartial?.Invoke(text);
        void OnSpeechFinal(string text) => SpeechFinal?.Invoke(text);
        void OnSpeechError(string message) => SpeechError?.Invoke(message);
    }
}
