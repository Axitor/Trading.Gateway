namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>How long an order stays open.</summary>
public enum TimeInForce : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Good till canceled.</summary>
    GTC,

    /// <summary>Immediate or cancel.</summary>
    IOC,

    /// <summary>Fill or kill.</summary>
    FOK,

    /// <summary>Post only: rejected if it would take liquidity.</summary>
    GTX,

    /// <summary>Good till date.</summary>
    GTD,

    /// <summary>Retail Price Improvement: post only, matched only with orders from the app or web.</summary>
    RPI,
}
