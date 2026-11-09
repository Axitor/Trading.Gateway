using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>Hands each trade of an SBE trades event to the handler on its own and drops trades already handed over.</summary>
/// <remarks>
/// DESIGN NOTE: The core calls it under the lock of the connection's shard, so the last ids need no atomics.
/// </remarks>
internal readonly struct BinanceSbeTradeDispatcher<THandler> : IWsStreamDispatcher
    where THandler : struct, IBinanceStreamHandler<SbeTradeModel>
{
    private readonly THandler _handler;

    // Last delivered trade id per subscription handle.
    private readonly long[] _lastTradeIds;

    public BinanceSbeTradeDispatcher(THandler handler, int capacity)
    {
        _handler = handler;
        _lastTradeIds = new long[capacity];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WsDispatchResult Dispatch(int subscription, ReadOnlySpan<byte> data)
    {
        // Market data: an unreadable event is dropped, like on the JSON streams.
        if (!SbeTradeParser.TryOpen(data, out SbeTradeModel model, out int entriesStart, out int entryLength, out int count, out int priceExponent, out int quantityExponent))
        {
            return WsDispatchResult.Unreadable;
        }

        ref long lastTradeId = ref _lastTradeIds[subscription];
        int entryStart = entriesStart;

        for (int i = 0; i < count; i++)
        {
            SbeTradeParser.ReadTrade(data, entryStart, priceExponent, quantityExponent, ref model);
            entryStart += entryLength;

            // Both connections deliver while a rotation overlaps: trades already handed over are dropped.
            if (model.TradeId <= lastTradeId)
            {
                continue;
            }

            lastTradeId = model.TradeId;
            _handler.OnData(subscription, in model);
        }

        return WsDispatchResult.Handled;
    }

    /// <inheritdoc />
    public WsInboundSignal DispatchEvent(ReadOnlySpan<byte> message) => WsInboundSignal.None;

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _handler.OnDisconnected(subscription, reason, isFatal);

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap) => _handler.OnReconnected(subscription, in gap);

    /// <inheritdoc />
    public void OnSubscribing(int subscription) => _lastTradeIds[subscription] = 0;

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) => _handler.OnUnsubscribed(subscription);
}
