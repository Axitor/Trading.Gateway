using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.BestBidAsk;

/// <summary>Best Bid/Ask Streams over SBE.</summary>
public sealed class BestBidAskStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private static readonly byte[] _nameSuffix = "@bestBidAsk"u8.ToArray();

    private readonly BinanceRouter<TTelemetry> _router;

    internal BestBidAskStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for best bid and ask updates in real time. Under high load the exchange drops stale updates and sends only the latest.
    /// Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets or BinanceSecret.Ed25519ApiKey is not set.
    /// </summary>
    /// <param name="handler">Receives the data on the receive loop: must return fast and never throw.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceStreamHandler<SbeBestBidAskModel> =>
        _router.CreateSbeStreamSession<SbeBestBidAskParser, SbeBestBidAskModel, THandler>(BinanceSbeReader.BestBidAskTemplateId, _nameSuffix, handler);
}
