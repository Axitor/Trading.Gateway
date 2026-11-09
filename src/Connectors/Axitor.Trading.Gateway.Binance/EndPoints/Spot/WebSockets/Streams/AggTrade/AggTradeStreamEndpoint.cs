using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.AggTrade;

/// <summary>Aggregate Trade Streams.</summary>
public sealed class AggTradeStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private static readonly byte[] _nameSuffix = "@aggTrade"u8.ToArray();

    private readonly BinanceRouter<TTelemetry> _router;

    internal AggTradeStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for market trades aggregated by price and taker side, pushed in real time. Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets.
    /// </summary>
    /// <param name="handler">Receives the data on the receive loop: must return fast and never throw.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceStreamHandler<AggTradeModel> =>
        _router.CreateStreamSession<SpotAggTradeParser, AggTradeModel, THandler>(
            BinanceMarkets.Spot, _router.Options.Uri.SpotWebSocketStream, _nameSuffix, handler);
}
