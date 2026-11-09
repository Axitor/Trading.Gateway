using Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures;

/// <summary>Binance Futures endpoints</summary>
public sealed class Futures<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>UsdM endpoints.</summary>
    public UsdM<TTelemetry> UsdM { get; init; }

    /// <summary>CoinM endpoints.</summary>
    public CoinM<TTelemetry> CoinM { get; init; }

    internal Futures(BinanceRouter<TTelemetry> router)
    {
        UsdM = new UsdM<TTelemetry>(router);
        CoinM = new CoinM<TTelemetry>(router);
    }
}
