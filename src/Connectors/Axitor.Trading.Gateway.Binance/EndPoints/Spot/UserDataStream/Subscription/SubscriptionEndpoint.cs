using System.Text;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;

/// <summary>Spot user data subscription on one WebSocket API connection. Owned by the user data session; never exposed.</summary>
/// <remarks>
/// DESIGN NOTE: Both requests go strictly to the connection of the ticket, with no retries: a subscription lives on its connection,
/// and the session decides what a failure means for it.
/// </remarks>
internal sealed class SubscriptionEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly BinanceRouter<TTelemetry> _router;

    // Method names for telemetry, built once.
    private readonly string _subscribeMethod;
    private readonly string _unsubscribeMethod;

    internal SubscriptionEndpoint(BinanceRouter<TTelemetry> router)
    {
        _router = router;
        _subscribeMethod = Encoding.UTF8.GetString(SubscribePayload.Method);
        _unsubscribeMethod = Encoding.UTF8.GetString(UnsubscribePayload.Method);
    }

    /// <summary>
    /// Subscribes the user data of the account on the ticket's connection; the answer carries the subscription id.
    /// Security: USER_STREAM, signed.
    /// Weight: 2.
    /// </summary>
    public ValueTask<WsApiRequestResult<SubscriptionModel>> SubscribeAsync<TEventDispatcher>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiConnectionTicket ticket,
        int timeToLiveMs = 0,
        CancellationToken cancellationToken = default)
        where TEventDispatcher : struct, IWsApiEventDispatcher
    {
        WsApiEndpointDescriptor descriptor = new()
        {
            Method = _subscribeMethod,
            Weight = 2,
            LimitBucket = (int)BinanceRateLimitBucket.SpotWsApiRequestWeight,
            Market = (int)BinanceMarkets.Spot,
            Priority = RequestPriority.Normal,
            TTL = timeToLiveMs,
            IsRetryable = false,
        };

        SubscribePayload payload = new();

        return _router.SendWsApiSignedOnAsync<TEventDispatcher, SubscribePayload, SubscriptionParser, SubscriptionModel>(
            pipeline, ticket, descriptor, payload, cancellationToken);
    }

    /// <summary>
    /// Ends every user data subscription on the ticket's connection. The exchange sends eventStreamTerminated before the answer.
    /// Security: none.
    /// Weight: 2.
    /// </summary>
    public ValueTask<WsApiRequestResult<SubscriptionModel>> UnsubscribeAsync<TEventDispatcher>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiConnectionTicket ticket,
        int timeToLiveMs = 0,
        CancellationToken cancellationToken = default)
        where TEventDispatcher : struct, IWsApiEventDispatcher
    {
        WsApiEndpointDescriptor descriptor = new()
        {
            Method = _unsubscribeMethod,
            Weight = 2,
            LimitBucket = (int)BinanceRateLimitBucket.SpotWsApiRequestWeight,
            Market = (int)BinanceMarkets.Spot,
            Priority = RequestPriority.Normal,
            TTL = timeToLiveMs,
            IsRetryable = false,
        };

        UnsubscribePayload payload = new();

        return _router.SendWsApiPublicOnAsync<TEventDispatcher, UnsubscribePayload, SubscriptionParser, SubscriptionModel>(
            pipeline, ticket, descriptor, payload, cancellationToken);
    }
}
