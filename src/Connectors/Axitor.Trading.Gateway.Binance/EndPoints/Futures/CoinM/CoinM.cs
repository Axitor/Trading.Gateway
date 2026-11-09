using Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.RestApi;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.WebSockets.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM;

/// <summary>Binance Futures CoinM endpoints.</summary>
public sealed class CoinM<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>REST API endpoints.</summary>
    public RestApi<TTelemetry> RestApi { get; init; }

    /// <summary>WebSocket streams.</summary>
    public WsStreams<TTelemetry> WsStreams { get; init; }

    internal CoinM(BinanceRouter<TTelemetry> router)
    {
        RestApi = new RestApi<TTelemetry>(router);
        WsStreams = new WsStreams<TTelemetry>(router);
    }
}
