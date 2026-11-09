using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Samples.Handlers;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>
/// USD-M futures user data: your orders, fills, balances and positions in real time.
/// Signed: the client needs an API key and secret. Needs FuturesUsdM in both WsApiMarkets and StreamMarkets, which is the default.
/// </summary>
internal static class UsdMUserDataSamples
{
    /// <summary>
    /// Subscribes the events you need. The listen key is created, kept alive and renewed by the session.
    /// Create the session once and keep it as long as the client.
    /// </summary>
    public static async Task UserDataAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(new SampleUserDataHandler());

        // The exchange sends only these events.
        // A fill comes in both TradeLite and OrderTradeUpdate: count fills from one of them only.
        BinanceUserDataEvents events =
            BinanceUserDataEvents.OrderTradeUpdate
            | BinanceUserDataEvents.AccountUpdate
            | BinanceUserDataEvents.AlgoUpdate
            | BinanceUserDataEvents.AccountConfigUpdate
            | BinanceUserDataEvents.MarginCall;

        WsStreamSubscribeStatus status = await session.SubscribeAsync(events, appToken).ConfigureAwait(false);

        if (status == WsStreamSubscribeStatus.Rejected)
        {
            Console.WriteLine("User data subscribe rejected");
            return;
        }

        // Load open orders and balances now, after subscribing, so no change falls between them and the events.
        Console.WriteLine($"User data: {status}");

        // To stop the events later: await session.UnsubscribeAsync(). The listen key stays open for other apps of the account.
    }
}
