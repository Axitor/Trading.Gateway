namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Status of an algo order in the Algo Service.</summary>
public enum AlgoStatus : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Placed in the Algo Service, not triggered yet.</summary>
    NEW,

    /// <summary>Canceled.</summary>
    CANCELED,

    /// <summary>Trigger condition met, being sent to the matching engine.</summary>
    TRIGGERING,

    /// <summary>Placed in the matching engine.</summary>
    TRIGGERED,

    /// <summary>The triggered order was filled or canceled in the matching engine.</summary>
    FINISHED,

    /// <summary>Rejected by the matching engine, e.g. on a margin check.</summary>
    REJECTED,

    /// <summary>Canceled by the system, e.g. a GTE_GTC order after its position was closed.</summary>
    EXPIRED,
}
