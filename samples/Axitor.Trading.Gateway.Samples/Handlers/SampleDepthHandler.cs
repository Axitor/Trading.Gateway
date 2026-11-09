using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Samples.Handlers;

/// <summary>
/// Your own depth handler, e.g. to build your own order book. Must be a readonly struct; state lives in an object it holds.
/// Calls run on the receive loop: return fast and never throw. Calls for one subscription never overlap.
/// </summary>
internal readonly struct SampleDepthHandler : IBinanceDepthHandler
{
    // Last applied update id per subscription handle.
    private readonly long[] _lastUpdateIds;

    /// <summary>Pass BinanceClient.StreamSessionCapacity.</summary>
    public SampleDepthHandler(int capacity) => _lastUpdateIds = new long[capacity];

    /// <summary>Last update id of a subscription, read from any thread.</summary>
    public long GetLastUpdateId(int subscription) => Volatile.Read(ref _lastUpdateIds[subscription]);

    // Each level holds the new quantity at its price; zero removes the level.
    // The spans are valid only during this call: copy what you keep.
    public void OnData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        // When PreviousFinalUpdateId is not the last id you applied, updates were missed: rebuild your book from a snapshot.
        Volatile.Write(ref _lastUpdateIds[subscription], header.FinalUpdateId);
    }

    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) =>
        Console.WriteLine($"Depth {subscription} stopped: {reason}, fatal {isFatal}");

    // Updates inside the gap are lost: rebuild your book from a new snapshot.
    public void OnReconnected(int subscription, in WsStreamGap gap) =>
        Console.WriteLine($"Depth {subscription} is back: rebuild the book");

    public void OnUnsubscribed(int subscription) => Volatile.Write(ref _lastUpdateIds[subscription], 0);
}
