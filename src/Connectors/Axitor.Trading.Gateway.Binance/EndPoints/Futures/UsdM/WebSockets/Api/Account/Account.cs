using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountInfo;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account;

/// <summary>Binance Futures UsdM WebSocket API Account endpoints.</summary>
public sealed class Account<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Futures Account Balance V2 endpoint.</summary>
    public AccountBalanceEndpoint<TTelemetry> AccountBalance { get; init; }

    /// <summary>Account Information endpoint, the counterpart of REST Account Information V2.</summary>
    public AccountInfoEndpoint<TTelemetry> AccountInfo { get; init; }

    internal Account(BinanceRouter<TTelemetry> router)
    {
        AccountBalance = new AccountBalanceEndpoint<TTelemetry>(router);
        AccountInfo = new AccountInfoEndpoint<TTelemetry>(router);
    }
}
