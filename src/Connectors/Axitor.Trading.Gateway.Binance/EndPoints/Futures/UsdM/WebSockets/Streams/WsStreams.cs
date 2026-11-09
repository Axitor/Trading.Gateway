using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.AggTrade;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.BookTicker;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.Depth;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.Kline;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.PartialDepth;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.UserData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams;

/// <summary>Binance Futures UsdM WebSocket streams.</summary>
public sealed class WsStreams<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Individual Symbol Book Ticker stream.</summary>
    public BookTickerStreamEndpoint<TTelemetry> BookTicker { get; init; }

    /// <summary>Aggregate Trade stream.</summary>
    public AggTradeStreamEndpoint<TTelemetry> AggTrade { get; init; }

    /// <summary>User Data stream.</summary>
    public UserDataStreamEndpoint<TTelemetry> UserData { get; init; }

    /// <summary>Diff Book Depth stream.</summary>
    public DepthStreamEndpoint<TTelemetry> Depth { get; init; }

    /// <summary>Partial Book Depth stream.</summary>
    public PartialDepthStreamEndpoint<TTelemetry> PartialDepth { get; init; }

    /// <summary>Kline/Candlestick stream.</summary>
    public KlineStreamEndpoint<TTelemetry> Kline { get; init; }

    internal WsStreams(BinanceRouter<TTelemetry> router)
    {
        BookTicker = new BookTickerStreamEndpoint<TTelemetry>(router);
        AggTrade = new AggTradeStreamEndpoint<TTelemetry>(router);
        UserData = new UserDataStreamEndpoint<TTelemetry>(router);
        Depth = new DepthStreamEndpoint<TTelemetry>(router);
        PartialDepth = new PartialDepthStreamEndpoint<TTelemetry>(router);
        Kline = new KlineStreamEndpoint<TTelemetry>(router);
    }
}
