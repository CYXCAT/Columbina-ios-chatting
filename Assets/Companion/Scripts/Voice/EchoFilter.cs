using System.Collections.Generic;
using System.Text;

namespace Companion
{
    // Tells her own voice, picked up by the microphone past the echo canceller, from the user talking:
    // an echo transcript is mostly made of what she has just been saying. Compares adjacent token pairs
    // (CJK characters, or Latin words) so that sharing a few common words doesn't count.
    public static class EchoFilter
    {
        const float Overlap = 0.6f;

        // True when `heard` looks like an echo of `spoken`; `tokens` is the size of `heard`.
        public static bool IsEcho(string heard, string spoken, out int tokens)
        {
            var h = Tokens(heard);
            tokens = h.Count;
            var s = Tokens(spoken);
            if (h.Count == 0 || s.Count == 0) return false;
            if (h.Count == 1) return s.Contains(h[0]);

            var pairs = new HashSet<(string, string)>();
            for (int i = 0; i + 1 < s.Count; i++) pairs.Add((s[i], s[i + 1]));
            int hits = 0;
            for (int i = 0; i + 1 < h.Count; i++)
                if (pairs.Contains((h[i], h[i + 1]))) hits++;
            return hits >= Overlap * (h.Count - 1);
        }

        static List<string> Tokens(string text)
        {
            var tokens = new List<string>();
            var word = new StringBuilder();
            void Flush()
            {
                if (word.Length == 0) return;
                tokens.Add(word.ToString());
                word.Clear();
            }
            foreach (char c in text ?? "")
            {
                if (IsCjk(c))
                {
                    Flush();
                    tokens.Add(c.ToString());
                }
                else if (char.IsLetterOrDigit(c) || c == '\'') word.Append(char.ToLowerInvariant(c));
                else Flush();
            }
            Flush();
            return tokens;
        }

        static bool IsCjk(char c) => (c >= '㐀' && c <= '鿿') || (c >= '豈' && c <= '﫿');
    }
}
