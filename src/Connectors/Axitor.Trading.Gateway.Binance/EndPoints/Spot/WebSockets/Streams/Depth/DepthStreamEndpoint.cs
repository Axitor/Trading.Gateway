using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.Depth;

/// <summary>Diff Depth Stream.</summary>
public sealed class DepthStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly BinanceRouter<TTelemetry> _router;

    internal DepthStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for order book changes. A local order book starts from an OrderBook snapshot and applies them on top.
    /// Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets.
    /// </summary>
    /// <param name="handler">Receives the updates on the receive loop: must return fast and never throw.</param>
    /// <param name="speed">How often the exchange pushes.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler, SpotDepthUpdateSpeed speed = SpotDepthUpdateSpeed.Ms1000)
        where THandler : struct, IBinanceDepthHandler =>
        _router.CreateDepthSession<SpotDepthUpdateParser, THandler>(
            BinanceMarkets.Spot, _router.Options.Uri.SpotWebSocketStream, BuildNameSuffix(speed), handler);

    /// <summary>Cold path. Suffix "@depth@100ms", or "@depth" for the exchange default speed.</summary>
    private static byte[] BuildNameSuffix(SpotDepthUpdateSpeed speed) =>
        speed switch
        {
            SpotDepthUpdateSpeed.Ms100 => "@depth@100ms"u8.ToArray(),
            _ => "@depth"u8.ToArray(),
        };
}
