using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.WebSockets.Streams.UserData;

/// <summary>User Data Streams.</summary>
public sealed class UserDataStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    // COIN-M documents no TRADE_LITE or ALGO_UPDATE.
    private const BinanceUserDataEvents SupportedEvents =
        BinanceUserDataEvents.OrderTradeUpdate
        | BinanceUserDataEvents.AccountUpdate
        | BinanceUserDataEvents.AccountConfigUpdate
        | BinanceUserDataEvents.MarginCall;

    private readonly BinanceRouter<TTelemetry> _router;

    internal UserDataStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for the user data of the account. Its connection opens on SubscribeAsync.
    /// COIN-M sends no TRADE_LITE or ALGO_UPDATE: subscribing them throws ArgumentException.
    /// Note: Throws InvalidOperationException when COIN-M futures are left out of BinanceOptions.StreamMarkets or BinanceOptions.WsApiMarkets.
    /// </summary>
    /// <param name="handler">Receives the events on the receive loop: must return fast and never throw.</param>
    public BinanceUserDataSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceUserDataHandler =>
        _router.CreateUserDataSession(BinanceMarkets.FuturesCoinM, _router.Options.Uri.FuturesCoinWebSocketStream, SupportedEvents, handler);
}
