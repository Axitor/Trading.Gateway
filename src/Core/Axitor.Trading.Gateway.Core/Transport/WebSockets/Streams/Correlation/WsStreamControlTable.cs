using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Correlation;

/// <summary>Subscribe and unsubscribe requests of one route awaiting their replies. Cold path.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Built on TaskCompletionSource. It's a cold path, subscribe requests are rare, so a small allocation per request is fine.
/// </para>
/// <para>
/// DESIGN NOTE: A request is tied to the connection it was sent on, not to the shard.
/// During rotation the old connection closes while the new one is already serving,
/// and its loss must not fail requests sent on the new one.
/// </para>
/// <para>
/// DESIGN NOTE: Replies are completed under the lock. This is safe: continuations run asynchronously, so no caller code runs inside the lock.
/// </para>
/// </remarks>
internal sealed class WsStreamControlTable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<long, Pending> _pending = [];
    private long _lastRequestId;

    /// <summary>Takes a fresh id for a request about to be sent on the given connection.</summary>
    public long Register(int connectionEpoch, out Task<WsStreamControlReply> reply)
    {
        long requestId = Interlocked.Increment(ref _lastRequestId);

        // The receive loop completes it and must not run the caller's continuation inline.
        TaskCompletionSource<WsStreamControlReply> source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_sync)
        {
            _pending.Add(requestId, new Pending() { Source = source, ConnectionEpoch = connectionEpoch });
        }

        reply = source.Task;
        return requestId;
    }

    /// <summary>Hands a reply to the request waiting for it. False for an unknown or late id. Runs on the receive loop.</summary>
    public bool TryComplete(long requestId, bool isError, int exchangeCode)
    {
        Pending pending;

        lock (_sync)
        {
            if (!_pending.Remove(requestId, out pending))
            {
                return false;
            }
        }

        pending.Source.TrySetResult(new WsStreamControlReply() { Received = true, IsError = isError, ExchangeCode = exchangeCode });
        return true;
    }

    /// <summary>Drops a request that stopped waiting, e.g. after its timeout. A late reply is then unmatched.</summary>
    public void Forget(long requestId)
    {
        lock (_sync)
        {
            _pending.Remove(requestId);
        }
    }

    /// <summary>Ends every request sent on a connection that has gone, as not received.</summary>
    public void FailConnection(int connectionEpoch)
    {
        lock (_sync)
        {
            foreach (KeyValuePair<long, Pending> entry in _pending)
            {
                if (entry.Value.ConnectionEpoch != connectionEpoch)
                {
                    continue;
                }

                // Removing during enumeration is allowed for Dictionary since .NET Core 3.0.
                _pending.Remove(entry.Key);
                entry.Value.Source.TrySetResult(default);
            }
        }
    }

    /// <summary>Ends every request in flight, as not received.</summary>
    public void FailAll()
    {
        lock (_sync)
        {
            foreach (KeyValuePair<long, Pending> entry in _pending)
            {
                entry.Value.Source.TrySetResult(default);
            }

            _pending.Clear();
        }
    }

    private struct Pending
    {
        public TaskCompletionSource<WsStreamControlReply> Source;
        public int ConnectionEpoch;
    }
}
