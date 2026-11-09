using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.MarketData;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.UserData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api;

/// <summary>Binance Spot WebSocket API endpoints.</summary>
public sealed class WsApi<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Market Data endpoints.</summary>
    public MarketData<TTelemetry> MarketData { get; init; }

    /// <summary>User Data Stream endpoint.</summary>
    public UserDataStreamEndpoint<TTelemetry> UserData { get; init; }

    internal WsApi(BinanceRouter<TTelemetry> router)
    {
        MarketData = new MarketData<TTelemetry>(router);
        UserData = new UserDataStreamEndpoint<TTelemetry>(router);
    }
}
