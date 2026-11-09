using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.AccountInfoV2;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.AccountInfoV3;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account;

/// <summary>Binance Futures UsdM REST API Account endpoints.</summary>
public sealed class Account<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Account Information V3 endpoint.</summary>
    public AccountInfoEndpoint<TTelemetry> AccountInfo { get; init; }

    /// <summary>Account Information V2 endpoint.</summary>
    public AccountInfoV2Endpoint<TTelemetry> AccountInfoV2 { get; init; }

    /// <summary>Symbol Configuration endpoint.</summary>
    public SymbolConfigEndpoint<TTelemetry> SymbolConfig { get; init; }

    /// <summary>Notional and Leverage Brackets endpoint.</summary>
    public LeverageBracketEndpoint<TTelemetry> LeverageBracket { get; init; }

    internal Account(BinanceRouter<TTelemetry> router)
    {
        AccountInfo = new AccountInfoEndpoint<TTelemetry>(router);
        AccountInfoV2 = new AccountInfoV2Endpoint<TTelemetry>(router);
        SymbolConfig = new SymbolConfigEndpoint<TTelemetry>(router);
        LeverageBracket = new LeverageBracketEndpoint<TTelemetry>(router);
    }
}
