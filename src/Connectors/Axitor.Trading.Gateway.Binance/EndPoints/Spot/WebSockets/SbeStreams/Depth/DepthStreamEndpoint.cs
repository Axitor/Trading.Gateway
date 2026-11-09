using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.Depth;

/// <summary>Diff Depth Streams over SBE.</summary>
public sealed class DepthStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private static readonly byte[] _nameSuffix = "@depth"u8.ToArray();

    private readonly BinanceRouter<TTelemetry> _router;

    internal DepthStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for order book changes every 20 ms. A local order book starts from a spot OrderBook snapshot and applies them on top;
    /// an OrderBookSet of spot can serve it. Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets or BinanceSecret.Ed25519ApiKey is not set.
    /// </summary>
    /// <param name="handler">Receives the updates on the receive loop: must return fast and never throw.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceDepthHandler =>
        _router.CreateSbeDepthSession(_nameSuffix, handler);
}
