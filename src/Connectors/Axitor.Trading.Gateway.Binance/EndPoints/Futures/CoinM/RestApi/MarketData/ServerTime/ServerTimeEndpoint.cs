using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ServerTime;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Parsing.MarketData;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.RestApi.MarketData.ServerTime;

/// <summary>Server Time. Public endpoint.</summary>
public sealed class ServerTimeEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal ServerTimeEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesCoinRestApi) { }

    /// <summary>
    /// Test connectivity to the Rest API and get the current server time.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Public endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<ServerTimeModel>> GetAsync(bool isRetryable = true, int timeToLiveMs = 0, RequestPriority priority = RequestPriority.Normal, CancellationToken cancellationToken = default)
    {
        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = 1,
            HttpMethod = HttpMethod.Get,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesRequestWeight,
            Market = (int)BinanceMarkets.FuturesCoinM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = false,
            NeedsSignature = false,
        };

        ServerTimePayload payload = new()
        {
            Host = _host,
        };

        return _router.SendRestApiPublicAsync<ServerTimePayload, ServerTimeParser, ServerTimeModel>(descriptor, payload, cancellationToken);
    }
}
