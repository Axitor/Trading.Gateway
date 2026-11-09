namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>What happened to an order in this update.</summary>
public enum ExecutionType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Accepted.</summary>
    NEW,

    /// <summary>Canceled.</summary>
    CANCELED,

    /// <summary>Liquidation execution.</summary>
    CALCULATED,

    /// <summary>Expired.</summary>
    EXPIRED,

    /// <summary>Filled, fully or in part.</summary>
    TRADE,

    /// <summary>Modified.</summary>
    AMENDMENT,
}
