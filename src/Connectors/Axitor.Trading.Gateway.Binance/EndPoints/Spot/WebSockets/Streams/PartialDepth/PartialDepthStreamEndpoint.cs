using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.PartialDepth;

/// <summary>Partial Book Depth Streams.</summary>
public sealed class PartialDepthStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly BinanceRouter<TTelemetry> _router;

    internal PartialDepthStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for the top bids and asks of each symbol, sent whole on every push: no snapshot or sync needed.
    /// Spot sends no symbol: the subscription handle tells which one. Its connections open on the first subscription.
    /// For the best bid and ask in real time use BookTicker; for a full local order book use Depth.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets.
    /// </summary>
    /// <param name="handler">Receives the data on the receive loop: must return fast and never throw.</param>
    /// <param name="levels">Levels per side.</param>
    /// <param name="speed">How often the exchange pushes.</param>
    public BinanceStreamSession CreateSession<THandler>(
        THandler handler,
        PartialDepthLevels levels = PartialDepthLevels.Twenty,
        SpotDepthUpdateSpeed speed = SpotDepthUpdateSpeed.Ms1000)
        where THandler : struct, IBinanceStreamHandler<SpotPartialDepthModel> =>
        _router.CreateStreamSession<SpotPartialDepthParser, SpotPartialDepthModel, THandler>(
            BinanceMarkets.Spot, _router.Options.Uri.SpotWebSocketStream, BuildNameSuffix(levels, speed), handler);

    /// <summary>Cold path. Suffix such as "@depth20@100ms"; the exchange default speed has no speed part.</summary>
    private static byte[] BuildNameSuffix(PartialDepthLevels levels, SpotDepthUpdateSpeed speed)
    {
        ReadOnlySpan<byte> count = levels switch
        {
            PartialDepthLevels.Five => "5"u8,
            PartialDepthLevels.Ten => "10"u8,
            _ => "20"u8,
        };

        ReadOnlySpan<byte> rate = speed switch
        {
            SpotDepthUpdateSpeed.Ms100 => "@100ms"u8,
            _ => ""u8,
        };

        return [.. "@depth"u8, .. count, .. rate];
    }
}
