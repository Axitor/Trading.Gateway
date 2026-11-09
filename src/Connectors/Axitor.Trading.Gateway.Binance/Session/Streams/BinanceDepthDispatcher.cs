using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>Parses depth updates into reused level buffers, drops duplicates by final update id and hands them to the handler.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Each subscription has its own level buffers, made when it subscribes and grown when a message has more levels.
/// Shards dispatch on different threads at once, so a buffer shared by the session would be a race.
/// </para>
/// <para>
/// DESIGN NOTE: An unreadable update is dropped. The next one breaks the order book's id chain, and the book is rebuilt.
/// </para>
/// </remarks>
internal readonly struct BinanceDepthDispatcher<TParser, THandler> : IWsStreamDispatcher
    where TParser : struct, IBinanceDepthParser
    where THandler : struct, IBinanceDepthHandler
{
    // Starting size of the level buffers; they double when a message has more levels.
    private const int InitialLevels = 64;

    private readonly THandler _handler;
    private readonly long[] _lastUpdateIds;
    private readonly DepthLevel[]?[] _bids;
    private readonly DepthLevel[]?[] _asks;

    public BinanceDepthDispatcher(THandler handler, int capacity)
    {
        _handler = handler;
        _lastUpdateIds = new long[capacity];
        _bids = new DepthLevel[]?[capacity];
        _asks = new DepthLevel[]?[capacity];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WsDispatchResult Dispatch(int subscription, ReadOnlySpan<byte> data)
    {
        // Made in OnSubscribing, before any data of the handle.
        ref DepthLevel[] bids = ref _bids[subscription]!;
        ref DepthLevel[] asks = ref _asks[subscription]!;

        if (!TParser.TryParse(data, out DepthUpdateHeader header, ref bids, out int bidCount, ref asks, out int askCount))
        {
            return WsDispatchResult.Unreadable;
        }

        ref long lastUpdateId = ref _lastUpdateIds[subscription];

        // Both connections deliver while a rotation overlaps: the later copy is dropped.
        if (header.FinalUpdateId <= lastUpdateId)
        {
            return WsDispatchResult.Handled;
        }

        lastUpdateId = header.FinalUpdateId;
        _handler.OnData(subscription, in header, bids.AsSpan(0, bidCount), asks.AsSpan(0, askCount));
        return WsDispatchResult.Handled;
    }

    /// <inheritdoc />
    public WsInboundSignal DispatchEvent(ReadOnlySpan<byte> message) => WsInboundSignal.None;

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _handler.OnDisconnected(subscription, reason, isFatal);

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap) => _handler.OnReconnected(subscription, in gap);

    /// <inheritdoc />
    public void OnSubscribing(int subscription)
    {
        _lastUpdateIds[subscription] = 0;

        // Kept when the handle is reused: a grown buffer stays grown.
        _bids[subscription] ??= new DepthLevel[InitialLevels];
        _asks[subscription] ??= new DepthLevel[InitialLevels];
    }

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) => _handler.OnUnsubscribed(subscription);
}
