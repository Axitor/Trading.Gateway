using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Samples.Handlers;

namespace Axitor.Trading.Gateway.Samples.Spot;

/// <summary>
/// Spot WebSocket API. Needs Spot in BinanceOptions.WsApiMarkets.
/// A call on a market left out of the options throws InvalidOperationException.
/// </summary>
internal static class SpotWsApiSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Latest price of one symbol. Public: no keys needed. The first call waits until the connection is open.</summary>
    public static async Task TickerPriceAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        WsApiRequestResult<TickerPriceModel> result = await client.Spot.WsApi.MarketData.TickerPrice.GetAsync(_symbol).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"{result.Value.Symbol}: {result.Value.Price}" : $"Ticker price failed: {result.Errors.Reason}");
    }

    /// <summary>
    /// Your spot orders and balances in real time, on their own connection. Signed: the API key needs spot access.
    /// Create the session once and keep it as long as the client. Every event comes: spot has no event filter.
    /// </summary>
    public static async Task UserDataAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(new SampleSpotUserDataHandler());

        // Returns once the exchange answered: until then it waits for the connection and asks again.
        WsStreamSubscribeStatus status = await session.SubscribeAsync(appToken).ConfigureAwait(false);

        if (status == WsStreamSubscribeStatus.Rejected)
        {
            // The exchange refused it, e.g. the key has no spot access. Nothing is subscribed.
            Console.WriteLine("User data rejected");
            return;
        }

        // Load open orders and balances now, after subscribing, so no change falls between them and the events.
        Console.WriteLine($"User data: {status}");

        // To stop the events later: await session.UnsubscribeAsync().
        // Session.Completion faults with WsApiFatalException once the session stops for good.
    }
}
