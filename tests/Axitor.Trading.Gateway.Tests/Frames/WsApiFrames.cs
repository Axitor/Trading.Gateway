using System.Globalization;
using System.Text;

namespace Axitor.Trading.Gateway.Tests.Frames;

/// <summary>WebSocket API responses and events as the exchange sends them.</summary>
internal static class WsApiFrames
{
    /// <summary>Server time in the error data of a ban; the ban lasts from it to retryAfter.</summary>
    public const long ServerTimeMillis = 1659142907531;

    /// <summary>Spot userDataStream.unsubscribe result.</summary>
    public const string SpotUnsubscribed = "{}";

    /// <summary>The event spot sends before it drops a connection.</summary>
    public const string ServerShutdown = """{"e":"serverShutdown","E":1764079200000}""";

    // Every response reports the weight used in the current window.
    private const string RateLimits = """[{"rateLimitType":"REQUEST_WEIGHT","interval":"MINUTE","intervalNum":1,"limit":2400,"count":1}]""";

    /// <summary>USD-M order.place result: the same order as the REST answer.</summary>
    public static string FuturesOrder { get; } = Encoding.UTF8.GetString(RestBodies.FuturesOrder);

    /// <summary>USD-M ticker.price result for the symbol.</summary>
    public static string FuturesTickerPrice(string symbol) => "{\"symbol\":\"" + symbol + "\",\"price\":\"6000.01\",\"time\":1589437530011}";

    /// <summary>Futures userDataStream.start result with the key.</summary>
    public static string ListenKey(string listenKey) => "{\"listenKey\":\"" + listenKey + "\"}";

    /// <summary>Spot userDataStream.subscribe.signature result with the subscription id.</summary>
    public static string SpotSubscription(int subscriptionId) => "{\"subscriptionId\":" + Number(subscriptionId) + "}";

    /// <summary>A spot user data event in its envelope.</summary>
    public static string SpotEvent(int subscriptionId, string data) => "{\"subscriptionId\":" + Number(subscriptionId) + ",\"event\":" + data + "}";

    /// <summary>An event that belongs to no subscription, e.g. serverShutdown.</summary>
    public static string Event(string data) => "{\"event\":" + data + "}";

    /// <summary>A success carrying the result. A zero id is written as null.</summary>
    public static string Success(long id, string result) =>
        "{\"id\":" + Id(id) + ",\"status\":200,\"result\":" + result + ",\"rateLimits\":" + RateLimits + "}";

    /// <summary>An error. A ban length above zero adds the data object with serverTime and retryAfter.</summary>
    public static string Failure(long id, int statusCode, int code, string message, long retryAfterMillis)
    {
        string data = 0 < retryAfterMillis
            ? ",\"data\":{\"serverTime\":" + Number(ServerTimeMillis) + ",\"retryAfter\":" + Number(ServerTimeMillis + retryAfterMillis) + "}"
            : string.Empty;

        return "{\"id\":" + Id(id) + ",\"status\":" + Number(statusCode) + ",\"error\":{\"code\":" + Number(code) + ",\"msg\":\"" + message + "\"" + data
            + "},\"rateLimits\":" + RateLimits + "}";
    }

    private static string Id(long id) => id == 0 ? "null" : Number(id);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
