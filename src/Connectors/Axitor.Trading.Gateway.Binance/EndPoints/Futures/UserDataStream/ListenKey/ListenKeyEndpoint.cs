using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UserDataStream.ListenKey;

/// <summary>Start User Data Stream of USD-M or COIN-M. Owned by the user data session; never exposed, so no one closes a key the session uses.</summary>
internal sealed class ListenKeyEndpoint<TTelemetry> : BinanceWsApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry> _pipeline;
    private readonly BinanceRateLimitBucket _limitBucket;
    private readonly BinanceMarkets _market;

    /// <summary>Binds the endpoint to the WebSocket API of the market. Throws InvalidOperationException when it is not enabled.</summary>
    internal ListenKeyEndpoint(BinanceRouter<TTelemetry> router, BinanceMarkets market) : base(router, ListenKeyPayload.Method)
    {
        _market = market;

        // COIN-M WebSocket API weight is charged to the REST window, which USD-M shares.
        if (market == BinanceMarkets.FuturesCoinM)
        {
            _pipeline = router.FuturesCoinMWsApi;
            _limitBucket = BinanceRateLimitBucket.FuturesRequestWeight;
        }
        else
        {
            _pipeline = router.FuturesUsdMWsApi;
            _limitBucket = BinanceRateLimitBucket.FuturesWsApiRequestWeight;
        }
    }

    /// <summary>
    /// Returns the active listen key of the account and extends it for 60 minutes, or a new key when none is active.
    /// Security: USER_STREAM, apiKey only.
    /// Weight: 1.
    /// </summary>
    public ValueTask<WsApiRequestResult<ListenKeyModel>> StartAsync(
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        WsApiEndpointDescriptor descriptor = new()
        {
            Method = _method,
            Weight = 1,
            LimitBucket = (int)_limitBucket,
            Market = (int)_market,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
        };

        ListenKeyPayload payload = new();

        return _router.SendWsApiKeyAsync<BinanceWsApiEventDispatcher, ListenKeyPayload, ListenKeyParser, ListenKeyModel>(
            _pipeline, descriptor, payload, cancellationToken);
    }
}
