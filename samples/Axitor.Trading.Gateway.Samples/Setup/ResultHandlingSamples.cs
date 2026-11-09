using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Samples.Setup;

/// <summary>
/// How to read a result. Calls do not throw on normal failures: check Outcome first, Value is set only on success.
/// Only fatal errors throw, when the client must stop.
/// </summary>
internal static class ResultHandlingSamples
{
    /// <summary>Reading a REST result.</summary>
    public static async Task ReadRestResultAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ServerTimeModel> result = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync().ConfigureAwait(false);

        switch (result.Outcome)
        {
            case RequestOutcome.Success:
                Console.WriteLine($"Server time: {result.Value.ServerTime}");

                // Latency is in nanoseconds.
                Console.WriteLine($"Took {result.Latency.ExecutionFull / 1000} microseconds, network {result.Latency.TransportNetworkOnly / 1000}");
                break;

            case RequestOutcome.Failed:
                // Errors tells why. Retries, if any, already happened inside the call.
                Console.WriteLine($"Failed: {result.Errors.Reason}, HTTP {result.Errors.StatusCode}, exchange code {result.Errors.ExchangeCode}");
                Console.WriteLine($"Retries: {result.Resiliency.RetryNumber}");
                break;

            case RequestOutcome.Canceled:
                // The app is shutting down.
                Console.WriteLine("Canceled");
                break;
        }
    }

    /// <summary>Reading a WebSocket API result. The first call waits until the connection is open.</summary>
    public static async Task ReadWsApiResultAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        WsApiRequestResult<TickerPriceModel> result = await client.Futures.UsdM.WsApi.MarketData.TickerPrice
            .GetAsync(Symbol.Parse("BTCUSDT"))
            .ConfigureAwait(false);

        if (result.Outcome == RequestOutcome.Success)
        {
            Console.WriteLine($"Price: {result.Value.Price}");
            return;
        }

        // Disconnect tells why the connection dropped, when that is the cause.
        Console.WriteLine($"Request {result.RequestId} failed: {result.Errors.Reason}, connection {result.Errors.Disconnect.Reason}");
    }

    /// <summary>
    /// Optional parameters every call takes. Here: no retries, drop the request if it is not sent within 500 ms,
    /// and the critical slots that routine calls cannot take.
    /// The cancellation token is for app shutdown only: canceling a sent request does not cancel it on the exchange.
    /// </summary>
    public static async Task CallParametersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ServerTimeModel> result = await client.Futures.UsdM.RestApi.MarketData.ServerTime
            .GetAsync(isRetryable: false, timeToLiveMs: 500, priority: RequestPriority.Critical)
            .ConfigureAwait(false);

        if (result.Errors.WasDroppedByTtl)
        {
            // Not sent: it waited too long, e.g. for a rate limit.
            Console.WriteLine("Dropped: too late to send");
        }
    }

    /// <summary>
    /// A fatal REST error means the client must stop: a long ban, a bad key, a request the exchange will never accept.
    /// Catch it at the top of your app and stop trading. A ban stays in force even if you catch it.
    /// </summary>
    public static async Task CatchRestFatalAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        try
        {
            RestApiRequestResult<AccountInfoV2Model> result = await client.Futures.UsdM.RestApi.Account.AccountInfoV2.GetAsync().ConfigureAwait(false);
            Console.WriteLine($"Outcome: {result.Outcome}");
        }
        catch (RestApiFatalException exception)
        {
            Console.WriteLine($"Stop the app: {exception.Errors.Reason}, HTTP {exception.Errors.StatusCode}, exchange code {exception.Errors.ExchangeCode}");
        }
    }

    /// <summary>
    /// A fatal WebSocket API error: the WebSocket API of that market stops for good.
    /// A call to a market left out of BinanceOptions.WsApiMarkets throws InvalidOperationException.
    /// </summary>
    public static async Task CatchWsApiFatalAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        try
        {
            WsApiRequestResult<AccountBalanceModel> result = await client.Futures.UsdM.WsApi.Account.AccountBalance.GetAsync().ConfigureAwait(false);
            Console.WriteLine($"Outcome: {result.Outcome}");
        }
        catch (WsApiFatalException exception)
        {
            Console.WriteLine($"Stop the app: {exception.Errors.Reason}, connection {exception.Errors.Disconnect.Reason}");
        }
    }
}
