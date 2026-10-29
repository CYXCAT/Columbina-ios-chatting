using System;
using UnityEngine;

namespace Companion
{
    // Offline stand-in for the real backend: keyword replies, typed out character by character.
    public class MockChatService : IChatService
    {
        static readonly (string[] keys, string[] replies)[] RulesEn =
        {
            (new[] { "hello", "hi", "hey" }, new[] { "Hey, I'm right here! What's on your mind today?", "Hi! I'm so happy to see you." }),
            (new[] { "name", "who are you" }, new[] { "I'm the one who's always here with you. Call me whatever you like." }),
            (new[] { "tired", "sleepy", "sad", "stressed" }, new[] { "That sounds rough. Take a deep breath with me. Want to tell me what happened?", "Sending you a hug. It's okay to slow down today." }),
            (new[] { "like you", "love" }, new[] { "Hehe... you're making me blush.", "I love spending time with you too." }),
            (new[] { "weather", "rain", "sunny" }, new[] { "Whatever the weather's like out there, take care of yourself, okay?" }),
            (new[] { "good night", "sleep" }, new[] { "Good night! Sweet dreams, see you tomorrow." }),
            (new[] { "morning" }, new[] { "Good morning! Let's make today a good one." }),
            (new[] { "dance", "pose" }, new[] { "Want to see me dance? Try the menu in the top right corner." }),
        };

        static readonly (string[] keys, string[] replies)[] RulesZh =
        {
            (new[] { "你好", "嗨", "在吗" }, new[] { "我在呢～今天想聊点什么？", "嗨！见到你真开心。" }),
            (new[] { "名字", "你是谁" }, new[] { "我是一直陪在你身边的人呀，你想怎么叫我都可以。" }),
            (new[] { "累", "困", "烦", "难过" }, new[] { "辛苦啦，先深呼吸一下。要不要跟我说说发生了什么？", "抱抱你。今天就允许自己慢一点吧。" }),
            (new[] { "喜欢", "爱" }, new[] { "嘿嘿……被你这么说，我会害羞的。", "我也很喜欢和你待在一起。" }),
            (new[] { "天气", "下雨", "晴" }, new[] { "不管外面天气怎样，记得照顾好自己哦。" }),
            (new[] { "晚安", "睡" }, new[] { "晚安～做个好梦，明天见。" }),
            (new[] { "早" }, new[] { "早安！今天也一起加油吧。" }),
            (new[] { "跳舞", "动作" }, new[] { "想看我跳舞的话，可以点右上角的菜单哦。" }),
        };

        static readonly string[] FallbackEn =
        {
            "Mm-hm, I'm listening. And then?",
            "Oh, I see... how did that make you feel?",
            "That's interesting! Tell me more.",
            "Got it, I'll remember that. Anything else you want to share?",
        };

        static readonly string[] FallbackZh =
        {
            "嗯嗯，我在认真听。然后呢？",
            "原来是这样……你当时是什么感觉？",
            "这个好有意思，再多说一点嘛。",
            "我记住啦。还有什么想跟我分享的吗？",
        };

        const float TypeInterval = 0.035f;

        public event Action<string> ReplyUpdated;
        public event Action<string> ReplyCompleted;
        public event Action<string> Error { add { } remove { } }
        public event Action<float> AgentVoiceVolume { add { } remove { } }

        string language = "en";
        int turn, generation;

        public bool IsConnected => true;

        public Awaitable ConnectAsync(string language)
        {
            this.language = language;
            return Done();
        }

        public async Awaitable SendAsync(string message)
        {
            int gen = generation;
            await Awaitable.WaitForSecondsAsync(UnityEngine.Random.Range(0.6f, 1.2f));
            var reply = Pick(message);
            for (int n = 1; n <= reply.Length; n++)
            {
                if (gen != generation) return;
                ReplyUpdated?.Invoke(reply.Substring(0, n));
                await Awaitable.WaitForSecondsAsync(TypeInterval);
            }
            if (gen == generation) ReplyCompleted?.Invoke(reply);
        }

        string Pick(string message)
        {
            turn++;
            bool zh = language == "zh";
            var lower = message.ToLowerInvariant();
            foreach (var (keys, replies) in zh ? RulesZh : RulesEn)
                foreach (var k in keys)
                    if (lower.Contains(k))
                        return replies[UnityEngine.Random.Range(0, replies.Length)];
            var fallback = zh ? FallbackZh : FallbackEn;
            return fallback[turn % fallback.Length];
        }

        public void Reset()
        {
            turn = 0;
            generation++;
        }

        public void Dispose() => generation++;

        static async Awaitable Done() => await Awaitable.NextFrameAsync();
    }
}
