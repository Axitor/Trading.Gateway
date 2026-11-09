using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>Parses stream data, drops duplicates by update id and hands the model to the handler.</summary>
/// <remarks>
/// DESIGN NOTE: The core calls it under the lock of the connection's shard, so the last ids need no atomics.
/// </remarks>
internal readonly struct BinanceStreamDispatcher<TParser, TModel, THandler> : IWsStreamDispatcher
    where TParser : struct, IBinanceStreamParser<TModel>
    where TModel : struct
    where THandler : struct, IBinanceStreamHandler<TModel>
{
    private readonly THandler _handler;

    // Last delivered update id per subscription handle.
    private readonly long[] _lastUpdateIds;

    public BinanceStreamDispatcher(THandler handler, int capacity)
    {
        _handler = handler;
        _lastUpdateIds = new long[capacity];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WsDispatchResult Dispatch(int subscription, ReadOnlySpan<byte> data)
    {
        // Market data: an unreadable message is dropped, the next one replaces it.
        if (!TParser.TryParse(data, out TModel model, out long updateId))
        {
            return WsDispatchResult.Unreadable;
        }

        ref long lastUpdateId = ref _lastUpdateIds[subscription];

        // Both connections deliver while a rotation overlaps: the later copy is dropped.
        if (updateId <= lastUpdateId)
        {
            return WsDispatchResult.Handled;
        }

        lastUpdateId = updateId;
        _handler.OnData(subscription, in model);
        return WsDispatchResult.Handled;
    }

    /// <inheritdoc />
    public WsInboundSignal DispatchEvent(ReadOnlySpan<byte> message) => WsInboundSignal.None;

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _handler.OnDisconnected(subscription, reason, isFatal);

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap) => _handler.OnReconnected(subscription, in gap);

    /// <inheritdoc />
    public void OnSubscribing(int subscription) => _lastUpdateIds[subscription] = 0;

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) => _handler.OnUnsubscribed(subscription);
}
