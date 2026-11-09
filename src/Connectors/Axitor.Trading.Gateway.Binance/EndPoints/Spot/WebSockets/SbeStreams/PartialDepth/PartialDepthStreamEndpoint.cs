using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.PartialDepth;

/// <summary>Partial Book Depth Streams over SBE.</summary>
public sealed class PartialDepthStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private static readonly byte[] _nameSuffix = "@depth20"u8.ToArray();

    private readonly BinanceRouter<TTelemetry> _router;

    internal PartialDepthStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for the top 20 bids and asks of each symbol every 50 ms, sent whole on every push: no snapshot or sync needed.
    /// Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets or BinanceSecret.Ed25519ApiKey is not set.
    /// </summary>
    /// <param name="handler">Receives the data on the receive loop: must return fast and never throw.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceStreamHandler<SbePartialDepthModel> =>
        _router.CreateSbeStreamSession<SbePartialDepthParser, SbePartialDepthModel, THandler>(BinanceSbeReader.DepthSnapshotTemplateId, _nameSuffix, handler);
}
