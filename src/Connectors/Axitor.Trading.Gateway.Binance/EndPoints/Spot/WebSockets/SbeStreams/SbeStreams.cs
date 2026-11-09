using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.BestBidAsk;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.Depth;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.PartialDepth;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams.Trade;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams;

/// <summary>Binance Spot SBE streams.</summary>
public sealed class SbeStreams<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Trade stream.</summary>
    public TradeStreamEndpoint<TTelemetry> Trade { get; init; }

    /// <summary>Best Bid/Ask stream.</summary>
    public BestBidAskStreamEndpoint<TTelemetry> BestBidAsk { get; init; }

    /// <summary>Diff Depth stream.</summary>
    public DepthStreamEndpoint<TTelemetry> Depth { get; init; }

    /// <summary>Partial Book Depth stream.</summary>
    public PartialDepthStreamEndpoint<TTelemetry> PartialDepth { get; init; }

    internal SbeStreams(BinanceRouter<TTelemetry> router)
    {
        Trade = new TradeStreamEndpoint<TTelemetry>(router);
        BestBidAsk = new BestBidAskStreamEndpoint<TTelemetry>(router);
        Depth = new DepthStreamEndpoint<TTelemetry>(router);
        PartialDepth = new PartialDepthStreamEndpoint<TTelemetry>(router);
    }
}
