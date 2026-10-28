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

        bool IsConnected { get; }
        Awaitable ConnectAsync(string language);
        Awaitable SendAsync(string message);
        void Reset();
    }
}
