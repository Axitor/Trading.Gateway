using Axitor.Trading.Gateway.Binance.EndPoints.Spot.RestApi.MarketData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.RestApi;

/// <summary>Binance Spot REST API endpoints.</summary>
public sealed class RestApi<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Market Data endpoints.</summary>
    public MarketData<TTelemetry> MarketData { get; init; }

    internal RestApi(BinanceRouter<TTelemetry> router)
    {
        MarketData = new MarketData<TTelemetry>(router);
    }
}
