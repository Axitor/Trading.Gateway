using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.UserData;

/// <summary>User Data Stream through the WebSocket API.</summary>
public sealed class UserDataStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly BinanceRouter<TTelemetry> _router;

    internal UserDataStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for the user data of the account on its own WebSocket API connection, which opens at once. Events flow after SubscribeAsync.
    /// Needs an API key with spot access.
    /// Note: Throws InvalidOperationException when spot is left out of BinanceOptions.WsApiMarkets.
    /// </summary>
    /// <param name="handler">Receives the events on the receive loop: must return fast and never throw.</param>
    public BinanceSpotUserDataSession CreateSession<THandler>(THandler handler) where THandler : struct, IBinanceSpotUserDataHandler =>
        _router.CreateSpotUserDataSession(handler);
}
