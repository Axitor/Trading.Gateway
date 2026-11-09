using Axitor.Trading.Gateway.Binance.EndPoints.Spot.RestApi;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.SbeStreams;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot;

/// <summary>Binance Spot endpoints.</summary>
public sealed class Spot<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>REST API endpoints.</summary>
    public RestApi<TTelemetry> RestApi { get; init; }

    /// <summary>WebSocket API endpoints.</summary>
    public WsApi<TTelemetry> WsApi { get; init; }

    /// <summary>WebSocket streams.</summary>
    public WsStreams<TTelemetry> WsStreams { get; init; }

    /// <summary>SBE streams: binary market data, times in microseconds. Needs BinanceSecret.Ed25519ApiKey.</summary>
    public SbeStreams<TTelemetry> SbeStreams { get; init; }

    internal Spot(BinanceRouter<TTelemetry> router)
    {
        RestApi = new RestApi<TTelemetry>(router);
        WsApi = new WsApi<TTelemetry>(router);
        WsStreams = new WsStreams<TTelemetry>(router);
        SbeStreams = new SbeStreams<TTelemetry>(router);
    }
}
