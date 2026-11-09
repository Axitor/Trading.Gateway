using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM;

/// <summary>Binance Futures UsdM endpoints.</summary>
public sealed class UsdM<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>REST API endpoints.</summary>
    public RestApi<TTelemetry> RestApi { get; init; }

    /// <summary>WebSocket API endpoints.</summary>
    public WsApi<TTelemetry> WsApi { get; init; }

    /// <summary>WebSocket streams.</summary>
    public WsStreams<TTelemetry> WsStreams { get; init; }

    internal UsdM(BinanceRouter<TTelemetry> router)
    {
        RestApi = new RestApi<TTelemetry>(router);
        WsApi = new WsApi<TTelemetry>(router);
        WsStreams = new WsStreams<TTelemetry>(router);
    }
}
