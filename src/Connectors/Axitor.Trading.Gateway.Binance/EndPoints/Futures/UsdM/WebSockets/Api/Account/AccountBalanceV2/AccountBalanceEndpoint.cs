using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;

/// <summary>Futures Account Balance V2. Signed endpoint.</summary>
public sealed class AccountBalanceEndpoint<TTelemetry> : BinanceWsApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal AccountBalanceEndpoint(BinanceRouter<TTelemetry> router) : base(router, AccountBalancePayload.Method) { }

    /// <summary>
    /// Futures account balance, one entry per asset.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 5.
    /// </summary>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='WsApiFatalException']/node()"/></exception>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='InvalidOperationException']/node()"/></exception>
    public ValueTask<WsApiRequestResult<AccountBalanceModel>> GetAsync(
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        WsApiEndpointDescriptor descriptor = new()
        {
            Method = _method,
            Weight = 5,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesWsApiRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
        };

        AccountBalancePayload payload = new();

        return _router.SendWsApiSignedAsync<BinanceWsApiEventDispatcher, AccountBalancePayload, AccountBalanceParser, AccountBalanceModel>(
            _router.FuturesUsdMWsApi, descriptor, payload, cancellationToken);
    }
}
