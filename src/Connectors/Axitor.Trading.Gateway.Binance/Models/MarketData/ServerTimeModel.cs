using System.Text.Json.Serialization;

namespace Axitor.Trading.Gateway.Binance.Models.MarketData;

/// <summary>Server time of any market.</summary>
public struct ServerTimeModel
{
    /// <summary>Exchange server time, milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("serverTime")]
    public long ServerTime;
}
