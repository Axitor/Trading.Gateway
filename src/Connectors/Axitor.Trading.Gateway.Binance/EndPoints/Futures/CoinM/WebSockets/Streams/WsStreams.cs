using Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.WebSockets.Streams.Depth;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.WebSockets.Streams.UserData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.WebSockets.Streams;

/// <summary>Binance Futures CoinM WebSocket streams.</summary>
public sealed class WsStreams<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Diff Book Depth stream.</summary>
    public DepthStreamEndpoint<TTelemetry> Depth { get; init; }

    /// <summary>User Data stream.</summary>
    public UserDataStreamEndpoint<TTelemetry> UserData { get; init; }

    internal WsStreams(BinanceRouter<TTelemetry> router)
    {
        Depth = new DepthStreamEndpoint<TTelemetry>(router);
        UserData = new UserDataStreamEndpoint<TTelemetry>(router);
    }
}
