using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.WebSockets.Streams.Depth;

/// <summary>Diff Book Depth Streams.</summary>
public sealed class DepthStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly BinanceRouter<TTelemetry> _router;

    internal DepthStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for order book changes. A local order book starts from an OrderBook snapshot and applies them on top.
    /// Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when COIN-M futures are left out of BinanceOptions.StreamMarkets.
    /// </summary>
    /// <param name="handler">Receives the updates on the receive loop: must return fast and never throw.</param>
    /// <param name="speed">How often the exchange pushes.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler, DepthUpdateSpeed speed = DepthUpdateSpeed.Ms250)
        where THandler : struct, IBinanceDepthHandler =>
        _router.CreateDepthSession<FuturesDepthUpdateParser, THandler>(
            BinanceMarkets.FuturesCoinM, _router.Options.Uri.FuturesCoinWebSocketStream, BuildNameSuffix(speed), handler);

    /// <summary>Cold path. Suffix such as "@depth@100ms"; the exchange default speed has no speed part.</summary>
    private static byte[] BuildNameSuffix(DepthUpdateSpeed speed) =>
        speed switch
        {
            DepthUpdateSpeed.Ms500 => "@depth@500ms"u8.ToArray(),
            DepthUpdateSpeed.Ms100 => "@depth@100ms"u8.ToArray(),
            _ => "@depth"u8.ToArray(),
        };
}
