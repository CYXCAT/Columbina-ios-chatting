using System;
using UnityEngine;

namespace Companion
{
    // A conversation backend. Replies arrive as streamed text: ReplyUpdated carries the full text so far,
    // ReplyCompleted the final text of one reply. Implementations raise events on the main thread.
    public interface IChatService : IDisposable
    {
        event Action<string> ReplyUpdated;
        event Action<string> ReplyCompleted;
        event Action<string> Error;
        // Normalized agent speaking volume [0,1]; fires ~10 Hz. Used for lip sync.
        event Action<float> AgentVoiceVolume;

        bool IsConnected { get; }
        Awaitable ConnectAsync(string language);
        // Leaves the conversation and releases its server-side resources; ConnectAsync starts a new one.
        Awaitable DisconnectAsync();
        Awaitable SendAsync(string message);
        // Interrupts the reply in progress (text and voice).
        void Cancel();
        void Reset();
    }
}
