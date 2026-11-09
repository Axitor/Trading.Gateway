using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>A stream session bound to its dialect, dispatcher, transport and stream name suffix.</summary>
internal sealed class BinanceStreamSessionCore<TDialect, TDispatcher, TTelemetry> : BinanceStreamSession
    where TDialect : struct, IExchangeDialectWsStream
    where TDispatcher : struct, IWsStreamDispatcher
    where TTelemetry : struct, ITelemetryHandler
{
    // Channel of streams routed by their name.
    private const int NameChannel = 0;

    private readonly WsStreamTransport<TDialect, BinanceRateLimiterRef, TDispatcher, TTelemetry> _transport;

    // Stream name after the lowercase symbol, e.g. "@depth@100ms". Built once per session.
    private readonly byte[] _nameSuffix;

    // What the dialect routes data by: zero for the stream name, an SBE template id for binary data keyed by symbol.
    private readonly int _channel;

    public BinanceStreamSessionCore(WsStreamTransport<TDialect, BinanceRateLimiterRef, TDispatcher, TTelemetry> transport, byte[] nameSuffix, int channel)
    {
        _transport = transport;
        _nameSuffix = nameSuffix;
        _channel = channel;
    }

    /// <inheritdoc />
    public override Task Completion => _transport.Completion;

    /// <inheritdoc />
    public override ValueTask<WsStreamSubscribeResult> SubscribeAsync(Symbol symbol, CancellationToken cancellationToken)
    {
        byte[] name = BuildName(symbol);

        return _channel == NameChannel
            ? _transport.SubscribeAsync(name, cancellationToken)
            : _transport.SubscribeAsync(name, _channel, BuildKey(symbol), cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<bool> UnsubscribeAsync(int subscription, CancellationToken cancellationToken) =>
        _transport.UnsubscribeAsync(subscription, cancellationToken);

    /// <summary>Lowercase symbol plus the stream suffix, e.g. btcusdt@bookTicker: the exchange names streams by lowercase symbols.</summary>
    private byte[] BuildName(Symbol symbol)
    {
        byte[] name = new byte[symbol.Length + _nameSuffix.Length];

        symbol.TryWriteTo(name, out int written);

        for (int i = 0; i < written; i++)
        {
            // Letters only: digits and separators stay as they are.
            if ((uint)(name[i] - 'A') <= 'Z' - 'A')
            {
                name[i] |= 0x20;
            }
        }

        _nameSuffix.CopyTo(name.AsSpan(written));
        return name;
    }

    /// <summary>Uppercase symbol, as the exchange writes it inside binary data.</summary>
    private static byte[] BuildKey(Symbol symbol)
    {
        byte[] key = new byte[symbol.Length];

        symbol.TryWriteTo(key, out int written);

        for (int i = 0; i < written; i++)
        {
            // Letters only: digits and separators stay as they are.
            if ((uint)(key[i] - 'a') <= 'z' - 'a')
            {
                key[i] &= 0xDF;
            }
        }

        return key;
    }
}
