namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Side of an order, as the exchange reports it.</summary>
public enum OrderSide : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Buy.</summary>
    BUY,

    /// <summary>Sell.</summary>
    SELL,
}
