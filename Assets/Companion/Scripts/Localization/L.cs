using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companion
{
    public enum Language { English, Chinese }

    // Minimal two-language string table. English is the default.
    public static class L
    {
        const string PrefKey = "companion.language";

        static readonly Dictionary<string, (string en, string zh)> Table = new()
        {
            ["input.placeholder"] = ("Say something…", "说点什么…"),
            ["menu.motion"] = ("Change pose", "换个动作"),
            ["menu.clear"] = ("Clear chat", "清空对话"),
            ["menu.settings"] = ("Settings", "设置"),
            ["menu.about"] = ("About", "关于"),
            ["settings.title"] = ("Settings", "设置"),
            ["settings.language"] = ("Language", "语言"),
            ["settings.done"] = ("Done", "完成"),
            ["toast.cleared"] = ("Chat cleared", "对话已清空"),
            ["toast.about"] = ("Immersive companion demo", "沉浸式陪伴 Demo"),
            ["toast.voiceUnavailable"] = ("Voice input is available on iOS", "语音输入仅在 iOS 上可用"),
            ["toast.voicePermission"] = ("Allow microphone and speech recognition in Settings", "请在系统设置中允许麦克风和语音识别"),
            ["toast.voiceFailed"] = ("Didn't catch that, please try again", "没听清，请再说一次"),
            ["toast.offline"] = ("Can't reach her right now", "暂时连接不上她"),
            ["voice.hold"] = ("Hold to talk", "按住说话"),
            ["signin.title"] = ("Welcome", "欢迎"),
            ["signin.apple"] = ("Continue with Apple", "通过 Apple 继续"),
            ["signin.google"] = ("Continue with Google", "通过 Google 继续"),
            ["signin.failed"] = ("Sign-in failed, please try again", "登录失败，请重试"),
            ["character.name"] = ("Her", "她"),
        };

        static Language? current;

        public static event Action LanguageChanged;

        // The project enters Play mode without a domain reload, so statics must be reset explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            LanguageChanged = null;
        }

        public static Language Current
        {
            get => current ??= PlayerPrefs.GetString(PrefKey, "en") == "zh" ? Language.Chinese : Language.English;
            set
            {
                if (value == Current) return;
                current = value;
                PlayerPrefs.SetString(PrefKey, Code);
                PlayerPrefs.Save();
                LanguageChanged?.Invoke();
            }
        }

        public static bool HasSavedPreference => PlayerPrefs.HasKey(PrefKey);

        public static string Code => Current == Language.Chinese ? "zh" : "en";

        public static string SpeechLocale => Current == Language.Chinese ? "zh-CN" : "en-US";

        public static string Get(string key) =>
            Table.TryGetValue(key, out var s) ? (Current == Language.Chinese ? s.zh : s.en) : key;

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);
    }
}
