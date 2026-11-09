using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.BookTicker;

/// <summary>Individual Symbol Book Ticker Streams.</summary>
public sealed class BookTickerStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private static readonly byte[] _nameSuffix = "@bookTicker"u8.ToArray();

    private readonly BinanceRouter<TTelemetry> _router;

    internal BookTickerStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for best bid and ask updates in real time. Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.StreamMarkets.
    /// </summary>
    /// <param name="handler">Receives the data on the receive loop: must return fast and never throw.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceStreamHandler<SpotBookTickerModel> =>
        _router.CreateStreamSession<SpotBookTickerParser, SpotBookTickerModel, THandler>(
            BinanceMarkets.Spot, _router.Options.Uri.SpotWebSocketStream, _nameSuffix, handler);
}
