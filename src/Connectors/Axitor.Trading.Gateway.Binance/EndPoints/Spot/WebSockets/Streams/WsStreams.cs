using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.AggTrade;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.BookTicker;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.Depth;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams.PartialDepth;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams;

/// <summary>Binance Spot WebSocket streams.</summary>
public sealed class WsStreams<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Individual Symbol Book Ticker stream.</summary>
    public BookTickerStreamEndpoint<TTelemetry> BookTicker { get; init; }

    /// <summary>Aggregate Trade stream.</summary>
    public AggTradeStreamEndpoint<TTelemetry> AggTrade { get; init; }

    /// <summary>Diff Depth stream.</summary>
    public DepthStreamEndpoint<TTelemetry> Depth { get; init; }

    /// <summary>Partial Book Depth stream.</summary>
    public PartialDepthStreamEndpoint<TTelemetry> PartialDepth { get; init; }

    internal WsStreams(BinanceRouter<TTelemetry> router)
    {
        BookTicker = new BookTickerStreamEndpoint<TTelemetry>(router);
        AggTrade = new AggTradeStreamEndpoint<TTelemetry>(router);
        Depth = new DepthStreamEndpoint<TTelemetry>(router);
        PartialDepth = new PartialDepthStreamEndpoint<TTelemetry>(router);
    }
}
